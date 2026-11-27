using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

public class AiThinkFilterTests
{
    private static string Run(params string[] pieces)
    {
        var filter = new AiThinkFilter();
        var result = string.Concat(pieces.Select(filter.Push));
        return result + filter.Finish();
    }

    [Fact]
    public void Plain_text_passes_through()
    {
        Assert.Equal("Hello there", Run("Hello ", "there"));
    }

    [Fact]
    public void Thinking_block_is_removed()
    {
        Assert.Equal("The answer is 5.", Run("<think>let me think</think>The answer is 5."));
    }

    [Fact]
    public void Tags_split_between_pieces_are_still_removed()
    {
        Assert.Equal("Done.", Run("<thi", "nk>hidden</thi", "nk>Done."));
    }

    [Fact]
    public void A_less_than_sign_that_is_not_a_tag_is_kept()
    {
        Assert.Equal("a < b and c", Run("a < b", " and c"));
    }

    [Fact]
    public void Unclosed_thinking_gives_nothing()
    {
        Assert.Equal("", Run("<think>never ends"));
    }
}
