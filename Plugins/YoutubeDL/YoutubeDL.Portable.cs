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

            var data = Extract(CreateStartInfo(helper, deno, url), Cancelled);
            if (data == null || data._type == "playlist" || data._type == "multi_video")
                return new("The extractor did not return a single video.");

            // A URL-less root object is not a single-format stream. Reject non-web results from
            // generic extractors too; playback must never reinterpret web metadata as local files.
            data.formats = (data.formats ?? [data]).Where(f => f != null && IsSafeProcessUrl(f.url)).ToList();
            if (!IsSafeProcessUrl(data.webpage_url)) data.webpage_url = url;
            if (!IsSafeProcessUrl(data.manifest_url)) data.manifest_url = null;
            foreach (var format in data.formats)
                if (!IsSafeProcessUrl(format.manifest_url)) format.manifest_url = null;
            if (data.automatic_captions != null)
                foreach (var subtitles in data.automatic_captions.Values)
                    subtitles?.RemoveAll(s => !IsSafeProcessUrl(s?.url));

            var item = BuildItem(data, Cancelled);
            if (item == null)
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
                        AddPlaylistItem(item, data);
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

    static ProcessStartInfo CreateStartInfo(string helper, string deno, string url)
    {
        ProcessStartInfo start = new(helper)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        // Dump one JSON object to stdout. No watcher, writable metadata path or quoted command line.
        foreach (string arg in new[] { "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-js-runtimes",
            "--js-runtimes", "deno:" + deno, "--no-remote-components", "--no-playlist", "--playlist-end", "1", "--skip-download",
            "--dump-single-json", "--encoding", "utf-8", "--", url })
            start.ArgumentList.Add(arg);
        return start;
    }

    YoutubeDLJson Extract(ProcessStartInfo start, Func<bool> cancelled)
    {
        using var process = StartHelper(start);
        var json = JsonSerializer.DeserializeAsync<YoutubeDLJson>(process.Output, portableJson).AsTask();
        var stderr = process.Error.CopyToAsync(Stream.Null);
        while (!process.WaitForExit(35))
        {
            if (cancelled())
                throw new OperationCanceledException();
            if (json.IsFaulted)
                return json.GetAwaiter().GetResult();
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
        return json.GetAwaiter().GetResult();
    }
}
#endif
