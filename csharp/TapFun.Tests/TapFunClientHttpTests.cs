using System.Globalization;
using System.Net;

namespace TapFun.Tests;

public class TapFunClientHttpTests
{
    private static readonly LaunchArgs Args = LaunchArgs.Parse(new[]
    {
        "--tapfun-user-id", "PLYR01ABC+/=", "--api-host", "tapfun.co.jp", "--bearer-token", "34_secret", "--port", "10100",
    });

    private readonly FakeHandler _handler = new();
    private readonly TapFunClient _client;

    public TapFunClientHttpTests()
    {
        _client = new TapFunClient(Args, "TapFunTests/1.0", _handler);
    }

    private FakeHandler.Captured Single() => Assert.Single(_handler.Requests);

    private static Dictionary<string, string> Form(FakeHandler.Captured request)
    {
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        return QueryString.Decode(request.Body);
    }

    [Fact]
    public async Task SendPostsTheRequestToCustomEvent()
    {
        ApiResult result = await _client.SendAsync(ClientApi.InputTextSingleLine("name", title: "あいう & =", maxLength: 12));

        FakeHandler.Captured request = Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://tapfun.co.jp/api/v1/custom_event?userId=PLYR01ABC%2B%2F%3D", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer 34_secret", request.Authorization);
        Assert.Equal("TapFunTests/1.0", request.UserAgent);
        Assert.Equal(new Dictionary<string, string>
        {
            ["req"] = "inputTextSingleLine", ["label"] = "name", ["title"] = "あいう & =", ["maxlength"] = "12",
        }, Form(request));
        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("{\"result\":\"success\"}", result.Body);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task UploadPhotoSendsTheFileAsMultipart()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF };

        await _client.UploadPhotoAsync(png, "screenshot.png", "image/png");

        FakeHandler.Captured request = Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://tapfun.co.jp/api/v1/photos?userId=PLYR01ABC%2B%2F%3D", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer 34_secret", request.Authorization);
        Assert.Equal("multipart/form-data", request.ContentType);
        FakeHandler.Part part = Assert.Single(request.Parts);
        Assert.Equal("file", part.Name?.Trim('"'));
        Assert.Equal("screenshot.png", part.FileName?.Trim('"'));
        Assert.Equal("image/png", part.ContentType);
        Assert.Equal(png, part.Bytes);
    }

    [Fact]
    public async Task GetLeaderboardEntriesFromTheTop()
    {
        await _client.GetLeaderboardEntriesAsync("stage-1_a");

        FakeHandler.Captured request = Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://tapfun.co.jp/api/v1/leaderboard/entries?name=stage-1_a", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer 34_secret", request.Authorization);
    }

    [Fact]
    public async Task GetLeaderboardEntriesWithEveryOption()
    {
        await _client.GetLeaderboardEntriesAsync("sample", ascending: true, around: 5, offset: 10, limit: 20);

        Assert.Equal(new Dictionary<string, string>
        {
            ["name"] = "sample", ["sort"] = "asc", ["userId"] = "PLYR01ABC+/=", ["arround"] = "5", ["offset"] = "10", ["limit"] = "20",
        }, QueryString.Decode(Single().Uri.Query.TrimStart('?')));
    }

    [Fact]
    public async Task PostLeaderboardEntry()
    {
        await _client.PostLeaderboardEntryAsync("sample", 1234567890123);

        FakeHandler.Captured request = Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://tapfun.co.jp/api/v1/leaderboard/entries", request.Uri.AbsoluteUri);
        Assert.Equal(new Dictionary<string, string> { ["name"] = "sample", ["userId"] = "PLYR01ABC+/=", ["score"] = "1234567890123" }, Form(request));
    }

    [Fact]
    public async Task PostLeaderboardEntryWithNoteAndForce()
    {
        await _client.PostLeaderboardEntryAsync("sample", -5, note: "{\"stage\":1}", force: true);

        Assert.Equal(new Dictionary<string, string>
        {
            ["name"] = "sample", ["userId"] = "PLYR01ABC+/=", ["score"] = "-5", ["note"] = "{\"stage\":1}", ["force"] = "1",
        }, Form(Single()));
    }

    [Fact]
    public async Task ANegativeScoreIsSentWithAnAsciiMinus()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        // Some cultures use U+2212 as the minus sign.
        CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
        try
        {
            Assert.Equal("\u2212" + "5", (-5L).ToString());
            await _client.PostLeaderboardEntryAsync("sample", -5);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal("-5", Form(Single())["score"]);
    }

    [Fact]
    public async Task DeleteLeaderboardEntry()
    {
        await _client.DeleteLeaderboardEntryAsync("sample");

        FakeHandler.Captured request = Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://tapfun.co.jp/api/v1/leaderboard/entries/delete", request.Uri.AbsoluteUri);
        Assert.Equal(new Dictionary<string, string> { ["name"] = "sample", ["userId"] = "PLYR01ABC+/=" }, Form(request));
    }

    [Fact]
    public async Task AnErrorStatusIsReturnedWithItsBody()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Forbidden") };

        ApiResult result = await _client.SendAsync(ClientApi.GetPlayerEnvironment());

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Forbidden", result.Body);
        Assert.Equal("HTTP 403 Forbidden", result.ToString());
    }

    [Fact]
    public async Task AConnectionFailureIsReturnedAsStatus0()
    {
        _handler.Respond = _ => throw new HttpRequestException("No such host is known.");

        ApiResult result = await _client.SendAsync(ClientApi.GetPlayerEnvironment());

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.StatusCode);
        Assert.Equal("No such host is known.", result.Error);
        Assert.Equal("error: No such host is known.", result.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AUserAgentIsRequired(string userAgent)
    {
        Assert.Throws<ArgumentException>(() => new TapFunClient(Args, userAgent, new FakeHandler()));
    }

    [Fact]
    public async Task AUserAgentWithSpacesIsSentAsItIs()
    {
        using var client = new TapFunClient(Args, "My Game/1.0 (Unity)", _handler);

        await client.SendAsync(ClientApi.GetPlayerEnvironment());

        Assert.Equal("My Game/1.0 (Unity)", Single().UserAgent);
    }
}
