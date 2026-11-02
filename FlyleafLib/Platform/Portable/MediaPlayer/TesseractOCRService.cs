using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>Resident, per-track Tesseract 5 engine with managed, legacy and system language discovery.</summary>
public sealed class TesseractOCRService : IOCRService
{
    /// <summary>Host-owned writable model directory. Setting this performs no I/O or native loading.</summary>
    public static string? ManagedDataDirectory { get; set; }
    /// <summary>Read-only inventory locations; does not load native code or create directories.</summary>
    public static IReadOnlyList<string> GetDataDirectories()
    {
        string? prefix = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrEmpty(prefix)) return [Path.GetFullPath(prefix)];
        List<string> directories = [];
        if (!string.IsNullOrWhiteSpace(ManagedDataDirectory)) directories.Add(Path.GetFullPath(ManagedDataDirectory));
        directories.Add(TesseractModel.ModelsDirectory);
        if (OperatingSystem.IsLinux())
            directories.AddRange(["/usr/share/tesseract-ocr/5/tessdata", "/usr/share/tessdata", "/usr/local/share/tessdata"]);
        return directories.Distinct(StringComparer.Ordinal).ToArray();
    }
    readonly Config.SubtitlesConfig config;
    readonly Func<ITesseractNative> loadNative;
    readonly Func<string?> tessdataPrefix;
    readonly Func<string?> managedDirectory;
    readonly Func<string?> legacyDirectory;
    readonly object gate = new();
    ITesseractNative? native;
    nint engine;
    Language language = Language.Unknown;
    bool disposed;

    public TesseractOCRService(Config.SubtitlesConfig config)
        : this(config, () => TesseractNative.Shared, () => Environment.GetEnvironmentVariable("TESSDATA_PREFIX")) { }

    internal TesseractOCRService(Config.SubtitlesConfig config, Func<ITesseractNative> loadNative, Func<string?> tessdataPrefix)
        : this(config, loadNative, tessdataPrefix, () => ManagedDataDirectory,
            () => TesseractModel.ModelsDirectory) { }

    internal TesseractOCRService(Config.SubtitlesConfig config, Func<ITesseractNative> loadNative, Func<string?> tessdataPrefix,
        Func<string?> managedDirectory, Func<string?> legacyDirectory)
    {
        this.config = config;
        this.loadNative = loadNative;
        this.tessdataPrefix = tessdataPrefix;
        this.managedDirectory = managedDirectory;
        this.legacyDirectory = legacyDirectory;
    }

    public bool TryInitialize(Language lang, out string err)
    {
        lock (gate)
        {
            if (disposed) { err = "Tesseract OCR service is disposed."; return false; }
            nint candidate = 0;
            try
            {
                string? prefix = tessdataPrefix();
                if (!string.IsNullOrEmpty(prefix))
                {
                    if (prefix.Contains('\0'))
                    {
                        err = "TESSDATA_PREFIX contains an invalid NUL character.";
                        return false;
                    }
                    prefix = Path.GetFullPath(prefix);
                    if (!Directory.Exists(prefix))
                    {
                        err = $"TESSDATA_PREFIX '{prefix}' must point to an existing tessdata directory.";
                        return false;
                    }
                }
                native ??= loadNative();
                // The official --list-langs path: an empty language initializes only the data path, no model.
                candidate = native.Create();
                if (candidate == 0) throw new InvalidOperationException("Tesseract could not create an OCR engine.");
                if (native.Initialize(candidate, string.IsNullOrEmpty(prefix) ? null : prefix, "") != 0)
                    throw new InvalidOperationException("Tesseract could not initialize tessdata discovery. Check TESSDATA_PREFIX and installed language packages.");
                Dictionary<string, string> sources = native.AvailableLanguages(candidate)
                    .ToDictionary(code => code, _ => native.DataPath(candidate), StringComparer.Ordinal);
                string path = native.DataPath(candidate);
                native.Delete(candidate);
                candidate = 0;
                // A user override remains exclusive. Otherwise choose a source per language: installing
                // one managed language must not hide the other languages supplied by the operating system.
                if (string.IsNullOrEmpty(prefix))
                {
                    AddModelSources(sources, legacyDirectory());
                    AddModelSources(sources, managedDirectory());
                }

                string iso = lang.ISO6391 == "nb" ? "no" : lang.ISO6391;
                List<TesseractModel> mapped = TesseractModel.TesseractLangToISO6391
                    .Where(pair => pair.Value == iso).Select(pair => new TesseractModel { Lang = pair.Key }).ToList();
                List<TesseractModel> available = mapped.Where(model => sources.ContainsKey(model.LangCode)).ToList();
                if (available.Count == 0)
                {
                    string codes = mapped.Count == 0 ? iso : string.Join(", ", mapped.Select(m => m.LangCode));
                    err = $"Tesseract OCR language {lang.TopEnglishName} ({codes}) is missing in '{path}'. Install its language package or place the matching .traineddata in TESSDATA_PREFIX.";
                    return false;
                }
                TesseractModel selected = available[0];
                if (config.TesseractOcrRegions?.TryGetValue(iso, out string? preferred) == true)
                    selected = available.FirstOrDefault(model => model.LangCode == preferred) ?? selected;
                path = sources[selected.LangCode];
                candidate = native.Create();
                if (candidate == 0) throw new InvalidOperationException("Tesseract could not create an OCR engine.");
                if (native.Initialize(candidate, path, selected.LangCode) != 0)
                    throw new InvalidOperationException($"Tesseract cannot initialize '{selected.LangCode}.traineddata' in '{path}'; the model may be corrupt or incompatible. Reinstall the language package or replace the model.");
                native.SetPageSegMode(candidate, 3); // PSM_AUTO: preserve the Windows wrapper's multiline behavior.
                if (iso is "ja" or "ko" or "zh" && !native.SetVariable(candidate, "preserve_interword_spaces", "1"))
                    throw new InvalidOperationException("Tesseract could not configure CJK interword spacing.");
                if (engine != 0) native.Delete(engine);
                engine = candidate;
                candidate = 0;
                language = lang;
                err = "";
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or
                NotSupportedException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                string platformHelp = OperatingSystem.IsWindows()
                    ? "Check/install the app-local Windows x64 Tesseract libraries, Visual C++ x64 Redistributable 2022 and the selected language data."
                    : "Check/install system Tesseract 5 (libtesseract.so.5) and the selected language data.";
                err = $"Tesseract OCR initialization failed: {e.Message} {platformHelp}";
                return false;
            }
            finally
            {
                if (candidate != 0) native!.Delete(candidate);
            }
        }
    }

    static void AddModelSources(Dictionary<string, string> sources, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        string full = Path.GetFullPath(directory);
        foreach (string file in Directory.EnumerateFiles(full, "*.traineddata", SearchOption.TopDirectoryOnly))
            if (new FileInfo(file).Length > 0) sources[Path.GetFileNameWithoutExtension(file)] = full;
    }

    public Task<string> RecognizeTextAsync(BgraBitmap bitmap)
    {
        // Preserve IOCRService's Task contract while keeping native recognition on the caller's OCR worker.
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (engine == 0) throw new InvalidOperationException("Tesseract OCR engine is not initialized.");
                ArgumentNullException.ThrowIfNull(bitmap);
                if (bitmap.Width <= 0 || bitmap.Height <= 0 || bitmap.Width > int.MaxValue / 4 ||
                    (long)bitmap.Stride * bitmap.Height > bitmap.Data.Length || (long)bitmap.Width * bitmap.Height > int.MaxValue / 3)
                    throw new ArgumentException("OCR requires a nonempty, complete BGRA32 image with valid dimensions and stride.", nameof(bitmap));
                byte[] rgb = new byte[checked(bitmap.Width * bitmap.Height * 3)];
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int src = y * bitmap.Stride + x * 4, dst = (y * bitmap.Width + x) * 3;
                        rgb[dst] = bitmap.Data[src + 2];
                        rgb[dst + 1] = bitmap.Data[src + 1];
                        rgb[dst + 2] = bitmap.Data[src];
                    }
                return Task.FromResult(native!.Recognize(engine, rgb, bitmap.Width, bitmap.Height));
            }
        }
        catch (Exception e) { return Task.FromException<string>(e); }
    }

    public string PostProcess(string text)
    {
        lock (gate)
        {
            if (language == Language.English)
                text = text.Replace("|", "I").Replace("I1t's", "It's").Replace("’'", "'");
            return text.Trim();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (engine != 0) { native!.Delete(engine); engine = 0; }
            native?.Dispose();
            native = null;
        }
    }
}
