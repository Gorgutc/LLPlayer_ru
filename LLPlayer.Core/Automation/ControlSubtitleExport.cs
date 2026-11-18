using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaPlayer;
using LLPlayer.ControlApi;

namespace LLPlayer.Core.Automation;

public enum ControlSubtitleText { Original, TranslatedOnly, TranslatedWithFallback }

public static class ControlSubtitleExport
{
    public static Task WriteAsync(ControlCueDocument document, string path, SubtitleExportFormat format,
        ControlSubtitleText text, bool overwrite, bool includeUtf8Bom, bool includeSpeakerLabels, CancellationToken token) =>
        Prepare(document, format, text, includeUtf8Bom, includeSpeakerLabels).WriteAsync(path, overwrite, token);

    public static ControlPreparedTextExport Prepare(ControlCueDocument document, SubtitleExportFormat format,
        ControlSubtitleText text, bool includeUtf8Bom, bool includeSpeakerLabels)
    {
        if (!Enum.IsDefined(format) || !Enum.IsDefined(text) || (includeSpeakerLabels && format != SubtitleExportFormat.Srt))
            throw new ControlCueException("invalid_arguments", "The subtitle text mode, format or speaker label option is invalid.");
        var lines = new List<SubtitleExportLine>();
        foreach (ControlCue cue in document.Cues)
        {
            if (string.IsNullOrEmpty(cue.Text)) continue;
            string? content = text switch
            {
                ControlSubtitleText.Original => cue.Text,
                ControlSubtitleText.TranslatedOnly => cue.TranslatedText,
                _ => string.IsNullOrEmpty(cue.TranslatedText) ? cue.Text : cue.TranslatedText,
            };
            if (string.IsNullOrEmpty(content)) continue;
            var styles = text == ControlSubtitleText.Original
                ? cue.Styles.Select(style => new SubStyle(Enum.Parse<SubStyles>(style.Kind), style.Start, style.Length,
                    Color.FromArgb(style.Argb))).ToArray()
                : null;
            lines.Add(new(cue.Start, cue.End, content, styles) { SpeakerId = cue.SpeakerId });
        }
        if (lines.Count == 0) throw new ControlCueException("no_text_cues", "There are no matching subtitle cues to export.");
        return new(SubtitleExporter.Build(lines, format, includeSpeakerLabels), includeUtf8Bom);
    }
}

public sealed class ControlPreparedTextExport(string content, bool includeUtf8Bom)
{
    private readonly Encoding encoding = new UTF8Encoding(includeUtf8Bom);

    /// <summary>Exact bytes of the intended publication, including the optional BOM.</summary>
    public string ComputeSha256()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(encoding.GetPreamble());
        hash.AppendData(encoding.GetBytes(content));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public async Task WriteAsync(string path, bool overwrite, CancellationToken token)
    {
        try { await AtomicFile.WriteAllTextAsync(path, content, encoding, overwrite, token).ConfigureAwait(false); }
        catch (IOException) when (!overwrite && File.Exists(path))
        {
            throw new ControlCueException("output_exists", "The output file already exists. Set overwrite explicitly to replace it.");
        }
    }

    public async Task PublishAsync(ControlJobContext context, string path, bool overwrite, CancellationToken token,
        string effectId = "subtitle", Action<string>? validatePublication = null)
    {
        string destination = Path.GetFullPath(path);
        string stage = Path.Combine(Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{context.JobId:N}.{Guid.NewGuid():N}.tmp");
        bool ownsStage = false;
        try
        {
            validatePublication?.Invoke(stage);
            await WriteAsync(stage, overwrite: false, token).ConfigureAwait(false);
            ownsStage = true;
            await context.PublishFileAsync(effectId, stage, destination, overwrite, token, validatePublication)
                .ConfigureAwait(false);
        }
        finally
        {
            if (ownsStage)
            {
                try
                {
                    if (validatePublication is null || ControlPaths.IsPhysicalPath(stage, allowMissingLeaf: true))
                        File.Delete(stage);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
