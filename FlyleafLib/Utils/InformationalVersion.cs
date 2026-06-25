namespace FlyleafLib;

#nullable enable

/// <summary>
/// Parses an assembly InformationalVersion / <see cref="System.Diagnostics.FileVersionInfo.ProductVersion"/>
/// string of the form <c>"&lt;version&gt;"</c> or <c>"&lt;version&gt;+&lt;commitHash&gt;"</c>.
/// </summary>
/// <remarks>
/// The <c>+&lt;commitHash&gt;</c> suffix is appended by the SDK only when the build supplies a git SHA
/// (Source Link, or a <c>SourceRevisionId</c> MSBuild property). Local and agent publish builds do not inject
/// one, so a missing suffix is normal and must never be treated as an error: a throw here would block startup,
/// since <c>App.Version</c> is first read inside a try/catch that calls <c>Environment.Exit(1)</c>.
/// A version-only string maps to <c>(version, "")</c> and an empty string to <c>("", "")</c> by design —
/// do not tighten this back into a throw.
/// </remarks>
public static class InformationalVersion
{
    /// <summary>
    /// Splits a ProductVersion into its version and commit-hash parts. When the <c>+commitHash</c> suffix is
    /// absent the commit hash is returned as <see cref="string.Empty"/> rather than throwing.
    /// </summary>
    /// <param name="productVersion">The raw ProductVersion / AssemblyInformationalVersion string.</param>
    /// <returns>The leading version and the trailing commit hash ("" when there is no suffix).</returns>
    public static (string version, string commitHash) Parse(string? productVersion)
    {
        ArgumentNullException.ThrowIfNull(productVersion);

        // SemVer build metadata may itself contain '+', so split only on the first one:
        //   "0.3.7"        -> ("0.3.7", "")
        //   "0.3.7+abc123" -> ("0.3.7", "abc123")
        //   "0.3.7+a+b"    -> ("0.3.7", "a+b")
        string[] parts = productVersion.Split('+', 2);
        return (parts[0], parts.Length > 1 ? parts[1] : string.Empty);
    }
}
