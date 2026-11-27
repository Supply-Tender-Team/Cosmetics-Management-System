using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Tests.Ai;

/// <summary>
/// The tender summary and the bid analysis ask the model through the shared engine (AiGenerationService). These tests run the REAL writers
/// against a fake model (see SummaryTestKit): a good answer is saved, a bad answer is retried once, two bad answers save nothing.
/// </summary>
public class WriterEngineTests
{
    private const string GoodSummary = """
        {"overview":"Alpha is cheaper and faster.","offers":[{"supplier":"Alpha Co","strengths":["lowest price"],"weaknesses":[],"main_risk":"none"},{"supplier":"Beta Co","strengths":[],"weaknesses":["slow"],"main_risk":"delivery"}],"points_to_check":["samples"],"leaning":{"supplier":"Alpha Co","reason":"cheaper"}}
        """;

    private const string GoodAnalysis = """
        {"price":4800,"delivery_days":20,"missing_documents":[],"strengths":["price"],"weaknesses":[],"recommendation":"Recommended","reason":"fits the terms"}
        """;

    private static ClaimsPrincipal Manager() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "m1"), new Claim(ClaimTypes.Role, Roles.SourcingManager)], "test"));

    // ---------------------------------------------------------------- tender summary

    [Fact]
    public async Task A_good_summary_is_saved_with_one_model_call()
    {
        var kit = new SummaryTestKit();
        kit.Model.Answers.Add(GoodSummary);

        var view = await kit.Summary().GenerateAsync(1, Manager());

        Assert.Equal(1, kit.Model.Calls);
        Assert.Equal("", view.Message);
        Assert.Equal("Alpha is cheaper and faster.", view.Summary?.Overview);
        Assert.Equal("Alpha Co", view.Summary?.LeaningSupplier);
        Assert.Single(await kit.Db.AiTenderSummaries.ToListAsync());
    }

    [Fact]
    public async Task A_wrong_answer_is_retried_once_and_the_second_one_is_saved()
    {
        var kit = new SummaryTestKit();
        kit.Model.Answers.Add("sorry, here is some text and no JSON");
        kit.Model.Answers.Add("```json\n" + GoodSummary + "\n```");          // a code fence around the JSON is accepted

        var view = await kit.Summary().GenerateAsync(1, Manager());

        Assert.Equal(2, kit.Model.Calls);
        Assert.NotNull(view.Summary);
        Assert.Single(await kit.Db.AiTenderSummaries.ToListAsync());
    }

    [Fact]
    public async Task Two_wrong_answers_save_nothing_and_say_so()
    {
        var kit = new SummaryTestKit();
        kit.Model.Answers.Add("{\"overview\":\"x\",\"offers\":[{\"supplier\":\"Not In The Data Co\"}]}");   // right shape, but an invented supplier

        var view = await kit.Summary().GenerateAsync(1, Manager());

        Assert.Equal(2, kit.Model.Calls);
        Assert.Contains("did not give a usable answer", view.Message);
        Assert.Null(view.Summary);
        Assert.Empty(await kit.Db.AiTenderSummaries.ToListAsync());
    }

    [Fact]
    public async Task The_model_may_not_lean_towards_an_offer_while_a_form_is_still_open()
    {
        var kit = new SummaryTestKit(allFormsReady: false);
        kit.Model.Answers.Add(GoodSummary);

        var view = await kit.Summary().GenerateAsync(1, Manager());

        Assert.NotNull(view.Summary);
        Assert.Null(view.Summary!.LeaningSupplier);
    }

    // ---------------------------------------------------------------- bid analysis

    [Fact]
    public async Task Every_offer_is_analysed_and_saved()
    {
        var kit = new SummaryTestKit();
        kit.Model.Answers.Add(GoodAnalysis);

        var run = await kit.Analysis().AnalyzeAsync(1, "m1");

        Assert.True(run.Ok, run.Message);
        Assert.Equal(3, run.Items.Count);                 // Alpha, Beta and the rejected Gamma are all submitted offers
        Assert.Equal(3, kit.Model.Calls);
        Assert.Equal(3, await kit.Db.AiBidAnalyses.CountAsync());
        Assert.All(run.Items, i => Assert.Equal("Recommended", i.Recommendation));
    }

    [Fact]
    public async Task A_recommendation_outside_the_list_is_retried_and_then_rejected()
    {
        var kit = new SummaryTestKit();
        kit.Model.Answers.Add(GoodAnalysis.Replace("Recommended", "Definitely buy"));

        var run = await kit.Analysis().AnalyzeAsync(1, "m1");

        Assert.False(run.Ok);
        Assert.Contains("did not give a usable answer", run.Message);
        Assert.Equal(2, kit.Model.Calls);
        Assert.Empty(await kit.Db.AiBidAnalyses.ToListAsync());
    }

    [Fact]
    public async Task Only_a_member_of_the_committee_can_run_the_analysis_and_the_model_is_not_asked()
    {
        var kit = new SummaryTestKit();
        kit.Model.Answers.Add(GoodAnalysis);

        var run = await kit.Analysis().AnalyzeAsync(1, "oscar");

        Assert.False(run.Ok);
        Assert.Equal(0, kit.Model.Calls);
    }
}
