using System.Threading;
using System.Threading.Tasks;

namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>
/// F-13 portable (Linux) counterpart of MediaPlayer/SubtitlesOCR.cs, which depends on GDI+ (System.Drawing.Common,
/// Windows-only since .NET 7) and WinRT <c>Windows.Media.Ocr</c>. Same public surface and per-track engine ownership
/// (HC-36/HC-37); images are passed to engines as <see cref="BgraBitmap"/>. No OCR engine ships for Linux yet: hosts
/// may plug one in with <see cref="ServiceFactory"/>, otherwise <see cref="TryInitialize"/> reports an actionable error.
/// </summary>
public unsafe class SubtitlesOCR : IDisposable
{
    /// <summary>
    /// Creates the OCR engine for a track (null when the engine type is not available on this platform).
    /// </summary>
    public static Func<Config.SubtitlesConfig, SubOCREngineType, IOCRService?>? ServiceFactory { get; set; }

    private readonly Config.SubtitlesConfig _config;

    private readonly CancellationTokenSource?[] _ctss;
    private readonly object[] _lockers;
    private readonly OcrEngineSlots _engines;

    public SubtitlesOCR(Config.SubtitlesConfig config, int subNum)
    {
        _config = config;

        _lockers = new object[subNum];
        _ctss = new CancellationTokenSource[subNum];
        _engines = new OcrEngineSlots(subNum);
        for (int i = 0; i < subNum; i++)
        {
            _lockers[i] = new object();
            _ctss[i] = new CancellationTokenSource();
        }
    }

    /// <summary>
    /// Try to initialize OCR Engine
    /// </summary>
    /// <param name="lang">OCR Language</param>
    /// <param name="err">expected initialize error</param>
    /// <returns>whether to success to initialize</returns>
    public bool TryInitialize(int subIndex, Language lang, out string err)
    {
        lang = GetLanguageWithFallback(subIndex, lang);

        SubOCREngineType engineType = _config[subIndex].OCREngine;
        IOCRService? ocrService = ServiceFactory?.Invoke(_config, engineType);
        if (ocrService == null)
        {
            err = $"Bitmap subtitle OCR ({engineType}) is not available on this platform yet.";
            return false;
        }

        if (!ocrService.TryInitialize(lang, out err))
        {
            ocrService.Dispose();
            return false;
        }

        TryCancelWait(subIndex);
        lock (_lockers[subIndex])
        {
            _engines.Install(subIndex, ocrService);
        }

        err = "";
        return true;
    }

    /// <summary>
    /// Do OCR
    /// </summary>
    /// <param name="subIndex">0: Primary, 1: Secondary</param>
    /// <param name="subs">List of subtitle data for OCR</param>
    /// <param name="startTime">Timestamp to start OCR</param>
    public void Do(int subIndex, List<SubtitleData> subs, TimeSpan? startTime = null)
    {
        if (subs.Count == 0 || !subs[0].IsBitmap)
            return;

        // Cancel preceding OCR
        TryCancelWait(subIndex);

        lock (_lockers[subIndex])
        {
            IOCRService? ocrService = _engines.Get(subIndex);
            if (ocrService == null)
                throw new InvalidOperationException("ocrService is not initialized. you must call TryInitialize() first");

            _ctss[subIndex] = new CancellationTokenSource();

            int startIndex = 0;
            // Start OCR from the current playback point
            if (startTime.HasValue)
            {
                int match = subs.FindIndex(s => s.StartTime >= startTime);
                if (match != -1)
                {
                    // Do from 5 previous subtitles
                    startIndex = Math.Max(0, match - 5);
                }
            }

            for (int i = 0; i < subs.Count; i++)
            {
                if (_ctss[subIndex]!.Token.IsCancellationRequested)
                {
                    foreach (var sub in subs)
                    {
                        sub.Dispose();
                    }

                    break;
                }

                int index = (startIndex + i) % subs.Count;

                SubtitleBitmapData? bitmap = subs[index].Bitmap;
                if (bitmap == null)
                    continue;

                subs[index].Text = Process(ocrService, subIndex, bitmap);
                if (!string.IsNullOrEmpty(subs[index].Text))
                {
                    // If OCR succeeds, dispose of it (if it fails, leave it so that it can be displayed in the sidebar).
                    subs[index].Dispose();
                }
            }

            if (!_ctss[subIndex]!.Token.IsCancellationRequested)
            {
                Utils.PlayCompletionSound();
            }
        }
    }

