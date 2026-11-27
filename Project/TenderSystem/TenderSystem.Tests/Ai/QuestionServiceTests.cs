using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

public class QuestionServiceTests
{
    // CheckQuestion only reads the chat options, so the other parts are not needed for these tests.
    private static QuestionService Create(string provider = "ollama", int maxChars = 50, int perMinute = 2) =>
        new(Options.Create(new AiChatOptions { Provider = provider, MaxMessageChars = maxChars, MaxQuestionsPerMinute = perMinute }),
            [], NullLogger<QuestionService>.Instance, null!, null!, Options.Create(new AiActionOptions()), null!);

    // each test uses its own user id, because the speed limiter is shared by the whole process
    private static string NewUser() => Guid.NewGuid().ToString();

    [Fact]
    public void A_normal_question_is_accepted_and_trimmed()
    {
        var (error, question) = Create().CheckQuestion(NewUser(), "  Which tenders close this week?  ");
        Assert.Null(error);
        Assert.Equal("Which tenders close this week?", question);
    }

    [Fact]
    public void When_the_assistant_is_off_the_answer_is_503()
    {
        var (error, _) = Create(provider: "off").CheckQuestion(NewUser(), "hello");
        Assert.Equal(503, error?.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void An_empty_question_is_refused_with_400(string? message)
    {
        var (error, _) = Create().CheckQuestion(NewUser(), message);
        Assert.Equal(400, error?.Status);
    }

    [Fact]
    public void A_too_long_question_is_refused_with_400()
    {
        var (error, _) = Create(maxChars: 10).CheckQuestion(NewUser(), new string('x', 11));
        Assert.Equal(400, error?.Status);
    }

    [Fact]
    public void Asking_too_fast_is_refused_with_429()
    {
        var chat = Create(perMinute: 2);
        var user = NewUser();
        Assert.Null(chat.CheckQuestion(user, "one").Error);
        Assert.Null(chat.CheckQuestion(user, "two").Error);
        Assert.Equal(429, chat.CheckQuestion(user, "three").Error?.Status);
    }

    [Fact]
    public void A_refused_question_does_not_use_up_the_limit()
    {
        var chat = Create(maxChars: 10, perMinute: 1);
        var user = NewUser();
        Assert.Equal(400, chat.CheckQuestion(user, new string('x', 11)).Error?.Status);
        Assert.Null(chat.CheckQuestion(user, "short").Error);
    }
}
