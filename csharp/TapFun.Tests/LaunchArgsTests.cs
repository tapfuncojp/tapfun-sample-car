namespace TapFun.Tests;

public class LaunchArgsTests
{
    // The default launch parameters, as TapFun passes them (Environment.GetCommandLineArgs() starts with the exe path).
    private static readonly string[] Default =
    {
        @"C:\Apps\TapFunSample\TapFunSample.exe",
        "--type", "car", "--tapfun-user-id", "PLYR01HE2R3JV6Y2NEGRMS4KZQB861DBG", "--api-host", "tapfun.co.jp",
        "--bearer-token", "123_AAABBBCCC", "--sig-key", "FFFGGGHHH", "--port", "10100",
        "--username", "%E3%81%82%E3%81%84%20b", "--is-paid-timer", "1", "--is-sp", "0", "--dlcs", "delux_upgrade,special_upgrade",
    };

    [Fact]
    public void ReadsTheDefaultLaunchParameters()
    {
        LaunchArgs args = LaunchArgs.Parse(Default);

        Assert.Equal("PLYR01HE2R3JV6Y2NEGRMS4KZQB861DBG", args.UserId);
        Assert.Equal("tapfun.co.jp", args.ApiHost);
        Assert.Equal("123_AAABBBCCC", args.BearerToken);
        Assert.Equal(10100, args.Port);
        Assert.Equal("あい b", args.Username);
        Assert.True(args.IsPaidTimer);
        Assert.False(args.IsSp);
        Assert.Equal(new[] { "delux_upgrade", "special_upgrade" }, args.Dlcs);
        Assert.Equal("car", args.Get("--type"));
        Assert.Equal("FFFGGGHHH", args.Get("--sig-key"));
        Assert.True(args.HasClientApiValues);
    }

    [Fact]
    public void MissingOptionsHaveNoValue()
    {
        // TapFun drops an option together with its name when the value is empty.
        LaunchArgs args = LaunchArgs.Parse(new[] { "game.exe", "--type", "car", "--api-host", "tapfun.co.jp" });

        Assert.Equal("", args.UserId);
        Assert.Equal("", args.BearerToken);
        Assert.Equal("", args.Username);
        Assert.Equal(LaunchArgs.DefaultPort, args.Port);
        Assert.False(args.IsPaidTimer);
        Assert.Empty(args.Dlcs);
        Assert.Equal("", args.Get("--unknown"));
        Assert.False(args.HasClientApiValues);
    }

    [Fact]
    public void ReadsNameEqualsValue()
    {
        LaunchArgs args = LaunchArgs.Parse(new[] { "--tapfun-user-id=PLYR1", "--api-host=example.com", "--bearer-token=1_a=b", "--port=10200" });

        Assert.Equal("PLYR1", args.UserId);
        Assert.Equal("1_a=b", args.BearerToken);
        Assert.Equal(10200, args.Port);
        Assert.True(args.HasClientApiValues);
    }

    [Fact]
    public void AnOptionWithoutValueBeforeAnotherOptionIsEmpty()
    {
        LaunchArgs args = LaunchArgs.Parse(new[] { "--flag", "--port", "10101" });

        Assert.Equal("", args.Get("--flag"));
        Assert.Equal(10101, args.Port);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    public void InvalidPortFallsBackToTheDefault(string port)
    {
        Assert.Equal(10100, LaunchArgs.Parse(new[] { "--port", port }).Port);
    }

    [Fact]
    public void DlcsIgnoresEmptyCodes()
    {
        Assert.Equal(new[] { "a", "b" }, LaunchArgs.Parse(new[] { "--dlcs", " a,,b , " }).Dlcs);
    }

    [Fact]
    public void FromCommandLineReadsThisProcess()
    {
        // The test runner has no TapFun options, so they are empty.
        Assert.Equal("", LaunchArgs.FromCommandLine().UserId);
    }
}
