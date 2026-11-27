using TenderSystem.Data.Entities.Requests;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

/// <summary>The planner tool "help": the model names a task, the server runs it for the right role only. Same story as RecordCardTests.</summary>
public class HelpToolTests
{
    private static AiToolCall Help(string task, string? number = null, string? category = null)
    {
        var args = new Dictionary<string, string> { ["task"] = task };
        if (number != null) args["number"] = number;
        if (category != null) args["category"] = category;
        return new AiToolCall("help", args);
    }

    private static async Task<(AiDataResult? Data, string Text)> RunAsync(string role, string userId, AiToolCall call, string question = "")
    {
        var (service, _) = RecordCardTests.Create();
        var data = await service.RunToolAsync(role, userId, call, question, CancellationToken.None);
        var text = data == null ? "" : data.Facts + "\n" + data.Table?.Title + "\n" + string.Join("\n", data.Table?.Rows.Select(r => string.Join(" | ", r)) ?? []);
        return (data, text);
    }

    // ---------------------------------------------------------------- the catalog

    [Theory]
    [InlineData(Roles.Requester)]
    [InlineData(Roles.SourcingOfficer)]
    [InlineData(Roles.EvaluationCommittee)]
    [InlineData(Roles.SourcingManager)]
    [InlineData(Roles.TopCommittee)]
    [InlineData(Roles.Supplier)]
    public void These_roles_have_the_help_tool(string role) => Assert.True(AiLookupCatalog.Allows(role, false, "help"));

    [Fact]
    public void The_admin_has_no_help_tool() => Assert.False(AiLookupCatalog.Allows(Roles.Admin, false, "help"));

    // ---------------------------------------------------------------- tasks of another role are refused

    [Theory]
    [InlineData(Roles.Requester, "compliance_checklist")]
    [InlineData(Roles.Supplier, "tender_package")]
    [InlineData(Roles.EvaluationCommittee, "offer_check")]
    [InlineData(Roles.SourcingOfficer, "word_request")]
    public async Task A_task_of_another_role_gives_nothing(string role, string task)
    {
        var (data, _) = await RunAsync(role, "oscar", Help(task, "tnd-1"));
        Assert.Null(data);
    }

    [Fact]
    public async Task An_unknown_task_gives_nothing()
    {
        var (data, _) = await RunAsync(Roles.Requester, "rana", Help("delete_everything"));
        Assert.Null(data);
    }

    // ---------------------------------------------------------------- the tasks keep the role scope

    [Fact]
    public async Task A_committee_member_gets_help_only_on_a_tender_they_sit_on()
    {
        var (data, text) = await RunAsync(Roles.EvaluationCommittee, "eva", Help("scoring_rubric", "tnd-2"));
        Assert.Contains("No tender where this user is a committee member", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(data);
    }

    [Fact]
    public async Task A_supplier_checks_only_their_own_offer()
    {
        var (_, own) = await RunAsync(Roles.Supplier, "alpha-user", Help("offer_check", "tnd-1"));
        Assert.Contains("Checklist of your offer for tnd-1", own, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Beta", own, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("999", own);

        var (_, other) = await RunAsync(Roles.Supplier, "beta-user", Help("offer_check", "tnd-2"));
        Assert.Contains("No offer of this supplier was found", other, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_officer_gets_the_approved_requests_when_no_number_is_given()
    {
        var (service, db) = RecordCardTests.Create();
        db.PurchaseRequisitions.Add(new PurchaseRequisition { Id = 9, RequestNumber = "pr-9", Title = "Ribbons", Status = Statuses.RequestStatus.Approved, DepartmentId = 1, RequestedByUserId = "rana", Priority = "Low", TargetDate = DateTime.UtcNow.AddDays(30) });
        db.SaveChanges();

        var data = await service.RunToolAsync(Roles.SourcingOfficer, "oscar", Help("tender_questions"), "", CancellationToken.None);
        Assert.Contains("pr-9", string.Join("|", data!.Table!.Rows.Select(r => r[0])));
    }

    [Fact]
    public async Task The_shortlist_asks_for_a_category_when_none_is_given()
    {
        var (data, text) = await RunAsync(Roles.SourcingOfficer, "oscar", Help("supplier_shortlist"), "suggest suppliers");
        Assert.NotNull(data);
        Assert.Contains("did not name a supplier category", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_requester_gets_a_wording_task_with_only_their_own_earlier_requests()
    {
        var (_, text) = await RunAsync(Roles.Requester, "rana", Help("word_request"), "help me write a request for glass boxes");
        Assert.Contains("TASK:", text);
        Assert.DoesNotContain("pr-2", text, StringComparison.OrdinalIgnoreCase);     // another requester's request
    }
}