    private Language GetLanguageWithFallback(int subIndex, Language lang)
    {
        if (lang == Language.Unknown)
        {
            // fallback to user set language
            lang = subIndex == 0 ? _config.LanguageFallbackPrimary : _config.LanguageFallbackSecondary;
        }

        return lang;
    }

    public void TryCancelWait(int subIndex)
    {
        CancellationTokenSource? cts = CtsGuard.CancelCaptured(ref _ctss[subIndex]);
        if (cts == null)
            return;

        // Wait until it is canceled by taking a lock
        lock (_lockers[subIndex])
        {
            CtsGuard.TryDisposeAndClear(ref _ctss[subIndex], cts);
        }
    }

    public static string Process(IOCRService ocrService, int subIndex, SubtitleBitmapData sub)
    {
        (byte[] data, AVSubtitleRect rect) = sub.SubToBitmap(true);

        int width = rect.w;
        int height = rect.h;

        fixed (byte* ptr = data)
        {
            Binarize(width, height, ptr, 4, true);
        }

        // Same preprocessing as the Windows build (ImageProcessor.BlackText + AddPadding(20)), in managed code
        BgraBitmap ocrBitmap = OcrImageProcessor.AddPadding(OcrImageProcessor.BlackText(new BgraBitmap(data, width, height)), 20);

        string ocrText = ocrService.RecognizeTextAsync(ocrBitmap).GetAwaiter().GetResult();
        string processedText = ocrService.PostProcess(ocrText);

        return processedText;
    }

    private static void Binarize(int width, int height, byte* buffer, int pixelByte, bool srcTextWhite)
    {
        // Black text on white background
        byte white = 255;
        byte black = 0;
        if (srcTextWhite)
        {
            // The text is white on a black background, so invert it to black text.
            white = 0;
            black = 255;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte* pixel = buffer + (y * width + x) * pixelByte;
                int grayscale = pixel[0];
                byte binaryValue = grayscale < 128 ? black : white;
                pixel[0] = pixel[1] = pixel[2] = binaryValue;
            }
        }
    }

    public void Reset(int subIndex)
    {
        TryCancelWait(subIndex);
    }

    /// <summary>
    /// Frees the per-track OCR engines held for reuse across Do() calls. Idempotent.
    /// </summary>
    public void Dispose()
    {
        for (int subIndex = 0; subIndex < _lockers.Length; subIndex++)
        {
            TryCancelWait(subIndex);
            lock (_lockers[subIndex])
            {
                _engines.Clear(subIndex);
            }
        }
    }
}

/// <summary>OCR engine contract (portable build: images are BGRA32 <see cref="BgraBitmap"/>s).</summary>
public interface IOCRService : IDisposable
{
    bool TryInitialize(Language lang, out string err);
    Task<string> RecognizeTextAsync(BgraBitmap bitmap);
    string PostProcess(string text);
}

/// <summary>Managed equivalents of the Windows GDI+ ImageProcessor used before OCR.</summary>
public static class OcrImageProcessor
{
    /// <summary>Composites the image over an opaque white background (source-over alpha blending).</summary>
    public static BgraBitmap BlackText(BgraBitmap original)
    {
        byte[] src = original.Data;
        byte[] dst = new byte[original.Width * original.Height * 4];

        for (int i = 0; i < dst.Length; i += 4)
        {
            int a = src[i + 3];
            dst[i + 0] = (byte)((src[i + 0] * a + 255 * (255 - a) + 127) / 255);
            dst[i + 1] = (byte)((src[i + 1] * a + 255 * (255 - a) + 127) / 255);
            dst[i + 2] = (byte)((src[i + 2] * a + 255 * (255 - a) + 127) / 255);
            dst[i + 3] = 255;
        }

        return new(dst, original.Width, original.Height, original.DpiX, original.DpiY);
    }

    /// <summary>Adds a white border of <paramref name="padding"/> pixels around the image.</summary>
    public static BgraBitmap AddPadding(BgraBitmap original, int padding)
    {
        int newWidth  = original.Width  + padding * 2;
        int newHeight = original.Height + padding * 2;
        byte[] dst = new byte[newWidth * newHeight * 4];
        Array.Fill(dst, (byte)255);

        for (int y = 0; y < original.Height; y++)
            Buffer.BlockCopy(original.Data, y * original.Stride, dst, ((y + padding) * newWidth + padding) * 4, original.Stride);

        return new(dst, newWidth, newHeight, original.DpiX, original.DpiY);
    }
}
