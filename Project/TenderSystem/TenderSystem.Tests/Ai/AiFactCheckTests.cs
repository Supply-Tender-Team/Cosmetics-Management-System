using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

public class AiFactCheckTests
{
    [Fact]
    public void Numbers_are_written_the_same_way()
    {
        Assert.Equal(["4200"], AiFactCheck.Numbers("Total 4,200.00"));
        Assert.Equal(["4200"], AiFactCheck.Numbers("Total 4200"));
    }

    [Fact]
    public void A_number_that_is_not_in_the_facts_is_reported()
    {
        var bad = AiFactCheck.UnsupportedNumbers("The price is 5000 and delivery 14 days", "price 4,200 delivery 14 days");
        Assert.Equal(["5000"], bad);
    }

    [Fact]
    public void An_answer_with_only_supported_numbers_passes()
    {
        Assert.Empty(AiFactCheck.UnsupportedNumbers("Offer A costs 4200.00", "Offer A: 4,200"));
    }

    [Fact]
    public void Amount_is_found_in_any_common_format()
    {
        Assert.True(AiFactCheck.ContainsAmount("costs 4,200.00 in total", 4200m));
        Assert.True(AiFactCheck.ContainsAmount("costs 4200 in total", 4200m));
        Assert.False(AiFactCheck.ContainsAmount("costs 4300", 4200m));
    }

    [Fact]
    public void Names_are_matched_without_case_and_short_terms_are_ignored()
    {
        Assert.True(AiFactCheck.MentionsAny("Winner is ACME Ltd", ["acme"]));
        Assert.False(AiFactCheck.MentionsAny("Winner is ACME Ltd", ["ac"]));
        Assert.False(AiFactCheck.MentionsAny(null, ["acme"]));
    }
}
