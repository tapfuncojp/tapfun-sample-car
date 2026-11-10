#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TapFun;
using UnityEngine;

/// <summary>
/// Sample screen for Unity: buttons on the left, log on the right (IMGUI, so no scene setup or UI package is needed).
/// The buttons can also be pressed with the A to L keys.
///
/// This is a reference implementation: it has not been built or tested in Unity.
///
/// Put the .cs files of csharp/TapFun/ in your Assets folder, then this file. It adds itself to whatever scene is loaded,
/// so do not put it in your game.
///
/// What it does:
///   1. Reads the launch arguments.
///   2. Listens for messages from TapFun on UDP 127.0.0.1:{port}.
///   3. Sends launchSuccessNotice, then getPlayerEnvironment (sent again until the answer arrives).
///   4. Calls every other client API and Web API from the buttons and shows the answers.
/// </summary>
public sealed class TapFunSample : MonoBehaviour
{
    private const string Leaderboard = "sample";

    private readonly List<string> _log = new List<string>();
    private TapFunClient? _tapfun;
    private (string Label, Func<Task> Run)[] _actions = Array.Empty<(string, Func<Task>)>();
    private Vector2 _scroll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Camera.main == null)
        {
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        }
        new GameObject("TapFun Sample").AddComponent<TapFunSample>();
    }

    // await in a Unity message returns to the main thread, so Unity APIs can be used after it.
    private async void Start()
    {
        _actions = new (string, Func<Task>)[]
        {
            // Client API
            ("getPlayerEnvironment", () => Send(ClientApi.GetPlayerEnvironment())),
            ("openUrl", () => Send(ClientApi.OpenUrl("https://tapfun.co.jp/"))),
            ("inputTextSingleLine", () => Send(ClientApi.InputTextSingleLine(
                label: "nickname", title: "Nickname", placeholder: "Enter your nickname", maxLength: 12))),
            ("inputTextMultiLine", () => Send(ClientApi.InputTextMultiLine(
                label: "message", title: "Message", text: "Line 1\nLine 2", maxLength: 1000))),
            ("inputTextMultiLine (long text)", InputLongText),
            ("showText", () => Send(ClientApi.ShowText("Hello from the game."))),
            ("copyText", () => Send(ClientApi.CopyText("SAMPLE-CODE-1234", title: "Your code"))),
            ("consoleLog", () => Send(ClientApi.ConsoleLog($"{{\"time\":\"{DateTime.UtcNow:O}\"}}"))),
            // Web API
            ("photos (screenshot)", () =>
            {
                StartCoroutine(UploadScreenshot());
                return Task.CompletedTask;
            }),
            ("leaderboard: post score", PostScore),
            ("leaderboard: get top 10", GetTopScores),
            ("leaderboard: delete mine", () => Call("leaderboard delete", _tapfun!.DeleteLeaderboardEntryAsync(Leaderboard))),
        };

        // 1. Launch arguments. Do not show the TapFun user ID or the Bearer token on screen.
        LaunchArgs args = LaunchArgs.FromCommandLine();
        Log($"apiHost={args.ApiHost} port={args.Port} userId={Mask(args.UserId)} bearerToken={Mask(args.BearerToken)}");
        Log($"username={args.Username} isPaidTimer={args.IsPaidTimer} isSp={args.IsSp} dlcs=[{string.Join(",", args.Dlcs)}]");
        if (!args.HasClientApiValues)
        {
            Log("--tapfun-user-id, --api-host and --bearer-token are required. Add {userId}, {apiHost} and {bearerToken} to the exe launch parameters.");
            return;
        }
        _tapfun = new TapFunClient(args, "TapFunSampleUnity/1.0");
        _tapfun.MessageReceived += OnMessage;
        _tapfun.InputTextMultiLineReceived += (label, result, text) =>
            // The multi-line answer comes in chunks; this is called when all of them have arrived.
            Log(result == "submit" ? $"   entered ({label}, {text.Length} chars): \"{text}\"" : $"   {result} ({label})");

        // 2. Messages from TapFun. Listen on the main thread, so that the events are raised on it.
        try
        {
            _tapfun.Listen();
            Log($"listening on UDP 127.0.0.1:{args.Port}");
        }
        catch (Exception e)
        {
            Log($"UDP listen failed: {e.Message}");
        }

        // 3. Startup requests. Send launchSuccessNotice once the game is ready to play.
        await Send(ClientApi.LaunchSuccessNotice());

        // Answers to requests sent soon after the game starts can be lost: wait until StartupWait has passed
        // since the start. GetPlayerEnvironmentAsync sends the request again when no answer arrives.
        double wait = ClientApi.StartupWait.TotalSeconds - Time.realtimeSinceStartupAsDouble;
        if (wait > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(wait));
        }
        if (await _tapfun.GetPlayerEnvironmentAsync() == null)
        {
            Log("getPlayerEnvironment: no answer");
        }
    }

    private void OnDestroy()
    {
        _tapfun?.Dispose();
    }

    private void OnMessage(IReadOnlyDictionary<string, string> message)
    {
        string text = string.Join(" ", message.Select(p => $"{p.Key}={p.Value}"));
        if (message.TryGetValue("from", out string? from))
        {
            // Answer to a request: from=getPlayerEnvironment, from=inputTextSingleLine, ...
            Log($"<- {from}: {text}");
            if (from == "inputTextSingleLine" && message.TryGetValue("result", out string? result) && result == "submit")
            {
                Log($"   entered: \"{(message.TryGetValue("text", out string? entered) ? entered : "")}\"");
            }
        }
        else if (message.TryGetValue("req", out string? req) && req == "clientStatus")
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
    private async Task InputLongText()
    {
        string longText = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"Line {i}: this initial text is sent in chunks."));
        List<Dictionary<string, string>> chunks = ClientApi.InputTextMultiLineChunks(longText);
        foreach (Dictionary<string, string> chunk in chunks)
        {
            await Send(chunk);
        }
        await Send(ClientApi.InputTextMultiLine(label: "long", title: "Long text", maxLength: 2000, end: chunks.Count));
    }

    // Saves a screenshot of the game screen to the player's TapFun photos.
    private IEnumerator UploadScreenshot()
    {
        yield return new WaitForEndOfFrame();
        Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
        byte[] png = texture.EncodeToPNG();
        Destroy(texture);
        _ = Call("photos", _tapfun!.UploadPhotoAsync(png, "screenshot.png", "image/png"));
    }

    private Task PostScore()
    {
        long score = UnityEngine.Random.Range(1, 10000);
        Log($"   score={score}");
        return Call("leaderboard post", _tapfun!.PostLeaderboardEntryAsync(Leaderboard, score, note: "from TapFunSample"));
    }

    private async Task GetTopScores()
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
            string me = entry.UserId == _tapfun!.Args.UserId ? " (you)" : "";
            Log($"   #{entry.Position} {entry.UserName}{me} {entry.Score} {entry.Note}");
        }
    }

    private async Task Send(Dictionary<string, string> request)
    {
        ApiResult result = await _tapfun!.SendAsync(request);
        Log($"-> {request["req"]}: {result}");
    }

    private async Task<ApiResult> Call(string name, Task<ApiResult> call)
    {
        ApiResult result = await call;
        Log($"-> {name}: {result}");
        return result;
    }

    private void Run(int index)
    {
        if (_tapfun == null)
        {
            Log("not sent: launch arguments are missing");
            return;
        }
        _ = _actions[index].Run();
    }

    private void OnGUI()
    {
        // IMGUI key events work with both the Input Manager and the Input System package.
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.modifiers == EventModifiers.None &&
            e.keyCode >= KeyCode.A && e.keyCode < KeyCode.A + _actions.Length)
        {
            Run(e.keyCode - KeyCode.A);
            e.Use();
        }

        GUI.skin.button.fontSize = 18;
        GUI.skin.button.alignment = TextAnchor.MiddleLeft;
        GUI.skin.label.fontSize = 16;
        GUI.skin.label.wordWrap = true;

        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(340));
        for (int i = 0; i < _actions.Length; i++)
        {
            if (GUILayout.Button($"{(char)('A' + i)}) {_actions[i].Label}", GUILayout.Height(44)))
            {
                Run(i);
            }
        }
        GUILayout.EndVertical();
        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.Label(string.Join("\n", _log));
        GUILayout.EndScrollView();
        GUILayout.EndHorizontal();
    }

    private void Log(string line)
    {
        Debug.Log(line); // Also goes to Player.log.
        _log.Add($"{DateTime.Now:HH:mm:ss} {line}");
        _scroll.y = float.MaxValue;
    }

    private static string Mask(string value) => value == "" ? "(none)" : "***";
}
