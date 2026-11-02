#if LLPLAYER_WINDOWS_OCR
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace LLPlayer.Avalonia.Services;

/// <summary>Lazy Windows OCR adapter over the portable BGRA contract. Never installs Windows language packs.</summary>
[SupportedOSPlatform("windows10.0.18362")]
public sealed class MicrosoftOcrService(Config.SubtitlesConfig config) : IOCRService
{
    readonly object gate = new();
    OcrEngine? engine;
    string languageTag = "";
    bool disposed;

    public static IReadOnlyList<string> AvailableLanguageTags
    {
        get
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362)) return [];
            try { return OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag).ToArray(); }
            catch (Exception e) when (IsActivationFailure(e)) { return []; }
        }
    }

    public bool TryInitialize(Language language, out string error)
    {
        lock (gate)
        {
            if (disposed) { error = "Microsoft OCR service is disposed."; return false; }
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            { error = "Microsoft OCR requires Windows 10 version 1903 or later; use Tesseract on Linux."; return false; }
            try
            {
                config.MsOcrRegions.TryGetValue(language.ISO6391, out string? preferred);
                string? tag = MicrosoftOcrLanguagePolicy.SelectTag(language.ISO6391, preferred, AvailableLanguageTags);
                if (tag == null)
                {
                    error = $"Microsoft OCR language {language.TopEnglishName} ({preferred ?? language.ISO6391}) is unavailable. Install its Windows OCR language feature. Windows may require package identity for OCR in this desktop environment.";
                    return false;
                }
                OcrEngine? candidate = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag));
                if (candidate == null)
                { error = $"Microsoft OCR could not activate '{tag}'. Check the installed Windows OCR language feature and desktop package-identity support."; return false; }
                engine = candidate;
                languageTag = tag;
                error = "";
                return true;
            }
            catch (Exception e) when (IsActivationFailure(e))
            { error = $"Microsoft OCR initialization failed: {e.Message}. Check installed Windows OCR language features and desktop package-identity support."; return false; }
        }
    }

    public async Task<string> RecognizeTextAsync(BgraBitmap bitmap)
    {
        OcrEngine current;
        string tag;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            current = engine ?? throw new InvalidOperationException("Initialize the Microsoft OCR language before recognition.");
            tag = languageTag;
        }
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || bitmap.Width > OcrEngine.MaxImageDimension || bitmap.Height > OcrEngine.MaxImageDimension)
            throw new ArgumentException($"Microsoft OCR image dimensions must be from 1 to {OcrEngine.MaxImageDimension} pixels.", nameof(bitmap));
        try
        {
            using SoftwareBitmap image = new(BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Ignore);
            using DataWriter writer = new();
            writer.WriteBytes(bitmap.Data);
            image.CopyFromBuffer(writer.DetachBuffer());
            OcrResult result = await current.RecognizeAsync(image);
            return MicrosoftOcrLanguagePolicy.FormatLines(tag,
                result.Lines.Select(line => (line.Text, (IReadOnlyList<string>)line.Words.Select(word => word.Text).ToArray())));
        }
        catch (Exception e) when (IsActivationFailure(e))
        { throw new InvalidOperationException($"Microsoft OCR failed for '{tag}': {e.Message}", e); }
    }

    public string PostProcess(string text) => text;
    public void Dispose() { lock (gate) { disposed = true; engine = null; } }
    static bool IsActivationFailure(Exception error) => error is COMException or TypeInitializationException or TypeLoadException
        or DllNotFoundException or FileNotFoundException or BadImageFormatException or UnauthorizedAccessException
        or NotSupportedException or NotImplementedException or InvalidOperationException or ArgumentException;
}
#endif
