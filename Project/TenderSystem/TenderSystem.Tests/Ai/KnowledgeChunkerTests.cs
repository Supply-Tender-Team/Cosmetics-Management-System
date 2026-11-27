using TenderSystem.Services.Knowledge;

namespace TenderSystem.Tests.Ai;

public class KnowledgeChunkerTests
{
    private static string Words(int count, string prefix = "word") =>
        string.Join(' ', Enumerable.Range(1, count).Select(i => prefix + i));

    [Fact]
    public void Short_text_stays_in_one_piece()
    {
        var chunks = KnowledgeChunker.Split(Words(60), 100, 10);
        Assert.Single(chunks);
    }

    [Fact]
    public void Long_paragraph_is_cut_and_pieces_overlap()
    {
        var chunks = KnowledgeChunker.Split(Words(300), 100, 20);

        Assert.True(chunks.Count >= 3);
        // the last 20 words of one piece start the next piece
        var first = chunks[0].Split(' ');
        var second = chunks[1].Split(' ');
        Assert.Equal(first.TakeLast(20), second.Take(20));
    }

    [Fact]
    public void Every_word_of_the_text_appears_in_some_piece()
    {
        var chunks = KnowledgeChunker.Split(Words(350), 100, 20);
        var all = string.Join(' ', chunks) + " ";
        for (var i = 1; i <= 350; i++)
            Assert.Contains($"word{i} ", all);
    }

    [Fact]
    public void Small_paragraphs_are_kept_together()
    {
        var text = Words(30, "a") + "\n" + Words(30, "b");
        var chunks = KnowledgeChunker.Split(text, 100, 10);
        Assert.Single(chunks);
        Assert.Contains("\n", chunks[0]);
    }

    [Fact]
    public void Empty_text_gives_no_pieces()
    {
        Assert.Empty(KnowledgeChunker.Split("   \n  ", 100, 10));
    }
}
