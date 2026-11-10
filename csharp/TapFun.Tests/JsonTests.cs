namespace TapFun.Tests;

public class JsonTests
{
    [Fact]
    public void ReadsAllKinds()
    {
        var value = (Dictionary<string, object?>)Json.Parse(
            " { \"s\": \"a\\\"b\\\\c\\/d\\n\\u3042\\ud83d\\ude00\", \"i\": -12, \"big\": 9007199254740993, \"d\": 1.5e2," +
            " \"t\": true, \"f\": false, \"n\": null, \"a\": [1, \"x\", [], {}] } ")!;

        Assert.Equal("a\"b\\c/d\nあ😀", value["s"]);
        Assert.Equal(-12L, value["i"]);
        Assert.Equal(9007199254740993L, value["big"]);
        Assert.Equal(150.0, value["d"]);
        Assert.Equal(true, value["t"]);
        Assert.Equal(false, value["f"]);
        Assert.Null(value["n"]);
        var array = (List<object?>)value["a"]!;
        Assert.Equal(1L, array[0]);
        Assert.Equal("x", array[1]);
        Assert.Empty((List<object?>)array[2]!);
        Assert.Empty((Dictionary<string, object?>)array[3]!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"a\":}")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1 2]")]
    [InlineData("\"abc")]
    [InlineData("\"\\x\"")]
    [InlineData("\"\\u12\"")]
    [InlineData("tru")]
    [InlineData("-")]
    [InlineData("{} x")]
    [InlineData("<html>")]
    public void RejectsInvalidJson(string json)
    {
        Assert.Throws<FormatException>(() => Json.Parse(json));
    }
}
