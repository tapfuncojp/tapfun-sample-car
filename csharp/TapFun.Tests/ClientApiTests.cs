namespace TapFun.Tests;

public class ClientApiTests
{
    [Fact]
    public void RequestsWithoutParameters()
    {
        Assert.Equal(new Dictionary<string, string> { ["req"] = "launchSuccessNotice" }, ClientApi.LaunchSuccessNotice());
        Assert.Equal(new Dictionary<string, string> { ["req"] = "getPlayerEnvironment" }, ClientApi.GetPlayerEnvironment());
    }

    [Fact]
    public void RequestsWithText()
    {
        Assert.Equal(new Dictionary<string, string> { ["req"] = "openUrl", ["url"] = "https://tapfun.co.jp/" }, ClientApi.OpenUrl("https://tapfun.co.jp/"));
        Assert.Equal(new Dictionary<string, string> { ["req"] = "showText", ["text"] = "Hello" }, ClientApi.ShowText("Hello"));
        Assert.Equal(new Dictionary<string, string> { ["req"] = "consoleLog", ["data"] = "{\"a\":1}" }, ClientApi.ConsoleLog("{\"a\":1}"));
        Assert.Equal(new Dictionary<string, string> { ["req"] = "copyText", ["text"] = "CODE" }, ClientApi.CopyText("CODE"));
        Assert.Equal(new Dictionary<string, string> { ["req"] = "copyText", ["text"] = "CODE", ["title"] = "Your code" }, ClientApi.CopyText("CODE", "Your code"));
    }

    [Fact]
    public void InputTextSingleLineSendsOnlyTheGivenOptions()
    {
        Assert.Equal(new Dictionary<string, string> { ["req"] = "inputTextSingleLine", ["label"] = "name" }, ClientApi.InputTextSingleLine("name"));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["req"] = "inputTextSingleLine", ["label"] = "name", ["title"] = "T", ["text"] = "x", ["placeholder"] = "p",
                ["submit"] = "OK", ["cancel"] = "No", ["maxlength"] = "12", ["position"] = "bottom",
                ["pattern"] = "[0-9]+", ["invalid"] = "Digits only",
            },
            ClientApi.InputTextSingleLine("name", title: "T", text: "x", placeholder: "p", submit: "OK", cancel: "No",
                maxLength: 12, position: "bottom", pattern: "[0-9]+", invalid: "Digits only"));
    }

    [Fact]
    public void InputTextMultiLineSendsOnlyTheGivenOptions()
    {
        Assert.Equal(new Dictionary<string, string> { ["req"] = "inputTextMultiLine", ["label"] = "m" }, ClientApi.InputTextMultiLine("m"));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["req"] = "inputTextMultiLine", ["label"] = "m", ["title"] = "T", ["text"] = "a\nb", ["placeholder"] = "p",
                ["submit"] = "OK", ["cancel"] = "No", ["maxlength"] = "10000", ["position"] = "bottom", ["end"] = "3",
            },
            ClientApi.InputTextMultiLine("m", title: "T", text: "a\nb", placeholder: "p", submit: "OK", cancel: "No",
                maxLength: 10000, position: "bottom", end: 3));
    }

    [Fact]
    public void ChunksAreAtMost128CharactersAndNumberedFrom1()
    {
        string text = new string('a', 128) + new string('b', 128) + "cc";

        List<Dictionary<string, string>> chunks = ClientApi.InputTextMultiLineChunks(text);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.Equal("inputTextMultiLineChunkText", c["req"]));
        Assert.Equal(new[] { "1", "2", "3" }, chunks.Select(c => c["seq"]));
        Assert.Equal(new[] { 128, 128, 2 }, chunks.Select(c => c["text"].Length));
        Assert.Equal(text, string.Concat(chunks.Select(c => c["text"])));
    }

    [Fact]
    public void ShortAndEmptyTexts()
    {
        Assert.Empty(ClientApi.InputTextMultiLineChunks(""));
        Assert.Single(ClientApi.InputTextMultiLineChunks(new string('a', 128)));
    }

    [Fact]
    public void AChunkDoesNotSplitASurrogatePair()
    {
        // "😀" is two UTF-16 code units. It would straddle the 128th character.
        string text = new string('a', 127) + "😀" + "z";

        List<Dictionary<string, string>> chunks = ClientApi.InputTextMultiLineChunks(text);

        Assert.Equal(new[] { new string('a', 127), "😀z" }, chunks.Select(c => c["text"]));
    }

    [Fact]
    public void UpTo100Chunks()
    {
        Assert.Equal(100, ClientApi.InputTextMultiLineChunks(new string('a', 128 * 100)).Count);
        Assert.Throws<ArgumentException>(() => ClientApi.InputTextMultiLineChunks(new string('a', 128 * 100 + 1)));
    }
}
