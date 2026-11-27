using System.Runtime.CompilerServices;
using TenderSystem.Infrastructure;

namespace TenderSystem.Tests.Ai;

/// <summary>
/// The facts text that the tender summary gives to the model and to the assistant chat must not change by accident (it is also what the saved
/// summary's "outdated" check hashes). The expected text is stored next to this file in Golden/. To accept an intended change, run the tests once
/// with the environment variable UPDATE_GOLDEN=1 and read the difference in git.
/// </summary>
public class TenderPackageGoldenTests
{
    private static string GoldenPath(string name, [CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden", name);

    private static void Check(string name, string actual)
    {
        var path = GoldenPath(name);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual.ReplaceLineEndings("\n"));
        }
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"));
    }

    private static async Task<string> PackageAsync(bool allFormsReady, string role)
    {
        var kit = new SummaryTestKit(allFormsReady);
        var package = await kit.Summary().GetChatPackageAsync(1, "m1", role, CancellationToken.None);
        return $"State: {package.State}\nMessage: {package.Message}\nTender: {package.TenderNumber} / {package.Title}\n"
            + "Columns: " + string.Join(" | ", package.Columns) + "\n"
            + string.Join("\n", package.Rows.Select(r => string.Join(" | ", r))) + "\n---\n" + package.FactsText;
    }

    [Fact]
    public async Task Manager_package_when_every_form_is_confirmed() => Check("package-manager-final.txt", await PackageAsync(true, Roles.SourcingManager));

    [Fact]
    public async Task Manager_package_when_a_form_is_still_open() => Check("package-manager-provisional.txt", await PackageAsync(false, Roles.SourcingManager));
}
