using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MasteryTracker;

/// <summary>
/// Listens to Supabase Realtime for changes to one client's rows. It carries no data itself: when anything
/// changes it raises <see cref="Changed"/> and the app re-reads the rows, so progress appears within a moment
/// instead of waiting for the next poll. Reconnects on its own.
/// </summary>
public sealed class RealtimeClient : IDisposable
{
    static readonly string[] Tables = { "ticks", "levels", "classes", "custom_weapons" };

    readonly string url;
    readonly string anonKey;
    readonly Func<Task<string?>> tokenProvider;
    readonly CancellationTokenSource cts = new();
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly object gate = new();

    ClientWebSocket? socket;
    Task? loop;
    string? watching;     // client id we want events for
    string? joinedTopic;  // channel currently joined
    int refCounter;
    bool disposed;

    public event Action<string?>? Changed;
    public event Action<bool>? ConnectionChanged;

    public RealtimeClient(string baseUrl, string anonKey, Func<Task<string?>> tokenProvider)
    {
        this.anonKey = anonKey;
        this.tokenProvider = tokenProvider;
        url = baseUrl.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://")
              + "/realtime/v1/websocket?apikey=" + Uri.EscapeDataString(anonKey) + "&vsn=1.0.0";
    }

    /// <summary>Start (or switch) listening for one client's changes. Pass null to stop listening.</summary>
    public void Watch(string? clientId)
    {
        lock (gate) watching = clientId;
        if (clientId == null) return;
        _ = Task.Run(async () =>
        {
            try { if (socket?.State == WebSocketState.Open) await JoinAsync(socket); }
            catch { /* the loop will reconnect and join */ }
        });
        if (loop == null || loop.IsCompleted) loop = Task.Run(RunAsync);
    }

    async Task RunAsync()
    {
        while (!disposed)
        {
            try
            {
                using var sock = new ClientWebSocket();
                await sock.ConnectAsync(new Uri(url), cts.Token);
                socket = sock;
                joinedTopic = null;
                await JoinAsync(sock);
                var beat = HeartbeatAsync(sock);
                await ReadAsync(sock);
            }
            catch { /* fall through to reconnect */ }

            socket = null;
            joinedTopic = null;
            ConnectionChanged?.Invoke(false);
            if (disposed) break;
            try { await Task.Delay(2000, cts.Token); } catch { break; }
        }
    }

    string NextRef() => Interlocked.Increment(ref refCounter).ToString();

    async Task SendAsync(ClientWebSocket sock, object message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await sendLock.WaitAsync(cts.Token);
        try
        {
            if (sock.State == WebSocketState.Open)
                await sock.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
        }
        finally { sendLock.Release(); }
    }

    async Task JoinAsync(ClientWebSocket sock)
    {
        string? id;
        lock (gate) id = watching;
        if (id == null) return;

        var old = joinedTopic;
        if (old != null)
            await SendAsync(sock, new { topic = old, @event = "phx_leave", payload = new { }, @ref = NextRef() });

        var topic = "realtime:tracker-" + id;
        var token = await tokenProvider() ?? anonKey;
        // Supabase cannot filter DELETE events (unticking a camo deletes a row), so deletes are listened for on
        // every table without a filter. They carry no data: the app just re-reads the watched client.
        var changes = new List<object>();
        foreach (var t in Tables)
        {
            changes.Add(new { @event = "*", schema = "public", table = t, filter = "client_id=eq." + id });
            changes.Add(new { @event = "DELETE", schema = "public", table = t });
        }
        // custom_weapons is keyed by its own id, so it filters on client_id too (needs replica identity full for deletes)
        var r = NextRef();
        await SendAsync(sock, new
        {
            topic,
            @event = "phx_join",
            payload = new
            {
                config = new
                {
                    broadcast = new { ack = false, self = false },
                    presence = new { key = "" },
                    postgres_changes = changes.ToArray(),
                },
                access_token = token,
            },
            @ref = r,
            join_ref = r,
        });
        joinedTopic = topic;
    }

    async Task HeartbeatAsync(ClientWebSocket sock)
    {
        int ticks = 0;
        while (sock.State == WebSocketState.Open && !disposed)
        {
            try { await Task.Delay(20000, cts.Token); } catch { return; }
            await SendAsync(sock, new { topic = "phoenix", @event = "heartbeat", payload = new { }, @ref = NextRef() });
            // keep the channel's access token fresh (about every 10 minutes)
            if (++ticks % 30 == 0 && joinedTopic != null)
            {
                var token = await tokenProvider();
                if (token != null)
                    await SendAsync(sock, new { topic = joinedTopic, @event = "access_token", payload = new { access_token = token }, @ref = NextRef() });
            }
        }
    }

    async Task ReadAsync(ClientWebSocket sock)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        while (sock.State == WebSocketState.Open && !disposed)
        {
            var res = await sock.ReceiveAsync(buffer, cts.Token);
            if (res.MessageType == WebSocketMessageType.Close) return;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
            if (!res.EndOfMessage) continue;

            var text = sb.ToString();
            sb.Clear();
            try
            {
                using var d = JsonDocument.Parse(text);
                var root = d.RootElement;
                var ev = root.TryGetProperty("event", out var e) ? e.GetString() : null;
                var topic = root.TryGetProperty("topic", out var t) ? t.GetString() : null;
                if (ev == "postgres_changes")
                {
                    string? table = null;
                    try
                    {
                        if (root.GetProperty("payload").GetProperty("data").TryGetProperty("table", out var tb))
                            table = tb.GetString();
                    }
                    catch { /* unknown shape: refresh everything */ }
                    Changed?.Invoke(table);
                }
                else if (ev == "phx_reply" && topic == joinedTopic && root.TryGetProperty("payload", out var p)
                         && p.TryGetProperty("status", out var s))
                {
                    ConnectionChanged?.Invoke(s.GetString() == "ok");
                    if (s.GetString() == "ok") Changed?.Invoke(null); // catch anything missed while connecting
                }
            }
            catch { /* ignore a malformed frame */ }
        }
    }

    public void Dispose()
    {
        disposed = true;
        try { cts.Cancel(); } catch { }
        try { socket?.Dispose(); } catch { }
    }
}
