# TapFun sample code for cloud gaming (CAR)

[日本語](README.ja.md)

Sample code for games that run on TapFun cloud gaming (Tencent Cloud CAR). It shows how the exe

- reads the launch parameters that TapFun passes,
- sends [client API](https://tapfun.dev/en/cloudgaming/#client-api) requests to TapFun and receives the answers over UDP,
- calls the [Web API](https://tapfun.dev/en/cloudgaming/#web-api) (photos and leaderboards).

| Folder | What it is | Language | Works with |
| --- | --- | --- | --- |
| [`csharp/`](csharp) | The C# code to copy into your game, with tests | C# | Unity 2021.3 or later (untested), .NET 8 or later |
| [`unity/`](unity) | Sample script for Unity that uses `csharp/` | C# | Unity 2021.3 or later (untested) |
| [`dotnet/`](dotnet) | Sample Windows exe (WinForms) that uses `csharp/` | C# | .NET 10 |
| [`godot/`](godot) | Sample project for Godot | GDScript | Godot 4.2 or later (built with 4.7) |

The full specification is in the [developer portal](https://tapfun.dev/en/cloudgaming/).

## How it works

1. In the Publisher Console, write placeholders into "exe launch parameters" ([Launch parameters](https://tapfun.dev/en/cloudgaming/#runtime-launch-params)). The "Set default launch arguments" button writes:

   ```text
   --type car --tapfun-user-id {userId} --api-host {apiHost} --bearer-token {bearerToken} --sig-key {sigKey} --port {port} --username {username} --is-paid-timer {isPaidTimer} --is-sp {isSp} --dlcs {dlcs}
   ```

   TapFun replaces the placeholders with each player's values when it launches the exe. The samples need `{userId}`, `{apiHost}`, `{bearerToken}` and `{port}`.
2. The exe listens on UDP `127.0.0.1:{port}` (usually `10100`) and replies `OK` to each message.
3. The exe sends a request as `POST https://{apiHost}/api/v1/custom_event?userId={userId}` with `Authorization: Bearer {bearerToken}` and an `application/x-www-form-urlencoded` body such as `req=getPlayerEnvironment`. **A `User-Agent` header is required**; requests without one are rejected with 403.
4. TapFun forwards the request to the player's browser. Answers come back over UDP as query strings, for example `from=getPlayerEnvironment&isSP=0&authenticated=1&...`. TapFun also sends notifications on its own, with `req=clientStatus`.
5. The Web API is called directly from the exe with the same Bearer token: `POST /api/v1/photos?userId={userId}` and `GET` / `POST /api/v1/leaderboard/entries`, `POST /api/v1/leaderboard/entries/delete`. It answers with JSON.

## What the samples do

The Unity, .NET and Godot samples do the same thing:

1. Read the launch arguments.
2. Listen on UDP `127.0.0.1:{port}`.
3. Send `launchSuccessNotice`. Then, 5 seconds after the start, send `getPlayerEnvironment`, and send it again every 3 seconds until the answer arrives (up to 5 times).
4. Call every other API from buttons (or the A to L keys) and show what comes back:

| Key | Button | API |
| --- | --- | --- |
| A | getPlayerEnvironment | Client API |
| B | openUrl | Client API |
| C | inputTextSingleLine | Client API |
| D | inputTextMultiLine | Client API. The entered text comes back in 128-character chunks (`seq` 1 to `end`); the samples join them |
| E | inputTextMultiLine (long text) | Client API. Sends a long initial text with `inputTextMultiLineChunkText` first, then `inputTextMultiLine` with `end` |
| F | showText | Client API |
| G | copyText | Client API |
| H | consoleLog | Client API |
| I | photos (screenshot) | Web API. Saves a screenshot of the game window to the player's TapFun photos |
| J | leaderboard: post score | Web API. Posts a random score to the leaderboard `sample` |
| K | leaderboard: get top 10 | Web API. Shows names and scores |
| L | leaderboard: delete mine | Web API |

The log is drawn in the game window, because console windows do not appear in the stream.

Points to keep in mind in your game:

- Do not show the TapFun user ID or the Bearer token on screen. The samples mask them, and show only names and scores from the leaderboard.
- Data sent with the client API passes through the player's browser. Do not put secrets in it.
- Answers to requests sent right after the exe starts can be lost. Send requests whose answer you need 5 seconds or more after the start. If no answer arrives in 3 seconds, send the request again. Send again only `getPlayerEnvironment`.
- Use only ASCII letters, digits and symbols in the folder name inside the zip and in the exe file name.
- To see the requests TapFun received, open the game page with `?debugLog=1`.

## C# (Unity and .NET)

`csharp/TapFun/` is the part to copy into your game. It is plain C# with no engine or UI dependency, written for C# 9 and .NET Standard 2.1, which Unity 2021.3 and later support.

```csharp
using TapFun;

var tapfun = new TapFunClient(LaunchArgs.FromCommandLine(), "MyGame/1.0"); // A User-Agent is required.
tapfun.MessageReceived += message => { /* from=getPlayerEnvironment, req=clientStatus, ... */ };
tapfun.InputTextMultiLineReceived += (label, result, text) => { /* the chunks joined */ };
tapfun.Listen(); // UDP 127.0.0.1:{port}
await tapfun.SendAsync(ClientApi.LaunchSuccessNotice());
// After ClientApi.StartupWait (5 seconds) has passed since the game started:
var environment = await tapfun.GetPlayerEnvironmentAsync(); // Sent again until the answer arrives; null if none.
ApiResult result = await tapfun.PostLeaderboardEntryAsync("stage1", 1200);
```

- `ClientApi` builds the client API requests. `TapFunClient` sends them and calls the Web API: `UploadPhotoAsync`, `GetLeaderboardEntriesAsync` (read the entries with `LeaderboardEntry.ParseEntries`), `PostLeaderboardEntryAsync` and `DeleteLeaderboardEntryAsync`. The calls do not throw on errors; check `ApiResult.IsSuccess`.
- `GetPlayerEnvironmentAsync` sends getPlayerEnvironment again every `ClientApi.RetryInterval` (3 seconds) until the answer arrives, up to 5 times. Call it after `ClientApi.StartupWait` has passed since the game started.
- The events are raised on the thread that called `Listen()` when it has a `SynchronizationContext`: the main thread in Unity, the UI thread in WinForms and WPF. `await` also returns to that thread.
- Dispose the `TapFunClient` when the game ends.

Tests (needs the .NET 8 and .NET 10 runtimes; with only one of them, add `-f net10.0` or `-f net8.0`):

```sh
dotnet test csharp/TapFun.Tests
```

`csharp/TapFun.csproj` compiles the code with C# 9 and .NET Standard 2.1, with warnings treated as errors, so the build fails when the code uses something Unity does not have.

## Unity

**The Unity code is a reference implementation. It has not been built or tested in Unity.** `csharp/TapFun/` is tested on .NET only. Check that it works in your project before you use it.

1. Copy `csharp/TapFun/` into the `Assets` folder of your project.
2. Keep **Player Settings > Other Settings > Api Compatibility Level** at .NET Standard 2.1 (the default).
3. To try the sample, also copy `unity/TapFunSample.cs` into `Assets`, then build for Windows. It adds its screen to whatever scene is loaded, so do not put it in your game.

When you build with IL2CPP:

- An IL2CPP build for Windows has to run on Windows, with Visual Studio's "Desktop development with C++" workload.
- **Do not put `<Product name>_BackUpThisFolder_ButDontShipItWithYourGame` in the zip.** IL2CPP writes it next to the exe for debugging.

## Godot

Open `godot/` with Godot 4.7.

- Build: **Project > Export > Windows Desktop**. The preset embeds the pck, so the exe is a single file (`godot/build/TapFunSample/TapFunSample.exe`). Zip the `TapFunSample` folder.
- From the command line (needs the 4.7 export templates):

  ```sh
  godot --headless --path godot --export-release "Windows Desktop" build/TapFunSample/TapFunSample.exe
  ```

To use it in your game, copy `tapfun/tapfun_client.gd` into your project. Add a `TapFunClient` node (or register the script as an autoload), call `listen()` once and send requests with `send(TapFunClient.xxx())` (`await` it when the next request must wait for it). Answers arrive through the `message_received` signal, and the joined answer of inputTextMultiLine through `input_text_multi_line_received`. `fetch_player_environment()` sends getPlayerEnvironment again every `RETRY_INTERVAL_SEC` (3 seconds) until the answer arrives; call it after `STARTUP_WAIT_SEC` (5 seconds) has passed since the start. The Web API is `upload_photo()`, `get_leaderboard_entries()`, `post_leaderboard_entry()` and `delete_leaderboard_entry()`; `await` them to get the result.

Godot keeps options it does not use itself in `OS.get_cmdline_args()`, so the launch parameters can be read as they are. If you write your options after `--`, they are in `OS.get_cmdline_user_args()` instead. `TapFunClient` reads both.

## .NET

```sh
cd dotnet
./build.sh
```

This needs the .NET 10 SDK and works on Windows (Git Bash), macOS and Linux. It writes a self-contained exe to `dotnet/publish/TapFunSample/` and the zip to `dotnet/publish/TapFunSample.zip`.

`dotnet/TapFun.Sample/` is a small WinForms app that references `csharp/TapFun.csproj`. In your own .NET game (MonoGame, Godot .NET, ...), reference the project or copy `csharp/TapFun/`.

## License

[MIT No Attribution (MIT-0)](LICENSE). You can copy the code into your game without keeping the copyright notice.
