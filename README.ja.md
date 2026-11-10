# TapFun クラウドゲーミング（CAR）のサンプルコード

[English](README.md)

TapFun のクラウドゲーミング（Tencent Cloud CAR）で動くゲーム向けのサンプルコードです。exe が次のことをする書き方を示します。

- TapFun が渡す起動パラメータを読む
- TapFun へ[クライアント API](https://tapfun.dev/ja/cloudgaming/#client-api) のリクエストを送り、応答を UDP で受け取る
- [Web API](https://tapfun.dev/ja/cloudgaming/#web-api)（写真・ランキング）を呼ぶ

| フォルダ | 中身 | 言語 | 動く版 |
| --- | --- | --- | --- |
| [`csharp/`](csharp) | ゲームにコピーして使う C# のコード（テスト付き） | C# | Unity 2021.3 以降（未検証）、.NET 8 以降 |
| [`unity/`](unity) | `csharp/` を使う Unity のサンプルスクリプト | C# | Unity 2021.3 以降（未検証） |
| [`dotnet/`](dotnet) | `csharp/` を使う Windows のサンプル exe（WinForms） | C# | .NET 10 |
| [`godot/`](godot) | Godot のサンプルプロジェクト | GDScript | Godot 4.2 以降（4.7 でビルド） |

仕様の全体は[デベロッパーポータル](https://tapfun.dev/ja/cloudgaming/)にあります。

## 仕組み

1. Publisher Console の「exe 起動パラメータ」にプレースホルダを書きます（[起動パラメータ](https://tapfun.dev/ja/cloudgaming/#runtime-launch-params)）。「デフォルト起動パラメータを設定」ボタンを押すと、次の値が入ります。

   ```text
   --type car --tapfun-user-id {userId} --api-host {apiHost} --bearer-token {bearerToken} --sig-key {sigKey} --port {port} --username {username} --is-paid-timer {isPaidTimer} --is-sp {isSp} --dlcs {dlcs}
   ```

   TapFun は exe を起動するたびに、プレースホルダをプレイヤーごとの値に置き換えます。サンプルが使うのは `{userId}`、`{apiHost}`、`{bearerToken}`、`{port}` です。
2. exe は UDP `127.0.0.1:{port}`（通常は `10100`）で待ち受け、受け取ったメッセージごとに `OK` を返します。
3. exe はリクエストを `POST https://{apiHost}/api/v1/custom_event?userId={userId}` で送ります。`Authorization: Bearer {bearerToken}` を付け、本文は `req=getPlayerEnvironment` のような `application/x-www-form-urlencoded` です。**`User-Agent` ヘッダーは必須**で、無いリクエストは 403 になります。
4. TapFun はリクエストをプレイヤーのブラウザに届けます。応答は `from=getPlayerEnvironment&isSP=0&authenticated=1&...` のようなクエリストリングで UDP に返ってきます。TapFun からリクエストなしで届く通知もあり、これには `req=clientStatus` が付きます。
5. Web API は、同じ Bearer token で exe から直接呼びます。`POST /api/v1/photos?userId={userId}`、`GET` / `POST /api/v1/leaderboard/entries`、`POST /api/v1/leaderboard/entries/delete` で、応答は JSON です。

## サンプルがすること

Unity・.NET・Godot のサンプルは同じことをします。

1. 起動引数を読む
2. UDP `127.0.0.1:{port}` で待ち受ける
3. `launchSuccessNotice` を送る。起動から 5 秒たったら `getPlayerEnvironment` を送り、応答が届くまで 3 秒ごとに送り直す（5 回まで）
4. ほかの API をすべてボタン（または A〜L キー）で呼び、返ってきたものを表示する

| キー | ボタン | API |
| --- | --- | --- |
| A | getPlayerEnvironment | クライアント API |
| B | openUrl | クライアント API |
| C | inputTextSingleLine | クライアント API |
| D | inputTextMultiLine | クライアント API。入力された文字列は 128 文字ずつに分かれて届く（`seq` 1〜`end`）ので、サンプルでつなげる |
| E | inputTextMultiLine (long text) | クライアント API。長い初期テキストを `inputTextMultiLineChunkText` で先に送り、`end` を付けて `inputTextMultiLine` を送る |
| F | showText | クライアント API |
| G | copyText | クライアント API |
| H | consoleLog | クライアント API |
| I | photos (screenshot) | Web API。ゲーム画面のスクリーンショットをプレイヤーの TapFun の写真に保存する |
| J | leaderboard: post score | Web API。ランキング `sample` にランダムなスコアを登録する |
| K | leaderboard: get top 10 | Web API。名前とスコアを表示する |
| L | leaderboard: delete mine | Web API |

ログはゲームの画面に出します。コンソールウィンドウは配信映像に映らないためです。

ゲームに組み込むときの注意:

- TapFun ユーザー ID と Bearer token は画面に出さないでください。サンプルでは伏せて表示し、ランキングも名前とスコアだけを表示しています。
- クライアント API で送るデータはプレイヤーのブラウザを通ります。秘匿情報は入れないでください。
- exe の起動直後に送ったリクエストは、応答が届かないことがあります。応答を使うリクエストは、起動から 5 秒以上たってから送ってください。3 秒待っても応答が届かなければ、送り直してください。送り直すのは `getPlayerEnvironment` だけにしてください。
- zip 内のフォルダ名と exe のファイル名は、半角英数字と記号だけにしてください。
- TapFun が受け取ったリクエストは、ゲームのページを `?debugLog=1` 付きで開くと見られます。

## C#（Unity・.NET）

ゲームにコピーして使うのは `csharp/TapFun/` です。ゲームエンジンにも UI にも依存しない C# で、Unity 2021.3 以降が対応している C# 9 と .NET Standard 2.1 の範囲で書いています。

```csharp
using TapFun;

var tapfun = new TapFunClient(LaunchArgs.FromCommandLine(), "MyGame/1.0"); // User-Agent は必須
tapfun.MessageReceived += message => { /* from=getPlayerEnvironment、req=clientStatus など */ };
tapfun.InputTextMultiLineReceived += (label, result, text) => { /* つなげたテキスト */ };
tapfun.Listen(); // UDP 127.0.0.1:{port}
await tapfun.SendAsync(ClientApi.LaunchSuccessNotice());
// ゲームの起動から ClientApi.StartupWait（5 秒）たってから:
var environment = await tapfun.GetPlayerEnvironmentAsync(); // 応答が届くまで送り直す。届かなければ null
ApiResult result = await tapfun.PostLeaderboardEntryAsync("stage1", 1200);
```

- `ClientApi` がクライアント API のリクエストを作ります。`TapFunClient` がそれを送り、Web API も呼びます。Web API は `UploadPhotoAsync`、`GetLeaderboardEntriesAsync`（エントリーは `LeaderboardEntry.ParseEntries` で読みます）、`PostLeaderboardEntryAsync`、`DeleteLeaderboardEntryAsync` です。失敗しても例外は出さないので、`ApiResult.IsSuccess` で確かめてください。
- `GetPlayerEnvironmentAsync` は、応答が届くまで `ClientApi.RetryInterval`（3 秒）ごとに getPlayerEnvironment を送り直します（5 回まで）。ゲームの起動から `ClientApi.StartupWait` たってから呼んでください。
- イベントは、`Listen()` を呼んだスレッドに `SynchronizationContext` があればそのスレッドで起きます。Unity ならメインスレッド、WinForms・WPF なら UI スレッドです。`await` の後もそのスレッドに戻ります。
- ゲームを終えるときに `TapFunClient` を Dispose してください。

テストは次のとおりです（.NET 8 と .NET 10 のランタイムが要ります。片方しか無いときは `-f net10.0` か `-f net8.0` を付けます）。

```sh
dotnet test csharp/TapFun.Tests
```

`csharp/TapFun.csproj` は C# 9・.NET Standard 2.1 で、警告もエラーとしてコンパイルします。Unity に無いものをコードで使うと、ここでビルドが失敗します。

## Unity

**Unity 向けのコードは参考実装です。Unity で実際にビルドして試したことはありません。** `csharp/TapFun/` のテストは .NET でだけ行っています。お使いのプロジェクトで動くことを確かめてから使ってください。

1. `csharp/TapFun/` をプロジェクトの `Assets` フォルダにコピーします。
2. **Player Settings > Other Settings > Api Compatibility Level** は .NET Standard 2.1（既定）のままにします。
3. サンプルを試すときは、`unity/TapFunSample.cs` も `Assets` にコピーして Windows 向けにビルドします。読み込まれたシーンにサンプルの画面を足すので、ゲームには入れないでください。

IL2CPP でビルドするときは次の点に気をつけてください。

- Windows 向けの IL2CPP ビルドは Windows 上でしかできません。Visual Studio の「C++ によるデスクトップ開発」ワークロードも要ります。
- **`<プロダクト名>_BackUpThisFolder_ButDontShipItWithYourGame` は zip に入れないでください。** IL2CPP がデバッグ用に exe の隣へ出力するフォルダです。

## Godot

`godot/` を Godot 4.7 で開きます。

- ビルド: **プロジェクト > エクスポート > Windows Desktop**。プリセットは pck を埋め込むので、exe は 1 ファイルです（`godot/build/TapFunSample/TapFunSample.exe`）。`TapFunSample` フォルダを zip にしてください。
- コマンドラインなら次のとおりです（4.7 のエクスポートテンプレートが要ります）。

  ```sh
  godot --headless --path godot --export-release "Windows Desktop" build/TapFunSample/TapFunSample.exe
  ```

ゲームに組み込むときは `tapfun/tapfun_client.gd` をプロジェクトにコピーします。`TapFunClient` ノードを足し（または自動読み込みに登録し）、`listen()` を一度呼び、`send(TapFunClient.xxx())` でリクエストを送ります（次のリクエストを待たせるときは `await` します）。応答は `message_received` シグナルに、つなげた inputTextMultiLine の応答は `input_text_multi_line_received` に届きます。`fetch_player_environment()` は、応答が届くまで `RETRY_INTERVAL_SEC`（3 秒）ごとに getPlayerEnvironment を送り直します。起動から `STARTUP_WAIT_SEC`（5 秒）たってから呼んでください。Web API は `upload_photo()`、`get_leaderboard_entries()`、`post_leaderboard_entry()`、`delete_leaderboard_entry()` で、`await` すると結果が受け取れます。

Godot は自分で使わないオプションを `OS.get_cmdline_args()` に残すので、起動パラメータはそのまま読めます。オプションを `--` の後ろに書いた場合は `OS.get_cmdline_user_args()` に入ります。`TapFunClient` は両方を読みます。

## .NET

```sh
cd dotnet
./build.sh
```

.NET 10 SDK が要ります。Windows（Git Bash）、macOS、Linux で動きます。自己完結型の exe を `dotnet/publish/TapFunSample/` に、zip を `dotnet/publish/TapFunSample.zip` に出力します。

`dotnet/TapFun.Sample/` は `csharp/TapFun.csproj` を参照する小さな WinForms アプリです。自分の .NET のゲーム（MonoGame、Godot .NET など）では、このプロジェクトを参照するか `csharp/TapFun/` をコピーしてください。

## ライセンス

[MIT No Attribution（MIT-0）](LICENSE)。著作権表示を残さずに、コードをゲームにコピーして使えます。
