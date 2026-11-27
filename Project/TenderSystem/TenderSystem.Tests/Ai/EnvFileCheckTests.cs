using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

public class EnvFileCheckTests
{
    [Fact]
    public void A_key_written_twice_is_reported_once()
    {
        var text = "AI_CHAT_PROVIDER=openai\nOTHER=1\nAI_CHAT_PROVIDER=ollama\nAI_CHAT_PROVIDER=off\n";
        Assert.Equal(["AI_CHAT_PROVIDER"], EnvFileCheck.DuplicateKeys(text));
    }

    [Fact]
    public void Different_keys_and_comments_are_fine()
    {
        var text = "# AI_CHAT_PROVIDER=openai\nAI_CHAT_PROVIDER=ollama\n\nAI_API_KEY=x\r\n";
        Assert.Empty(EnvFileCheck.DuplicateKeys(text));
    }

    [Fact]
    public void Spaces_and_export_do_not_hide_a_duplicate()
    {
        var text = "A=1\nexport A=2\n  A = 3\n";
        Assert.Equal(["A"], EnvFileCheck.DuplicateKeys(text));
    }
}
