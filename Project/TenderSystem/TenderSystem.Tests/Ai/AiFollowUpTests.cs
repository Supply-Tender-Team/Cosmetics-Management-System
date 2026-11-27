using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

public class AiFollowUpTests
{
    private static readonly AiChatTurn[] AboutTender =
    [
        new("user", "Tell me about TND-002"),
        new("assistant", "TND-002 closes on Friday."),
    ];

    [Fact]
    public void A_short_follow_up_gets_the_last_record_number()
    {
        var result = AiFollowUp.Resolve("what about its delivery?", AboutTender);
        Assert.Contains("tnd-002", result, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("what about its delivery?", result);
    }

    [Fact]
    public void A_number_typed_by_the_user_always_wins()
    {
        Assert.Equal("and TND-005?", AiFollowUp.Resolve("and TND-005?", AboutTender));
    }

    [Fact]
    public void Without_history_nothing_is_added()
    {
        Assert.Equal("what about its delivery?", AiFollowUp.Resolve("what about its delivery?", []));
    }

    [Fact]
    public void A_how_to_question_is_not_turned_into_a_lookup()
    {
        Assert.Equal("how do I score it?", AiFollowUp.Resolve("how do I score it?", AboutTender));
    }

    [Fact]
    public void A_new_topic_without_pointer_words_is_left_alone()
    {
        Assert.Equal("which suppliers are pending", AiFollowUp.Resolve("which suppliers are pending", AboutTender));
    }

    [Fact]
    public void The_second_one_uses_the_table_the_assistant_showed()
    {
        var history = new[]
        {
            new AiChatTurn("user", "list my requests"),
            new AiChatTurn("assistant", "Here they are", ["PR-001", "PR-002", "PR-003"]),
        };
        Assert.Contains("PR-002", AiFollowUp.Resolve("show the second one", history));
    }
}
