using System.Runtime.InteropServices;
using System.Text;

namespace FlyleafLib.MediaPlayer;

#nullable enable

internal interface ITesseractNative : IDisposable
{
    nint Create();
    void Delete(nint handle);
    int Initialize(nint handle, string? path, string language);
    string[] AvailableLanguages(nint handle);
    string DataPath(nint handle);
    bool SetVariable(nint handle, string name, string value);
    void SetPageSegMode(nint handle, int mode);
    string Recognize(nint handle, byte[] rgb, int width, int height);
}

/// <summary>Tesseract 5 C API binding. Native engines are per-track; the loaded library lives for the process.</summary>
internal sealed unsafe class TesseractNative : ITesseractNative
{
    static readonly object loadGate = new();
    static TesseractNative? shared;
    internal static TesseractNative Shared
    {
        get { lock (loadGate) return shared ??= new TesseractNative(); }
    }
    nint library;
    readonly delegate* unmanaged[Cdecl]<nint> create;
    readonly delegate* unmanaged[Cdecl]<nint, void> delete;
    readonly delegate* unmanaged[Cdecl]<nint, byte*, byte*, int, int> initialize;
    readonly delegate* unmanaged[Cdecl]<nint, byte**> available;
    readonly delegate* unmanaged[Cdecl]<byte**, void> deleteArray;
    readonly delegate* unmanaged[Cdecl]<nint, byte*> dataPath;
    readonly delegate* unmanaged[Cdecl]<nint, byte*, byte*, int> setVariable;
    readonly delegate* unmanaged[Cdecl]<nint, int, void> setPageSegMode;
    readonly delegate* unmanaged[Cdecl]<nint, byte*, int, int, int, int, void> setImage;
    readonly delegate* unmanaged[Cdecl]<nint, nint, int> recognize;
    readonly delegate* unmanaged[Cdecl]<nint, byte*> getText;
    readonly delegate* unmanaged[Cdecl]<byte*, void> deleteText;
    readonly delegate* unmanaged[Cdecl]<nint, void> clear;

    TesseractNative()
    {
        if (!NativeLibrary.TryLoad("libtesseract.so.5", out library))
            throw new DllNotFoundException("libtesseract.so.5 is unavailable; install Tesseract 5 (Ubuntu: tesseract-ocr).");
        try
        {
            var version = (delegate* unmanaged[Cdecl]<byte*>)NativeLibrary.GetExport(library, "TessVersion");
            string? actual = Marshal.PtrToStringUTF8((nint)version());
            if (actual?.StartsWith("5.", StringComparison.Ordinal) != true)
                throw new NotSupportedException($"Tesseract 5 is required; libtesseract.so.5 reports '{actual}'.");
            create = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(library, "TessBaseAPICreate");
            delete = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "TessBaseAPIDelete");
            initialize = (delegate* unmanaged[Cdecl]<nint, byte*, byte*, int, int>)NativeLibrary.GetExport(library, "TessBaseAPIInit2");
            available = (delegate* unmanaged[Cdecl]<nint, byte**>)NativeLibrary.GetExport(library, "TessBaseAPIGetAvailableLanguagesAsVector");
            deleteArray = (delegate* unmanaged[Cdecl]<byte**, void>)NativeLibrary.GetExport(library, "TessDeleteTextArray");
            dataPath = (delegate* unmanaged[Cdecl]<nint, byte*>)NativeLibrary.GetExport(library, "TessBaseAPIGetDatapath");
            setVariable = (delegate* unmanaged[Cdecl]<nint, byte*, byte*, int>)NativeLibrary.GetExport(library, "TessBaseAPISetVariable");
            setPageSegMode = (delegate* unmanaged[Cdecl]<nint, int, void>)NativeLibrary.GetExport(library, "TessBaseAPISetPageSegMode");
            setImage = (delegate* unmanaged[Cdecl]<nint, byte*, int, int, int, int, void>)NativeLibrary.GetExport(library, "TessBaseAPISetImage");
            recognize = (delegate* unmanaged[Cdecl]<nint, nint, int>)NativeLibrary.GetExport(library, "TessBaseAPIRecognize");
            getText = (delegate* unmanaged[Cdecl]<nint, byte*>)NativeLibrary.GetExport(library, "TessBaseAPIGetUTF8Text");
            deleteText = (delegate* unmanaged[Cdecl]<byte*, void>)NativeLibrary.GetExport(library, "TessDeleteText");
            clear = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "TessBaseAPIClear");
        }
        catch
        {
            NativeLibrary.Free(library);
            library = 0;
            throw;
        }
    }

    static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + '\0');
    public nint Create() => create();
    public void Delete(nint handle) => delete(handle);
    public int Initialize(nint handle, string? path, string language)
    {
        byte[]? p = path == null ? null : Utf8(path);
        byte[] l = Utf8(language);
        fixed (byte* pp = p, lp = l)
            return initialize(handle, pp, lp, 3); // OEM_DEFAULT, identical to the Windows wrapper.
    }
    public string[] AvailableLanguages(nint handle)
    {
        byte** list = available(handle);
        if (list == null) return [];
        try
        {
            List<string> languages = [];
            for (int i = 0; list[i] != null; i++)
                languages.Add(Marshal.PtrToStringUTF8((nint)list[i])!);
            return [.. languages];
        }
        finally { deleteArray(list); }
    }
    public string DataPath(nint handle) => Marshal.PtrToStringUTF8((nint)dataPath(handle)) ?? "";
    public bool SetVariable(nint handle, string name, string value)
    {
        byte[] n = Utf8(name), v = Utf8(value);
        fixed (byte* np = n, vp = v)
            return setVariable(handle, np, vp) != 0;
    }
    public void SetPageSegMode(nint handle, int mode) => setPageSegMode(handle, mode);
    public string Recognize(nint handle, byte[] rgb, int width, int height)
    {
        byte* text = null;
        try
        {
            // SetImage copies the pixels, but keep them pinned through recognition as well.
            fixed (byte* pixels = rgb)
            {
                setImage(handle, pixels, width, height, 3, checked(width * 3));
                if (recognize(handle, 0) != 0)
                    throw new InvalidOperationException("Tesseract OCR recognition failed.");
                text = getText(handle);
                if (text == null)
                    throw new InvalidOperationException("Tesseract OCR returned no text buffer.");
                return Marshal.PtrToStringUTF8((nint)text) ?? "";
            }
        }
        finally
        {
            if (text != null) deleteText(text);
            clear(handle); // Releases page results while retaining the model for the next cue.
        }
    }
    public void Dispose()
    {
        // Repeated Linux OCR crashed after unloading/reloading the library; retaining the binding passed the
        // same eng/rus tests. Like OpenAlNative, keep it loaded; services delete every per-track API engine.
    }
}
