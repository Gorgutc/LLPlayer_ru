#if !WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FlyleafLib.MediaFramework.MediaPlaylist;
using static FlyleafLib.Utils;

namespace FlyleafLib.Plugins;

public partial class YoutubeDL
{
    readonly object publicationLock = new();
    long generation;
    internal Func<ProcessStartInfo, OnlineVideoProcess> StartHelper { get; init; } = OnlineVideoProcess.Start;
    static readonly JsonSerializerOptions portableJson = new() { Converters = { new OptionalNumberConverter() } };

    // Extractors omit unknown measurements or emit null. The legacy mapping uses zero
    // for omitted numbers; normalize the other spelling while still rejecting bad types.
    sealed class OptionalNumberConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? 0 : reader.GetDouble();
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }

    public bool CanOpen() => Playlist.IOStream == null && OnlineVideoRequest.TryGetAdmittedUrl(Playlist.Url, out _);

    void DisposeInternal()
    {
        lock (publicationLock)
        {
            Interlocked.Increment(ref generation);
            Disposed = true;
        }
    }

    public OpenResults Open()
    {
        if (!OnlineVideoRequest.TryGetAdmittedUrl(Playlist.Url, out string url))
            return new("Invalid online video URL.");

        long session = Handler.OpenCounter;
        var token = OnlineVideoRequest.CurrentCancellation;
        long run;
        lock (publicationLock)
        {
            if (token.IsCancellationRequested || Handler.Interrupt || session != Handler.OpenCounter)
                return new("Cancelled");
            run = Interlocked.Increment(ref generation);
            Disposed = false;
        }
        bool Cancelled() => token.IsCancellationRequested || Handler.Interrupt ||
            Handler.OpenCounter != session || Interlocked.Read(ref generation) != run;

        try
        {
            string helper = Options[OnlineVideoRequest.YtDlpPath];
            string deno = Options[OnlineVideoRequest.DenoPath];
            if (!OnlineVideoRequest.IsExecutablePath(helper) || !OnlineVideoRequest.IsExecutablePath(deno))
                return new("Configure absolute paths to the yt-dlp and Deno executables in Settings.");
            if (Cancelled())
                return new("Cancelled");

            var data = Extract(CreateStartInfo(helper, deno, url), url, Cancelled);
            var items = data.Items.Select(metadata => (Item: BuildItem(metadata, Cancelled), Metadata: metadata))
                .Where(entry => entry.Item != null).ToList();
            if (items.Count == 0)
                return new(Cancelled() ? "Cancelled" : "No playable streams were found.");

            // AddItem registers collections synchronously with the UI. Post first, then
            // acquire the generation lock on the dispatcher so Stop never waits on itself.
            TaskCompletionSource<OpenResults> published = new(TaskCreationOptions.RunContinuationsAsynchronously);
            UIIfRequired(() =>
            {
                try
                {
                    lock (publicationLock)
                    {
                        if (Cancelled()) { published.SetResult(new("Cancelled")); return; }
                        Playlist.InputType = InputType.Web;
                        Playlist.Title = data.Title;
                        Playlist.ExpectingItems = data.ExpectedCount;
                        foreach (var entry in items)
                        {
                            if (Cancelled()) { published.SetResult(new("Cancelled")); return; }
                            AddPlaylistItem(entry.Item, entry.Metadata);
                        }
                        Handler.OnPlaylistCompleted();
                        published.SetResult(new());
                    }
                }
                catch (Exception error) { published.SetException(error); }
            });
            while (!published.Task.IsCompleted)
            {
                if (Cancelled())
                {
                    Interlocked.CompareExchange(ref generation, run + 1, run);
                    return new("Cancelled");
                }
                Thread.Sleep(15);
            }
            return published.Task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { return new("Cancelled"); }
        catch (Exception e) { return new($"Online video extraction failed: {e.Message}"); }
    }

    ProcessStartInfo CreateStartInfo(string helper, string deno, string url)
    {
        ProcessStartInfo start = new(helper)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        // A single overflow sentinel makes an oversized default playlist fail visibly while
        // bounding extractor work before it writes JSON. Explicit validated ranges override it.
        start.ArgumentList.Add("--playlist-items"); start.ArgumentList.Add("1:1001");
        if (Options.TryGetValue("ExtraArguments", out string extras))
            foreach (string argument in OnlineVideoArguments.Parse(extras)) start.ArgumentList.Add(argument);
        // Invariant flags follow user options. No watcher, writable metadata path or quoted command line.
        foreach (string arg in new[] { "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-js-runtimes",
            "--js-runtimes", "deno:" + deno, "--no-remote-components", "--skip-download",
            "--dump-single-json", "--encoding", "utf-8", "--", url })
            start.ArgumentList.Add(arg);
        return start;
    }

    PortablePlaylistData Extract(ProcessStartInfo start, string requestedUrl, Func<bool> cancelled)
    {
        using var process = StartHelper(start);
        var json = ReadBoundedMetadataAsync(process.Output);
        var stderr = process.Error.CopyToAsync(Stream.Null);
        while (!process.WaitForExit(35))
        {
            if (cancelled())
                throw new OperationCanceledException();
            if (json.IsFaulted)
                json.GetAwaiter().GetResult();
        }
        if (cancelled())
            throw new OperationCanceledException();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"yt-dlp exited with code {process.ExitCode}.");

        // Release any descendants holding inherited pipes before waiting for EOF.
        process.Stop();
        var drain = Stopwatch.StartNew();
        while (!json.IsCompleted || !stderr.IsCompleted)
        {
            if (cancelled())
                throw new OperationCanceledException();
            if (drain.Elapsed > TimeSpan.FromSeconds(2))
                throw new IOException("The extractor did not close its output.");
            Thread.Sleep(15);
        }
        stderr.GetAwaiter().GetResult();
        return ParsePortablePlaylist(json.GetAwaiter().GetResult(), requestedUrl);
    }

    static async Task<string> ReadBoundedMetadataAsync(Stream output)
    {
        using MemoryStream buffer = new();
        byte[] block = new byte[16384];
        int count;
        while ((count = await output.ReadAsync(block).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MaximumMetadataBytes)
                throw new IOException("Online metadata exceeds the 32 MiB limit. Select a smaller playlist range.");
            buffer.Write(block, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
#endif
