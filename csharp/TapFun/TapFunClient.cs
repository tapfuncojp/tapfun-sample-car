#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TapFun
{
    /// <summary>
    /// TapFun client API and Web API for games running on TapFun cloud gaming (Tencent Cloud CAR).
    ///
    /// <list type="number">
    /// <item>Create it with <see cref="LaunchArgs.FromCommandLine"/> and your User-Agent.</item>
    /// <item>Call <see cref="Listen"/> once to receive answers and notifications from TapFun.</item>
    /// <item>Send requests built with <see cref="ClientApi"/> with <see cref="SendAsync"/>,
    /// and call the Web API with the other methods.</item>
    /// <item>Dispose it when the game ends.</item>
    /// </list>
    ///
    /// Plain C# with no engine dependency: it works in Unity and in .NET apps.
    /// The methods can be called from any thread, and awaiting them returns to the caller's thread.
    /// </summary>
    public sealed class TapFunClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly MultiLineTextAssembler _multiLine = new MultiLineTextAssembler();
        private UdpClient? _udp;
        private SynchronizationContext? _context;
        private volatile bool _disposed;

        /// <param name="args">Launch arguments that contain the user ID, API host, Bearer token and port.</param>
        /// <param name="userAgent">Your app name and version, for example <c>MyGame/1.0</c>.
        /// A User-Agent header is required: requests without one are rejected with 403.</param>
        public TapFunClient(LaunchArgs args, string userAgent)
            : this(args, userAgent, new HttpClientHandler())
        {
        }

        /// <summary>Same as the other constructor, with your own <see cref="HttpMessageHandler"/> (for example a proxy, or a fake in tests).</summary>
        public TapFunClient(LaunchArgs args, string userAgent, HttpMessageHandler handler)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                throw new ArgumentException("A User-Agent is required.", nameof(userAgent));
            }
            Args = args;
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", args.BearerToken);
            // HttpClient sends no User-Agent by default.
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        }

        /// <summary>The launch arguments passed to the constructor.</summary>
        public LaunchArgs Args { get; }

        /// <summary>
        /// Raised for each message from TapFun, with the query string decoded.
        /// Answers have a <c>from</c> key (for example <c>from=getPlayerEnvironment</c>).
        /// Notifications that TapFun sends on its own have <c>req=clientStatus</c>.
        /// </summary>
        public event Action<IReadOnlyDictionary<string, string>>? MessageReceived;

        /// <summary>
        /// Raised when the answer of inputTextMultiLine is complete, with the label, the result
        /// (<c>submit</c> or <c>cancel</c>) and the text. TapFun sends the text in 128-character chunks; this joins them.
        /// The chunks are also raised one by one through <see cref="MessageReceived"/>.
        /// </summary>
        public event Action<string, string, string>? InputTextMultiLineReceived;

        private string BaseUrl => $"https://{Args.ApiHost}/api/v1";

        private string UserIdQuery => Uri.EscapeDataString(Args.UserId);

        // --- Receiving ---

        /// <summary>
        /// Starts listening on UDP <c>127.0.0.1:{port}</c> and replies <c>OK</c> to each message.
        /// The events are raised on the thread that called this, when it has a <see cref="SynchronizationContext"/>
        /// (the main thread in Unity, the UI thread in WinForms or WPF). Otherwise they are raised on a background thread.
        /// </summary>
        /// <exception cref="SocketException">The port is in use.</exception>
        public void Listen()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TapFunClient));
            }
            if (_udp != null)
            {
                throw new InvalidOperationException("Already listening.");
            }
            _context = SynchronizationContext.Current;
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, Args.Port));
            _ = ReceiveLoopAsync(_udp);
        }

        private async Task ReceiveLoopAsync(UdpClient udp)
        {
            byte[] ok = Encoding.UTF8.GetBytes("OK");
            while (!_disposed)
            {
                UdpReceiveResult received;
                try
                {
                    received = await udp.ReceiveAsync().ConfigureAwait(false);
                    await udp.SendAsync(ok, ok.Length, received.RemoteEndPoint).ConfigureAwait(false);
                }
                catch (Exception) when (_disposed)
                {
                    return;
                }
                catch (SocketException)
                {
                    // Keep listening after a transient error.
                    await Task.Delay(1000).ConfigureAwait(false);
                    continue;
                }
                Dictionary<string, string> message = QueryString.Decode(Encoding.UTF8.GetString(received.Buffer));
                if (_context != null)
                {
                    _context.Post(_ => Dispatch(message), null);
                }
                else
                {
                    Dispatch(message);
                }
            }
        }

        private void Dispatch(Dictionary<string, string> message)
        {
            if (_disposed)
            {
                return;
            }
            MessageReceived?.Invoke(message);
            if (message.TryGetValue("from", out string? from) && from == "inputTextMultiLine" &&
                _multiLine.TryAdd(message, out string label, out string result, out string text))
            {
                InputTextMultiLineReceived?.Invoke(label, result, text);
            }
        }

        // --- Client API ---

        /// <summary>
        /// Sends one client API request built with <see cref="ClientApi"/>:
        /// <c>POST /api/v1/custom_event?userId={userId}</c> with an <c>application/x-www-form-urlencoded</c> body.
        /// TapFun forwards it to the player's browser. Await it when the next request must arrive after this one.
        /// </summary>
        public Task<ApiResult> SendAsync(IReadOnlyDictionary<string, string> request) =>
            PostAsync($"{BaseUrl}/custom_event?userId={UserIdQuery}", new FormUrlEncodedContent(request));

        /// <summary>
        /// Sends getPlayerEnvironment and returns the answer. The answer can be lost when the request is sent
        /// soon after the game starts (see <see cref="ClientApi.StartupWait"/>), so when no answer arrives within
        /// <paramref name="retryInterval"/>, this sends the request again. Call <see cref="Listen"/> first.
        /// The answer is also raised through <see cref="MessageReceived"/>.
        /// </summary>
        /// <param name="attempts">How many times to send the request at most.</param>
        /// <param name="retryInterval">How long to wait for an answer before sending again.
        /// Null means <see cref="ClientApi.RetryInterval"/>.</param>
        /// <returns>The answer, or null when none arrived.</returns>
        /// <exception cref="InvalidOperationException"><see cref="Listen"/> has not been called.</exception>
        public async Task<IReadOnlyDictionary<string, string>?> GetPlayerEnvironmentAsync(int attempts = 5, TimeSpan? retryInterval = null)
        {
            if (_udp == null)
            {
                throw new InvalidOperationException("Call Listen() first.");
            }
            var answer = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<IReadOnlyDictionary<string, string>> onMessage = message =>
            {
                if (message.TryGetValue("from", out string? from) && from == "getPlayerEnvironment")
                {
                    answer.TrySetResult(message);
                }
            };
            MessageReceived += onMessage;
            try
            {
                for (int i = 0; i < attempts && !_disposed; i++)
                {
                    await SendAsync(ClientApi.GetPlayerEnvironment()).ConfigureAwait(false);
                    Task wait = Task.Delay(retryInterval ?? ClientApi.RetryInterval);
                    if (await Task.WhenAny(answer.Task, wait).ConfigureAwait(false) == answer.Task)
                    {
                        return answer.Task.Result;
                    }
                }
                return null;
            }
            finally
            {
                MessageReceived -= onMessage;
            }
        }

        // --- Web API ---

        /// <summary>
        /// Saves an image to the player's TapFun photos: <c>POST /api/v1/photos?userId={userId}</c>
        /// (multipart/form-data, field <c>file</c>). PNG, JPEG or WebP, up to 10 MB.
        /// </summary>
        /// <param name="image">The image file.</param>
        /// <param name="fileName">File name with an extension, for example <c>screenshot.png</c>.</param>
        /// <param name="contentType"><c>image/png</c>, <c>image/jpeg</c> or <c>image/webp</c>.</param>
        public Task<ApiResult> UploadPhotoAsync(byte[] image, string fileName, string contentType)
        {
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            var content = new MultipartFormDataContent { { file, "file", fileName } };
            return PostAsync($"{BaseUrl}/photos?userId={UserIdQuery}", content);
        }

        /// <summary>
        /// Gets leaderboard entries: <c>GET /api/v1/leaderboard/entries</c>.
        /// Read them with <see cref="LeaderboardEntry.ParseEntries"/>.
        /// </summary>
        /// <param name="name">Leaderboard name.</param>
        /// <param name="ascending"><c>true</c> for the lowest score first. The default is the highest first.</param>
        /// <param name="around">1 to 100: gets this player's entry and this many entries before and after it,
        /// instead of from the top. Empty when the player has no entry.</param>
        /// <param name="offset">Number of entries to skip from the top (up to 9999).</param>
        /// <param name="limit">Number of entries (1 to 1000). 0 means the default (100).</param>
        public Task<ApiResult> GetLeaderboardEntriesAsync(string name, bool ascending = false, int around = 0, int offset = 0, int limit = 0)
        {
            var query = new List<string> { "name=" + Uri.EscapeDataString(name) };
            if (ascending) query.Add("sort=asc");
            // The parameter name is "arround".
            if (around > 0) query.Add($"userId={UserIdQuery}&arround={around}");
            if (offset > 0) query.Add($"offset={offset}");
            if (limit > 0) query.Add($"limit={limit}");
            return SendHttpAsync(new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/leaderboard/entries?{string.Join("&", query)}"));
        }

        /// <summary>
        /// Saves this player's score: <c>POST /api/v1/leaderboard/entries</c>.
        /// A score less than or equal to the saved one is ignored unless <paramref name="force"/> is <c>true</c>.
        /// The leaderboard is created on the first entry.
        /// </summary>
        /// <param name="name">Leaderboard name: 1 to 64 letters, digits, <c>_</c> and <c>-</c>.</param>
        /// <param name="score">Score.</param>
        /// <param name="note">Extra information, up to 8192 characters.</param>
        /// <param name="force"><c>true</c> to save the score even when it is not higher.</param>
        public Task<ApiResult> PostLeaderboardEntryAsync(string name, long score, string note = "", bool force = false)
        {
            var fields = new Dictionary<string, string>
            {
                ["name"] = name,
                ["userId"] = Args.UserId,
                ["score"] = score.ToString(CultureInfo.InvariantCulture),
            };
            if (note != "") fields["note"] = note;
            if (force) fields["force"] = "1";
            return PostAsync($"{BaseUrl}/leaderboard/entries", new FormUrlEncodedContent(fields));
        }

        /// <summary>Deletes this player's entry: <c>POST /api/v1/leaderboard/entries/delete</c>. Succeeds even if there is none.</summary>
        /// <param name="name">Leaderboard name.</param>
        public Task<ApiResult> DeleteLeaderboardEntryAsync(string name) =>
            PostAsync($"{BaseUrl}/leaderboard/entries/delete", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["name"] = name,
                ["userId"] = Args.UserId,
            }));

        private Task<ApiResult> PostAsync(string url, HttpContent content) =>
            SendHttpAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = content });

        private async Task<ApiResult> SendHttpAsync(HttpRequestMessage request)
        {
            try
            {
                using (request)
                using (HttpResponseMessage response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new ApiResult((int)response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                // Connection failures and timeouts are returned with status 0.
                return new ApiResult(0, "", ex.GetBaseException().Message);
            }
        }

        /// <summary>Stops listening and releases the HTTP client.</summary>
        public void Dispose()
        {
            _disposed = true;
            _udp?.Dispose();
            _http.Dispose();
        }
    }
}
