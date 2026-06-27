using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Detects whether the Microsoft Visual C++ Redistributable (x64) runtime DLLs that the in-process
/// native ASR (whisper.cpp via Whisper.net) and OCR (Tesseract) libraries depend on are resolvable by
/// the OS loader.
/// </summary>
/// <remarks>
/// Without the redistributable those native loads abort the process the moment ASR or OCR is enabled —
/// the README calls this out: "the app will launch, but will crash when ASR or OCR is enabled". Callers
/// run <see cref="IsRuntimePresent"/> as a cheap preflight just before constructing the native engine and
/// surface a clear, actionable "install VC++" message instead of letting the app die with no explanation.
/// We probe the loader (not the registry) so we measure exactly what the whisper.cpp / Tesseract native
/// libraries will resolve at load time, regardless of how the runtime was installed.
/// </remarks>
public static class VcRedistChecker
{
    /// <summary>
    /// CRT modules imported in-process by the native ASR/OCR libraries (whisper.cpp via Whisper.net, and
    /// Tesseract). <c>vcruntime140_1.dll</c> was added in the VC++ 2019 (x64) redistributable and is present in
    /// every later release, so its absence is a reliable signal that no modern VC++ runtime is installed — the
    /// documented crash case. We deliberately detect the <em>absence</em> of these modules rather than enforce an
    /// exact "&gt;= 2022" version: the redistributable is cumulative and forward-compatible, a 2019+ runtime
    /// already resolves these imports, and a strict version gate would wrongly block a machine on which ASR/OCR
    /// actually works. The remediation message still points at the current 2022-or-newer installer. Order is
    /// preserved in the missing-module report.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredModules =
        new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" };

    /// <summary>Microsoft's permalink to the latest supported x64 VC++ redistributable installer.</summary>
    public const string DownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    /// <summary>
    /// Pure evaluator: returns the subset of <paramref name="modules"/> for which
    /// <paramref name="canLoad"/> reports the module cannot be loaded, in input order. Extracted from the
    /// platform probe so the missing-module logic is unit-testable without touching the real OS loader.
    /// </summary>
    public static IReadOnlyList<string> EvaluateMissing(IEnumerable<string> modules, Func<string, bool> canLoad)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(canLoad);

        List<string> missing = new();
        foreach (string module in modules)
        {
            if (!canLoad(module))
            {
                missing.Add(module);
            }
        }

        return missing;
    }

    /// <summary>
    /// Probes the real OS loader for <see cref="RequiredModules"/>. Returns true when every module is
    /// resolvable (redistributable present); otherwise false, with the unresolved modules in
    /// <paramref name="missing"/>.
    /// </summary>
    public static bool IsRuntimePresent(out IReadOnlyList<string> missing)
    {
        missing = EvaluateMissing(RequiredModules, CanLoadModule);
        return missing.Count == 0;
    }

    /// <summary>
    /// Builds the user-facing preflight message naming the <paramref name="feature"/> that needs the
    /// redistributable. The download URL is appended so it is visible even where the host UI surfaces the
    /// message without an action button (e.g. a modal error dialog).
    /// </summary>
    public static string BuildMissingMessage(string feature) =>
        $"{feature} requires the Microsoft Visual C++ Redistributable (x64) 2022 or newer, which is not installed. " +
        $"Install it and restart LLPlayer. Download: {DownloadUrl}";

    private static bool CanLoadModule(string name)
    {
        // NativeLibrary honours the OS loader search order (System32 + the app directory), so a successful
        // load means the native ASR/OCR library will be able to resolve this CRT dependency too. Free the
        // handle immediately: we only care whether it resolves, not about keeping a reference.
        if (NativeLibrary.TryLoad(name, out nint handle))
        {
            NativeLibrary.Free(handle);
            return true;
        }

        return false;
    }
}
