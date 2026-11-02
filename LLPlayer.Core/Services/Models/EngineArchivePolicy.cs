using FlyleafLib;

namespace LLPlayer.Core.Services.Models;

/// <summary>Portable pre-extraction policy for the pinned Windows engine package.</summary>
public static class EngineArchivePolicy
{
    public static void ValidateEntryNames(IEnumerable<string> names, string destinationDirectory)
    {
        string[] entries = names.ToArray();
        ArchivePathGuard.ValidateEntries(entries, destinationDirectory);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in entries)
        {
            string entry = raw.Replace('\\', '/').TrimEnd('/');
            string[] parts = entry.Split('/');
            if (entry.Length == 0 || parts.Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.Contains(':'))
                || (parts[0] != ModelManagementService.EngineName && entry != "license.txt") || !seen.Add(entry))
                throw new IOException("The engine archive contains an unsafe, duplicate or unexpected entry.");
        }
    }
}
