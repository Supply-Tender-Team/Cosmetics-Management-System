using System.Text.RegularExpressions;

namespace TenderSystem.Infrastructure;

/// <summary>
/// Helps with a broken .env file. A typo such as "EVALUATION SLA DAYS=7" (spaces instead of underscores) used to stop the application with
/// an unreadable parser error. Now the error names the line and says how to fix it, and a key that appears twice is reported at startup.
/// It only reads the file: it never changes it and never decides anything about the settings.
/// </summary>
public static class EnvFileCheck
{
    /// <summary>A clear message for the exception DotNetEnv throws when it cannot read the file.</summary>
    public static string Explain(Exception error)
    {
        var message = "The .env file could not be read. Every line must be KEY=value, a comment starting with #, or empty. " +
                      "A key has letters, digits and underscores only (no spaces), for example EVALUATION_SLA_DAYS=7.";

        var path = FindFile();
        var lineNumber = Regex.Match(error.Message, @"line (\d+)", RegexOptions.IgnoreCase);
        if (path is not null && lineNumber.Success && int.TryParse(lineNumber.Groups[1].Value, out var number))
        {
            var lines = File.ReadAllLines(path);
            if (number >= 1 && number <= lines.Length)
                return $"{message} Problem at line {number} of {path}: \"{Hide(lines[number - 1])}\".";
        }
        return path is null ? message : $"{message} File: {path}. Parser message: {error.Message}";
    }

    /// <summary>Keys that appear more than once. The FIRST one is used (a real environment variable and the first line win), so a later one is silently ignored.</summary>
    public static List<string> DuplicateKeys()
    {
        var path = FindFile();
        if (path is null) return [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var key = line[..equals].Trim();
            if (!seen.Add(key) && !duplicates.Contains(key)) duplicates.Add(key);
        }
        return duplicates;
    }

    /// <summary>Same search as DotNetEnv's TraversePath: the current folder, then its parents.</summary>
    private static string? FindFile()
    {
        for (var folder = new DirectoryInfo(Directory.GetCurrentDirectory()); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The text of a bad line without anything after the first "=" (it may hold a password or a key).</summary>
    private static string Hide(string line)
    {
        var equals = line.IndexOf('=');
        return equals < 0 ? line.Trim() : line[..(equals + 1)].Trim() + "<value hidden>";
    }
}
