using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TapFun.Tests;

public class TapFunClientUdpTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static int FreePort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static TapFunClient NewClient(int port, FakeHandler? handler = null) => new(
        LaunchArgs.Parse(new[] { "--tapfun-user-id", "PLYR1", "--api-host", "tapfun.co.jp", "--bearer-token", "1_a", "--port", port.ToString() }),
        "TapFunTests/1.0", handler ?? new FakeHandler());

    // Answers getPlayerEnvironment over UDP like TapFun does, from the given request on: the answers before it are lost.
    private static FakeHandler AnswerFrom(int firstAnswered, UdpClient sender, int port)
    {
        int count = 0;
        var handler = new FakeHandler();
        handler.Respond = _ =>
        {
            if (Interlocked.Increment(ref count) >= firstAnswered)
            {
                byte[] data = Encoding.UTF8.GetBytes("from=getPlayerEnvironment&isSP=0");
                sender.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, port));
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":\"success\"}") };
        };
        return handler;
    }

    // Sends like TapFun does, and returns the reply.
    private static async Task<string> SendAsync(UdpClient sender, int port, string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        await sender.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Loopback, port));
        UdpReceiveResult reply = await sender.ReceiveAsync().WaitAsync(Timeout);
        return Encoding.UTF8.GetString(reply.Buffer);
    }

    [Fact]
    public async Task RepliesOkAndRaisesTheDecodedMessage()
    {
        int port = FreePort();
        using TapFunClient client = NewClient(port);
        var received = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += message => received.TrySetResult(message);
        client.Listen();
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        string reply = await SendAsync(sender, port, "from=getPlayerEnvironment&userAgent=Mozilla%2F5.0%20(Windows)&isSP=0");

        Assert.Equal("OK", reply);
        IReadOnlyDictionary<string, string> message = await received.Task.WaitAsync(Timeout);
        Assert.Equal(new Dictionary<string, string>
        {
            ["from"] = "getPlayerEnvironment", ["userAgent"] = "Mozilla/5.0 (Windows)", ["isSP"] = "0",
        }, message);
    }

    [Fact]
    public async Task JoinsTheChunksOfInputTextMultiLine()
    {
        int port = FreePort();
        using TapFunClient client = NewClient(port);
        var messages = new ConcurrentQueue<IReadOnlyDictionary<string, string>>();
        var joined = new TaskCompletionSource<(string, string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += messages.Enqueue;
        client.InputTextMultiLineReceived += (label, result, text) => joined.TrySetResult((label, result, text));
        client.Listen();
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        // Out of order, as UDP may deliver them.
        await SendAsync(sender, port, "from=inputTextMultiLine&result=submit&label=m&text=%E3%81%84%0A&seq=2&end=3");
        await SendAsync(sender, port, "from=inputTextMultiLine&result=submit&label=m&text=%E3%81%82&seq=1&end=3");
        Assert.False(joined.Task.IsCompleted);
        await SendAsync(sender, port, "from=inputTextMultiLine&result=submit&label=m&text=end&seq=3&end=3");

        Assert.Equal(("m", "submit", "あい\nend"), await joined.Task.WaitAsync(Timeout));
        Assert.Equal(3, messages.Count);
    }

    [Fact]
    public async Task ReportsACancelledInputTextMultiLine()
    {
        int port = FreePort();
        using TapFunClient client = NewClient(port);
        var joined = new TaskCompletionSource<(string, string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.InputTextMultiLineReceived += (label, result, text) => joined.TrySetResult((label, result, text));
        client.Listen();
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        await SendAsync(sender, port, "from=inputTextMultiLine&result=cancel&label=m");

        Assert.Equal(("m", "cancel", ""), await joined.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task RaisesTheEventsOnTheSynchronizationContextOfListen()
    {
        int port = FreePort();
        using TapFunClient client = NewClient(port);
        using var context = new SingleThreadContext();
        var raised = new TaskCompletionSource<(bool, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += _ => raised.TrySetResult((SynchronizationContext.Current == context, Environment.CurrentManagedThreadId));
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            client.Listen();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        await SendAsync(sender, port, "req=clientStatus&result=lastTimeRemain&second=90");

        Assert.Equal((true, context.ThreadId), await raised.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task GetPlayerEnvironmentSendsAgainUntilAnAnswerArrives()
    {
        int port = FreePort();
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        FakeHandler handler = AnswerFrom(3, sender, port);
        using TapFunClient client = NewClient(port, handler);
        client.Listen();

        IReadOnlyDictionary<string, string>? answer = await client.GetPlayerEnvironmentAsync(5, TimeSpan.FromMilliseconds(300)).WaitAsync(Timeout);

        Assert.NotNull(answer);
        Assert.Equal("0", answer["isSP"]);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("req=getPlayerEnvironment", r.Body));
    }

    [Fact]
    public async Task GetPlayerEnvironmentReturnsNullWhenNoAnswerArrives()
    {
        int port = FreePort();
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        FakeHandler handler = AnswerFrom(int.MaxValue, sender, port);
        using TapFunClient client = NewClient(port, handler);
        client.Listen();

        IReadOnlyDictionary<string, string>? answer = await client.GetPlayerEnvironmentAsync(3, TimeSpan.FromMilliseconds(100)).WaitAsync(Timeout);

        Assert.Null(answer);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task GetPlayerEnvironmentThrowsBeforeListen()
    {
        using TapFunClient client = NewClient(FreePort());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetPlayerEnvironmentAsync());
    }

    [Fact]
    public void ListenThrowsWhenThePortIsInUse()
    {
        using var other = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)other.Client.LocalEndPoint!).Port;
        using TapFunClient client = NewClient(port);

        Assert.Throws<SocketException>(client.Listen);
    }

    [Fact]
    public void ListenTwiceThrows()
    {
        using TapFunClient client = NewClient(FreePort());
        client.Listen();

        Assert.Throws<InvalidOperationException>(client.Listen);
    }

    [Fact]
    public void DisposeReleasesThePort()
    {
        int port = FreePort();
        TapFunClient client = NewClient(port);
        client.Listen();

        client.Dispose();

        using var again = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        Assert.Throws<ObjectDisposedException>(client.Listen);
    }

    /// <summary>A SynchronizationContext with one thread, like the main thread of Unity or a UI thread.</summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback, object?)> _queue = new();
        private readonly Thread _thread;

        public SingleThreadContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            }) { IsBackground = true };
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void Dispose() => _queue.CompleteAdding();
    }
}
