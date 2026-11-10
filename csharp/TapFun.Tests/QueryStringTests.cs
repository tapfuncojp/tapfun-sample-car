namespace TapFun.Tests;

public class QueryStringTests
{
    [Fact]
    public void DecodesWhatTapFunSends()
    {
        // TapFun encodes spaces as %20.
        Dictionary<string, string> message = QueryString.Decode(
            "from=getPlayerEnvironment&userAgent=Mozilla%2F5.0%20(Windows%20NT%2010.0)&isSP=0&authenticated=1&isPaidTimer=0");

        Assert.Equal(new Dictionary<string, string>
        {
            ["from"] = "getPlayerEnvironment",
            ["userAgent"] = "Mozilla/5.0 (Windows NT 10.0)",
            ["isSP"] = "0",
            ["authenticated"] = "1",
            ["isPaidTimer"] = "0",
        }, message);
    }

    [Fact]
    public void DecodesUtf8PlusAndEncodedPlus()
    {
        Dictionary<string, string> message = QueryString.Decode("text=%E3%81%82+b%2Bc&empty=&flag");

        Assert.Equal("あ b+c", message["text"]);
        Assert.Equal("", message["empty"]);
        Assert.Equal("", message["flag"]);
    }

    [Fact]
    public void IgnoresEmptyPairs()
    {
        Assert.Equal(new Dictionary<string, string> { ["a"] = "1" }, QueryString.Decode("&a=1&&"));
        Assert.Empty(QueryString.Decode(""));
    }
}
