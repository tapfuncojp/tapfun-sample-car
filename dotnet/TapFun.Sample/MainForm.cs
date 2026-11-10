using System.Diagnostics;
using System.Drawing.Imaging;
using TapFun;

namespace TapFun.Sample;

/// <summary>
/// Buttons on the left, log on the right. The buttons can also be pressed with the A to L keys.
///
/// What it does:
///   1. Reads the launch arguments.
///   2. Listens for messages from TapFun on UDP 127.0.0.1:{port}.
///   3. Sends launchSuccessNotice, then getPlayerEnvironment (sent again until the answer arrives).
///   4. Calls every other client API and Web API from the buttons and shows the answers.
/// </summary>
public sealed class MainForm : Form
{
    private const string Leaderboard = "sample";

    private readonly LaunchArgs _args;
    private readonly TapFunClient? _tapfun;
    private readonly TextBox _log;
    private readonly List<(string Label, Func<Task> Run)> _actions;

    public MainForm()
    {
        Text = "TapFun Sample (.NET)";
        ClientSize = new Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 11f);
        KeyPreview = true;
        KeyDown += OnKeyDown;

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11f),
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 360,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8),
        };
        Controls.Add(_log);
        Controls.Add(buttons);

        _actions =
        [
            // Client API
            ("getPlayerEnvironment", () => SendAsync(ClientApi.GetPlayerEnvironment())),
            ("openUrl", () => SendAsync(ClientApi.OpenUrl("https://tapfun.co.jp/"))),
            ("inputTextSingleLine", () => SendAsync(ClientApi.InputTextSingleLine(
                label: "nickname", title: "Nickname", placeholder: "Enter your nickname", maxLength: 12))),
            ("inputTextMultiLine", () => SendAsync(ClientApi.InputTextMultiLine(
                label: "message", title: "Message", text: "Line 1\nLine 2", maxLength: 1000))),
            ("inputTextMultiLine (long text)", InputLongTextAsync),
            ("showText", () => SendAsync(ClientApi.ShowText("Hello from the game."))),
            ("copyText", () => SendAsync(ClientApi.CopyText("SAMPLE-CODE-1234", title: "Your code"))),
            ("consoleLog", () => SendAsync(ClientApi.ConsoleLog($"{{\"time\":\"{DateTime.UtcNow:O}\"}}"))),
            // Web API
            ("photos (screenshot)", UploadScreenshotAsync),
            ("leaderboard: post score", PostScoreAsync),
            ("leaderboard: get top 10", GetTopScoresAsync),
            ("leaderboard: delete mine", () => CallAsync("leaderboard delete", _tapfun!.DeleteLeaderboardEntryAsync(Leaderboard))),
        ];
        for (int i = 0; i < _actions.Count; i++)
        {
            var (label, run) = _actions[i];
            var button = new Button { Text = $"{(char)('A' + i)}) {label}", Width = 330, Height = 44, TextAlign = ContentAlignment.MiddleLeft };
            button.Click += async (_, _) => await RunAsync(run);
            buttons.Controls.Add(button);
        }

        // 1. Launch arguments.
        _args = LaunchArgs.FromCommandLine();
        // Do not show the TapFun user ID or the Bearer token on screen.
        Log($"apiHost={_args.ApiHost} port={_args.Port} userId={Mask(_args.UserId)} bearerToken={Mask(_args.BearerToken)}");
        Log($"username={_args.Username} isPaidTimer={_args.IsPaidTimer} isSp={_args.IsSp} dlcs=[{string.Join(",", _args.Dlcs)}]");
        if (!_args.HasClientApiValues)
        {
            Log("--tapfun-user-id, --api-host and --bearer-token are required. Add {userId}, {apiHost} and {bearerToken} to the exe launch parameters.");
            return;
        }
        _tapfun = new TapFunClient(_args, "TapFunSample/1.0");
        _tapfun.MessageReceived += OnMessage;
        _tapfun.InputTextMultiLineReceived += (label, result, text) =>
            // The multi-line answer comes in chunks; this is called when all of them have arrived.
            Log(result == "submit" ? $"   entered ({label}, {text.Length} chars): \"{text}\"" : $"   {result} ({label})");
        Shown += async (_, _) => await StartAsync(_tapfun);
    }

    private async Task StartAsync(TapFunClient tapfun)
    {
        // 2. Messages from TapFun. Listen on the UI thread, so that the events are raised on it.
        try
        {
            tapfun.Listen();
            Log($"listening on UDP 127.0.0.1:{_args.Port}");
        }
        catch (Exception ex)
        {
            Log($"UDP listen failed: {ex.Message}");
        }

        // 3. Startup requests. Send launchSuccessNotice once the game is ready to play.
        await SendAsync(ClientApi.LaunchSuccessNotice());

        // Answers to requests sent soon after the game starts can be lost: wait until StartupWait has passed
        // since the start. GetPlayerEnvironmentAsync sends the request again when no answer arrives.
        TimeSpan sinceStart = DateTime.Now - Process.GetCurrentProcess().StartTime;
        if (sinceStart < ClientApi.StartupWait)
        {
            await Task.Delay(ClientApi.StartupWait - sinceStart);
        }
        if (await tapfun.GetPlayerEnvironmentAsync() == null)
        {
            Log("getPlayerEnvironment: no answer");
        }
    }

    private void OnMessage(IReadOnlyDictionary<string, string> message)
    {
        string text = string.Join(" ", message.Select(p => $"{p.Key}={p.Value}"));
        if (message.TryGetValue("from", out string? from))
        {
            // Answer to a request: from=getPlayerEnvironment, from=inputTextSingleLine, ...
            Log($"<- {from}: {text}");
            if (from == "inputTextSingleLine" && message.GetValueOrDefault("result") == "submit")
            {
                Log($"   entered: \"{message.GetValueOrDefault("text")}\"");
            }
        }
        else if (message.GetValueOrDefault("req") == "clientStatus")
        {
            // Sent by TapFun without a request, for example result=lastTimeRemain&second=90.
            Log($"<- clientStatus: {text}");
        }
        else
        {
            Log($"<- {text}");
        }
    }

    // inputTextMultiLine with an initial text too long for one request:
    // send it in inputTextMultiLineChunkText requests first, then open the input with end = number of chunks.
    private async Task InputLongTextAsync()
    {
        string longText = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"Line {i}: this initial text is sent in chunks."));
        List<Dictionary<string, string>> chunks = ClientApi.InputTextMultiLineChunks(longText);
        foreach (Dictionary<string, string> chunk in chunks)
        {
            await SendAsync(chunk);
        }
        await SendAsync(ClientApi.InputTextMultiLine(label: "long", title: "Long text", maxLength: 2000, end: chunks.Count));
    }

    // Saves a screenshot of this window to the player's TapFun photos.
    private async Task UploadScreenshotAsync()
    {
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize));
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        await CallAsync("photos", _tapfun!.UploadPhotoAsync(png.ToArray(), "screenshot.png", "image/png"));
    }

    private Task PostScoreAsync()
    {
        long score = Random.Shared.Next(1, 10000);
        Log($"   score={score}");
        return CallAsync("leaderboard post", _tapfun!.PostLeaderboardEntryAsync(Leaderboard, score, note: "from TapFunSample"));
    }

    private async Task GetTopScoresAsync()
    {
        ApiResult result = await _tapfun!.GetLeaderboardEntriesAsync(Leaderboard, limit: 10);
        if (!result.IsSuccess)
        {
            Log($"-> leaderboard get: {result}");
            return;
        }
        // The body has the user IDs of other players. Do not show them on screen: show names and scores.
        List<LeaderboardEntry> entries = LeaderboardEntry.ParseEntries(result.Body);
        Log($"-> leaderboard get: HTTP {result.StatusCode}, {entries.Count} entries");
        foreach (LeaderboardEntry entry in entries)
        {
            string me = entry.UserId == _args.UserId ? " (you)" : "";
            Log($"   #{entry.Position} {entry.UserName}{me} {entry.Score} {entry.Note}");
        }
    }

    private async Task SendAsync(IReadOnlyDictionary<string, string> request)
    {
        ApiResult result = await _tapfun!.SendAsync(request);
        Log($"-> {request["req"]}: {result}");
    }

    private async Task<ApiResult> CallAsync(string name, Task<ApiResult> call)
    {
        ApiResult result = await call;
        Log($"-> {name}: {result}");
        return result;
    }

    private async Task RunAsync(Func<Task> run)
    {
        if (_tapfun == null)
        {
            Log("not sent: launch arguments are missing");
            return;
        }
        await run();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        int index = e.KeyCode - Keys.A;
        if (index >= 0 && index < _actions.Count && e.Modifiers == Keys.None)
        {
            e.Handled = true;
            _ = RunAsync(_actions[index].Run);
        }
    }

    private void Log(string line)
    {
        // A TextBox breaks lines only at \r\n.
        _log.AppendText($"{DateTime.Now:HH:mm:ss} {line.Replace("\n", Environment.NewLine)}{Environment.NewLine}");
    }

    private static string Mask(string value) => value == "" ? "(none)" : "***";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tapfun?.Dispose();
        }
        base.Dispose(disposing);
    }
}
