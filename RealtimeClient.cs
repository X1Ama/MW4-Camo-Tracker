using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MasteryTracker;

/// <summary>
/// Listens to Supabase Realtime for changes to one client's rows. It carries no data itself: when anything
/// changes it raises <see cref="Changed"/> and the app re-reads the rows, so progress appears within a moment
/// instead of waiting for the next poll. Reconnects on its own.
/// </summary>
/// <summary>The people who currently have the app open.</summary>
public static class Presence
{
    public static HashSet<string> Online { get; private set; } = new();
    public static void Replace(HashSet<string> ids) => Online = ids;
}

/// <summary>What a realtime event said, so the screen can be updated without asking the server again.</summary>
public sealed record RemoteChange(string Table, string Type, string? ClientId, string? CamoId, string? WeaponKey, int? Level, string? Cls);

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

    /// <summary>Raised with the details of a change. Returns true when the app dealt with it directly.</summary>
    public Func<RemoteChange, bool>? Detail { get; set; }

    /// <summary>Raised when somebody opens or closes the app.</summary>
    public event Action? PresenceChanged;

    /// <summary>Raised when an admin changed roles, names or assignments somewhere.</summary>
    public event Action? PeopleChanged;

    /// <summary>Tells every open copy of the app that people or roles changed, so they refresh at once.</summary>
    public void BroadcastPeopleChanged()
    {
        var sock = socket;
        if (sock?.State != WebSocketState.Open || presenceKey == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await SendAsync(sock, new
                {
                    topic = PresenceTopic,
                    @event = "broadcast",
                    payload = new { type = "broadcast", @event = "people", payload = new { from = presenceKey } },
                    @ref = NextRef(),
                });
            }
            catch { /* the 30 second safety refresh still covers it */ }
        });
    }

    const string PresenceTopic = "realtime:online";
    string? presenceKey;
    bool tracked;

    /// <summary>Announces this person as online and listens for everybody else.</summary>
    public void TrackPresence(string userId)
    {
        presenceKey = userId;
        if (loop == null || loop.IsCompleted) loop = Task.Run(RunAsync);
    }

    async Task JoinPresenceAsync(ClientWebSocket sock)
    {
        if (presenceKey == null) return;
        var token = await tokenProvider() ?? anonKey;
        var r = NextRef();
        await SendAsync(sock, new
        {
            topic = PresenceTopic,
            @event = "phx_join",
            payload = new
            {
                config = new
                {
                    broadcast = new { ack = false, self = false },
                    presence = new { key = presenceKey, enabled = true },
                    postgres_changes = Array.Empty<object>(),
                },
                access_token = token,
            },
            @ref = r,
            join_ref = r,
        });
    }

    void ReadPresence(string? ev, JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) return;
        var set = new HashSet<string>(Presence.Online);
        if (ev == "presence_state")
        {
            set.Clear();
            foreach (var k in p.EnumerateObject()) set.Add(k.Name);
        }
        else if (ev == "presence_diff")
        {
            if (p.TryGetProperty("leaves", out var lv) && lv.ValueKind == JsonValueKind.Object)
                foreach (var k in lv.EnumerateObject()) set.Remove(k.Name);
            if (p.TryGetProperty("joins", out var jn) && jn.ValueKind == JsonValueKind.Object)
                foreach (var k in jn.EnumerateObject()) set.Add(k.Name);
        }
        else return;
        Presence.Replace(set);
        PresenceChanged?.Invoke();
    }
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
                tracked = false;
                await JoinPresenceAsync(sock);
                var beat = HeartbeatAsync(sock);
                await ReadAsync(sock);
            }
            catch { /* fall through to reconnect */ }

            socket = null;
            joinedTopic = null;
            ConnectionChanged?.Invoke(false);
            if (Presence.Online.Count > 0) { Presence.Replace(new HashSet<string>()); PresenceChanged?.Invoke(); }
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
                if (topic == PresenceTopic)
                {
                    if (ev == "phx_reply" && root.TryGetProperty("payload", out var pr)
                        && pr.TryGetProperty("status", out var ps) && ps.GetString() == "ok" && presenceKey != null && !tracked)
                    {
                        // joined: announce this person (once per connection)
                        tracked = true;
                        await SendAsync(sock, new
                        {
                            topic = PresenceTopic,
                            @event = "presence",
                            payload = new { type = "presence", @event = "track", payload = new { user = presenceKey, online_at = DateTime.UtcNow.ToString("o") } },
                            @ref = NextRef(),
                        });
                    }
                    else if (ev == "broadcast")
                    {
                        if (root.TryGetProperty("payload", out var bp) && bp.TryGetProperty("event", out var be)
                            && be.GetString() == "people")
                            PeopleChanged?.Invoke();
                    }
                    else ReadPresence(ev, root);
                    continue;
                }
                if (ev == "postgres_changes")
                {
                    string? table = null;
                    RemoteChange? detail = null;
                    try
                    {
                        var data = root.GetProperty("payload").GetProperty("data");
                        if (data.TryGetProperty("table", out var tb)) table = tb.GetString();
                        var type = data.TryGetProperty("type", out var ty) ? ty.GetString() ?? "" : "";
                        string? Pick(string prop)
                        {
                            foreach (var holder in new[] { "record", "old_record" })
                                if (data.TryGetProperty(holder, out var rec) && rec.ValueKind == JsonValueKind.Object
                                    && rec.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
                                    return v.GetString();
                            return null;
                        }
                        int? level = null;
                        if (data.TryGetProperty("record", out var rr) && rr.ValueKind == JsonValueKind.Object
                            && rr.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number)
                            level = lv.GetInt32();
                        if (table != null)
                            detail = new RemoteChange(table, type, Pick("client_id"), Pick("camo_id"), Pick("weapon_key"), level, Pick("cls"));
                    }
                    catch { /* unknown shape: refresh everything */ }
                    if (detail != null && Detail != null)
                    {
                        bool handled;
                        try { handled = Detail(detail); } catch { handled = false; }
                        if (handled) continue;
                    }
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
