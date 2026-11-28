using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

/// <summary>
/// The facts text that the "help" tasks give to the model must not change by accident. Same story as SummaryTestKit:
/// tender tnd-9 with two eligible offers (Alpha, Beta), one rejected offer (Gamma) and a committee of two members (m1, m2).
/// </summary>
public class HelpFactsGoldenTests
{
    private static async Task<string> FactsAsync(SummaryTestKit kit, string role, string userId, string task, string? number = "tnd-9")
    {
        var service = new RoleDataService(kit.Db, kit.Summary(), kit.Docs);
        var args = new Dictionary<string, string> { ["task"] = task };
        if (number != null) args["number"] = number;
        var data = await service.RunToolAsync(role, userId, new AiToolCall("help", args), "", CancellationToken.None);
        Assert.NotNull(data);
        return data!.Facts + "\n=== TABLE ===\n" + data.Table?.Title + "\n" + string.Join("\n", data.Table?.Rows.Select(r => string.Join(" | ", r)) ?? []);
    }

    // ---------------------------------------------------------------- the committee member's brief

    [Theory]
    [InlineData("compare_offers", "brief-compare.txt")]
    [InlineData("supplier_questions", "brief-ask.txt")]
    [InlineData("scoring_rubric", "brief-rubric.txt")]
    public async Task The_committee_brief_when_the_member_confirmed_all_their_forms(string task, string golden)
    {
        var kit = new SummaryTestKit();
        GoldenFile.Check(golden, await FactsAsync(kit, Roles.EvaluationCommittee, "m1", task));
    }

    [Fact]
    public async Task The_committee_brief_while_the_member_has_not_confirmed_a_form()
    {
        var kit = new SummaryTestKit();
        foreach (var form in kit.Db.FinancialEvaluations.Where(f => f.EvaluatorUserId == "m1" && f.BidId == 2)) form.ReadyAt = null;
        await kit.Db.SaveChangesAsync();

        GoldenFile.Check("brief-compare-not-ready.txt", await FactsAsync(kit, Roles.EvaluationCommittee, "m1", "compare_offers"));
    }

    [Fact]
    public async Task The_committee_brief_without_a_tender_number_when_there_is_only_one_tender()
    {
        var kit = new SummaryTestKit();
        GoldenFile.Check("brief-compare-no-number.txt", await FactsAsync(kit, Roles.EvaluationCommittee, "m1", "compare_offers", number: null));
    }

    // ---------------------------------------------------------------- the officer's compliance checklist

    [Fact]
    public async Task The_officer_compliance_checklist()
    {
        var kit = new SummaryTestKit();
        GoldenFile.Check("compliance.txt", await FactsAsync(kit, Roles.SourcingOfficer, "oscar", "compliance_checklist"));
    }
}
