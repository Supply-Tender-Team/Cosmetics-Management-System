using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

public class AiLookupPlannerTests
{
    private static readonly IReadOnlyList<AiToolDef> RequesterTools = AiLookupCatalog.For(Roles.Requester, false);

    [Fact]
    public void A_valid_choice_is_read_with_its_arguments()
    {
        var call = AiLookupPlanner.Parse("{\"tool\":\"requests\",\"args\":{\"status\":\"Submitted\",\"limit\":5}}", RequesterTools);

        Assert.NotNull(call);
        Assert.Equal("requests", call!.Tool);
        Assert.Equal("Submitted", call.Arg("status"));
        Assert.Equal("5", call.Arg("limit"));
    }

    [Fact]
    public void Text_around_the_json_is_ignored()
    {
        var call = AiLookupPlanner.Parse("Sure! {\"tool\":\"counts\",\"args\":{\"subject\":\"requests\"}} hope it helps", RequesterTools);
        Assert.Equal("counts", call?.Tool);
    }

    [Fact]
    public void A_tool_the_role_does_not_have_is_refused()
    {
        Assert.Null(AiLookupPlanner.Parse("{\"tool\":\"audit\",\"args\":{}}", RequesterTools));
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("none")]
    public void The_two_special_tools_are_always_allowed(string tool)
    {
        Assert.NotNull(AiLookupPlanner.Parse("{\"tool\":\"" + tool + "\",\"args\":{}}", RequesterTools));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{broken json")]
    [InlineData("{\"args\":{}}")]
    public void Unusable_answers_give_nothing(string? answer)
    {
        Assert.Null(AiLookupPlanner.Parse(answer, RequesterTools));
    }

    [Fact]
    public void Very_long_argument_values_are_dropped()
    {
        var call = AiLookupPlanner.Parse("{\"tool\":\"requests\",\"args\":{\"text\":\"" + new string('x', 200) + "\"}}", RequesterTools);
        Assert.NotNull(call);
        Assert.Equal("", call!.Arg("text"));
    }
}
