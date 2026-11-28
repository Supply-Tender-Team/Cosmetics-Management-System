using System.Runtime.CompilerServices;

namespace TenderSystem.Tests.Ai;

/// <summary>
/// "The text must not change by accident." The expected text is stored in a file in Golden/ next to the tests.
/// To accept an intended change, run the tests once with the environment variable UPDATE_GOLDEN=1 and read the difference in git.
/// </summary>
internal static class GoldenFile
{
    public static void Check(string name, string actual, [CallerFilePath] string here = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(here)!, "Golden", name);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual.ReplaceLineEndings("\n"));
        }
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"));
    }
}
