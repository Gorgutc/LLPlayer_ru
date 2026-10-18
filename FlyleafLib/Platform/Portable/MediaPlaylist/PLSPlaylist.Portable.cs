namespace FlyleafLib.MediaFramework.MediaPlaylist;

// F-13 portable counterpart of PLSPlaylist.GetINIAttribute (the Windows build uses kernel32 GetPrivateProfileString).
public partial class PLSPlaylist
{
    /// <summary>
    /// Reads <paramref name="key"/> from INI section <paramref name="name"/> of <paramref name="path"/> with the same
    /// observable semantics as <c>GetPrivateProfileString</c> into a 255-char buffer: section/key match
    /// case-insensitively, surrounding whitespace and one pair of matching quotes are removed, the first occurrence
    /// wins, values are truncated to 254 chars, and an empty/missing value (or unreadable file) returns null.
    /// </summary>
    public static string GetINIAttribute(string name, string key, string path)
    {
        string[] lines;
        try
        {
            if (!File.Exists(path))
                return null;

            lines = File.ReadAllLines(path);
        }
        catch
        {
            return null;
        }

        bool inSection = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';')
                continue;

            if (line[0] == '[')
            {
                int end = line.IndexOf(']');
                string section = end > 0 ? line[1..end].Trim() : line[1..].Trim();
                inSection = string.Equals(section, name, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0 || !string.Equals(line[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
                continue;

            string value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                value = value[1..^1];

            if (value.Length > 254)
                value = value[..254];

            return value.Length > 0 ? value : null;
        }

        return null;
    }
}
