namespace TenderSystem.Services.Ai;

/// <summary>
/// Finds keys that are written twice in the .env file. The application loads .env with NoClobber, so the FIRST line of a key wins and the
/// second one is silently ignored (this once made AI_CHAT_PROVIDER=openai win over a later AI_CHAT_PROVIDER=ollama).
/// </summary>
public static class EnvFileCheck
{
    /// <summary>The keys that appear more than once in the text of a .env file. A line that starts with # is a comment.</summary>
    public static List<string> DuplicateKeys(string text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ")) line = line[7..].TrimStart();

            var equals = line.IndexOf('=');
            if (equals <= 0) continue;

            var key = line[..equals].Trim();
            if (!seen.Add(key) && !duplicates.Contains(key, StringComparer.OrdinalIgnoreCase)) duplicates.Add(key);
        }
        return duplicates;
    }

    /// <summary>The .env file DotNetEnv would load: the first one found in this folder or a parent folder. Null when there is none.</summary>
    public static string? FindFile()
    {
        for (var folder = new DirectoryInfo(Directory.GetCurrentDirectory()); folder != null; folder = folder.Parent)
        {
            var path = Path.Combine(folder.FullName, ".env");
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
