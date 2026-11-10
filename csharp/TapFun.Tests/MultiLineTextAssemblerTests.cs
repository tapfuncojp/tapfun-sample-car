namespace TapFun.Tests;

public class MultiLineTextAssemblerTests
{
    private static Dictionary<string, string> Chunk(string label, int seq, int end, string text) => new()
    {
        ["from"] = "inputTextMultiLine", ["result"] = "submit", ["label"] = label,
        ["seq"] = seq.ToString(), ["end"] = end.ToString(), ["text"] = text,
    };

    [Fact]
    public void OneChunk()
    {
        var assembler = new MultiLineTextAssembler();

        Assert.True(assembler.TryAdd(Chunk("m", 1, 1, "Line 1\nLine 2"), out string label, out string result, out string text));
        Assert.Equal(("m", "submit", "Line 1\nLine 2"), (label, result, text));
    }

    [Fact]
    public void JoinsChunksThatArriveOutOfOrder()
    {
        var assembler = new MultiLineTextAssembler();

        Assert.False(assembler.TryAdd(Chunk("m", 3, 3, "c"), out _, out _, out _));
        Assert.False(assembler.TryAdd(Chunk("m", 1, 3, "a"), out _, out _, out _));
        Assert.True(assembler.TryAdd(Chunk("m", 2, 3, "b"), out string label, out string result, out string text));
        Assert.Equal(("m", "submit", "abc"), (label, result, text));
    }

    [Fact]
    public void KeepsLabelsApart()
    {
        var assembler = new MultiLineTextAssembler();

        Assert.False(assembler.TryAdd(Chunk("a", 1, 2, "a1"), out _, out _, out _));
        Assert.False(assembler.TryAdd(Chunk("b", 1, 2, "b1"), out _, out _, out _));
        Assert.True(assembler.TryAdd(Chunk("b", 2, 2, "b2"), out string label, out _, out string text));
        Assert.Equal(("b", "b1b2"), (label, text));
        Assert.True(assembler.TryAdd(Chunk("a", 2, 2, "a2"), out label, out _, out text));
        Assert.Equal(("a", "a1a2"), (label, text));
    }

    [Fact]
    public void CancelCompletesWithoutText()
    {
        var assembler = new MultiLineTextAssembler();
        assembler.TryAdd(Chunk("m", 1, 2, "stale"), out _, out _, out _);

        Assert.True(assembler.TryAdd(new Dictionary<string, string> { ["from"] = "inputTextMultiLine", ["result"] = "cancel", ["label"] = "m" },
            out string label, out string result, out string text));
        Assert.Equal(("m", "cancel", ""), (label, result, text));

        // The chunk before the cancel is not joined with the next answer.
        Assert.True(assembler.TryAdd(Chunk("m", 1, 1, "new"), out _, out _, out text));
        Assert.Equal("new", text);
    }

    [Fact]
    public void ALaterAnswerWithFewerChunksIgnoresLeftOverChunks()
    {
        var assembler = new MultiLineTextAssembler();
        assembler.TryAdd(Chunk("m", 3, 3, "lost answer"), out _, out _, out _);

        Assert.True(assembler.TryAdd(Chunk("m", 1, 1, "next"), out _, out _, out string text));
        Assert.Equal("next", text);
    }

    [Theory]
    [InlineData("x", "1")]
    [InlineData("0", "1")]
    [InlineData("2", "1")]
    public void IgnoresChunksWithInvalidNumbers(string seq, string end)
    {
        var message = Chunk("m", 1, 1, "t");
        message["seq"] = seq;
        message["end"] = end;

        Assert.False(new MultiLineTextAssembler().TryAdd(message, out _, out _, out _));
    }
}
