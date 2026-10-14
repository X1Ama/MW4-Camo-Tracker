using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MasteryTracker;

public sealed class ServerConfig
{
    public string Url { get; set; } = "";
    public string AnonKey { get; set; } = "";
    public string LastEmail { get; set; } = "";
    public string UpdateUrl { get; set; } = "";

    static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasteryTracker", "server.json");
    static string ExePath => Path.Combine(AppContext.BaseDirectory, "server.json");

    public bool IsComplete => Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && AnonKey.Length > 20;

    /// <summary>server.json next to the exe is the default shipped to clients; the AppData copy holds what the user typed.</summary>
    public static ServerConfig Load()
    {
        var result = new ServerConfig();
        foreach (var path in new[] { ExePath, AppDataPath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var c = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path));
                if (c == null) continue;
                if (c.Url.Length > 0) result.Url = c.Url;
                if (c.AnonKey.Length > 0) result.AnonKey = c.AnonKey;
                if (c.LastEmail.Length > 0) result.LastEmail = c.LastEmail;
                if (c.UpdateUrl.Length > 0) result.UpdateUrl = c.UpdateUrl;
            }
            catch { /* ignore a damaged file */ }
        }
        return result;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppDataPath)!);
            File.WriteAllText(AppDataPath, JsonSerializer.Serialize(this));
        }
        catch { /* not critical */ }
    }
}

public class ApiException : Exception
{
    public int Status { get; }
    public ApiException(int status, string message) : base(message) { Status = status; }
}

public sealed class ClientData
{
    public HashSet<string> Ticks { get; } = new();
    public Dictionary<string, int> Levels { get; } = new();
    public Dictionary<string, string> Classes { get; } = new();
    public List<CustomWeapon> Customs { get; } = new();

    public string Signature()
    {
        var sb = new StringBuilder();
        foreach (var t in Ticks.OrderBy(x => x)) sb.Append(t).Append(',');
        sb.Append('|');
        foreach (var kv in Levels.OrderBy(x => x.Key)) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(',');
        sb.Append('|');
        foreach (var kv in Classes.OrderBy(x => x.Key)) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(',');
        sb.Append('|');
        foreach (var c in Customs.OrderBy(x => x.Id)) sb.Append(c.Id).Append(':').Append(c.Name).Append(':').Append(c.Cls).Append(',');
        return sb.ToString();
    }
}

/// <summary>Thin Supabase client: email/password sign-in plus PostgREST calls. No webview, plain HTTPS.</summary>
public class Api
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly ServerConfig cfg;
    string? access, refresh;
    DateTime expiresUtc;

    public string UserId { get; private set; } = "";

    public Api(ServerConfig config) { cfg = config; }

    string Base => cfg.Url.TrimEnd('/');

    async Task<string> SendAsync(HttpMethod method, string pathAndQuery, string? json = null, string? prefer = null, bool needAuth = true)
    {
        if (needAuth) await EnsureFreshAsync();
        using var req = new HttpRequestMessage(method, Base + pathAndQuery);
        req.Headers.Add("apikey", cfg.AnonKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", needAuth && access != null ? access : cfg.AnonKey);
        if (prefer != null) req.Headers.Add("Prefer", prefer);
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            string msg = body;
            try
            {
                using var d = JsonDocument.Parse(body);
                foreach (var key in new[] { "error_description", "msg", "message", "error" })
                    if (d.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) { msg = v.GetString()!; break; }
            }
            catch { }
            throw new ApiException((int)res.StatusCode, msg);
        }
        return body;
    }

    async Task EnsureFreshAsync()
    {
        if (refresh == null || DateTime.UtcNow < expiresUtc.AddSeconds(-60)) return;
        var body = await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token",
            JsonSerializer.Serialize(new { refresh_token = refresh }), needAuth: false);
        ReadSession(body);
    }

    void ReadSession(string body)
    {
        using var d = JsonDocument.Parse(body);
        var r = d.RootElement;
        access = r.GetProperty("access_token").GetString();
        refresh = r.GetProperty("refresh_token").GetString();
        expiresUtc = DateTime.UtcNow.AddSeconds(r.GetProperty("expires_in").GetInt32());
        if (r.TryGetProperty("user", out var u)) UserId = u.GetProperty("id").GetString() ?? "";
    }

    public virtual async Task<Profile> SignInAsync(string email, string password)
    {
        var body = await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=password",
            JsonSerializer.Serialize(new { email, password }), needAuth: false);
        ReadSession(body);
        var rows = await GetProfilesAsync($"?id=eq.{UserId}&select=id,email,display_name,role");
        return rows.FirstOrDefault() ?? throw new ApiException(404,
            "This account has no profile row. Run the setup SQL and sign in again.");
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    async Task<List<Profile>> GetProfilesAsync(string query)
    {
        var body = await SendAsync(HttpMethod.Get, "/rest/v1/profiles" + query);
        using var d = JsonDocument.Parse(body);
        return d.RootElement.EnumerateArray()
            .Select(e => new Profile(Str(e, "id"), Str(e, "email"), Str(e, "display_name"), Str(e, "role")))
            .ToList();
    }

    /// <summary>Everyone the signed-in user may see: clients they manage (all of them for an owner).</summary>
    public virtual async Task<List<Profile>> GetManagedClientsAsync() =>
        await GetProfilesAsync("?select=id,email,display_name,role&role=eq.client&order=display_name");

    // ---- admin panel (server functions in supabase-admin.sql) -------------------

    /// <summary>Every profile the caller may see: all of them for an owner, own clients plus self for an admin.</summary>
    public virtual Task<List<Profile>> GetAllProfilesAsync() =>
        GetProfilesAsync("?select=id,email,display_name,role&order=display_name");

    public virtual async Task<List<(string AdminId, string ClientId)>> GetAssignmentsAsync()
    {
        var body = await SendAsync(HttpMethod.Get, "/rest/v1/admin_clients?select=admin_id,client_id");
        using var d = JsonDocument.Parse(body);
        return d.RootElement.EnumerateArray().Select(e => (Str(e, "admin_id"), Str(e, "client_id"))).ToList();
    }

    Task RpcAsync(string fn, object args) =>
        SendAsync(HttpMethod.Post, "/rest/v1/rpc/" + fn, JsonSerializer.Serialize(args));

    /// <summary>The copy of the password kept by the admin panel, or null when none is stored.</summary>
    public virtual async Task<string?> GetPasswordNoteAsync(string userId)
    {
        var body = await SendAsync(HttpMethod.Get, $"/rest/v1/password_notes?user_id=eq.{userId}&select=password");
        using var d = JsonDocument.Parse(body);
        foreach (var e in d.RootElement.EnumerateArray())
        {
            var pw = Str(e, "password");
            return pw.Length > 0 ? pw : null;
        }
        return null;
    }

    public virtual Task CreateUserAsync(string username, string password, string displayName, string role) =>
        RpcAsync("admin_create_user", new { p_username = username, p_password = password, p_display_name = displayName, p_role = role });

    public virtual Task SetPasswordAsync(string userId, string password) =>
        RpcAsync("admin_set_password", new { p_user = userId, p_password = password });

    public virtual Task SetDisplayNameAsync(string userId, string name) =>
        RpcAsync("admin_set_display_name", new { p_user = userId, p_name = name });

    public virtual Task SetRoleAsync(string userId, string role) =>
        RpcAsync("admin_set_role", new { p_user = userId, p_role = role });

    public virtual Task AssignAsync(string adminId, string clientId, bool on) =>
        RpcAsync("admin_assign", new { p_admin = adminId, p_client = clientId, p_on = on });

    public virtual Task DeleteUserAsync(string userId) =>
        RpcAsync("admin_delete_user", new { p_user = userId });

    public virtual async Task LoadAsync(string clientId, ClientData into)
    {
        var f = $"?client_id=eq.{clientId}";
        var tTicks = SendAsync(HttpMethod.Get, "/rest/v1/ticks" + f + "&select=camo_id");
        var tLevels = SendAsync(HttpMethod.Get, "/rest/v1/levels" + f + "&select=weapon_key,level");
        var tClasses = SendAsync(HttpMethod.Get, "/rest/v1/classes" + f + "&select=weapon_key,cls");
        var tCustom = SendAsync(HttpMethod.Get, "/rest/v1/custom_weapons" + f + "&select=id,name,cls&order=created_at");
        await Task.WhenAll(tTicks, tLevels, tClasses, tCustom);

        into.Ticks.Clear(); into.Levels.Clear(); into.Classes.Clear(); into.Customs.Clear();
        using (var d = JsonDocument.Parse(tTicks.Result))
            foreach (var e in d.RootElement.EnumerateArray()) into.Ticks.Add(Str(e, "camo_id"));
        using (var d = JsonDocument.Parse(tLevels.Result))
            foreach (var e in d.RootElement.EnumerateArray())
                into.Levels[Str(e, "weapon_key")] = e.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number ? lv.GetInt32() : 0;
        using (var d = JsonDocument.Parse(tClasses.Result))
            foreach (var e in d.RootElement.EnumerateArray()) into.Classes[Str(e, "weapon_key")] = Str(e, "cls");
        using (var d = JsonDocument.Parse(tCustom.Result))
            foreach (var e in d.RootElement.EnumerateArray())
                into.Customs.Add(new CustomWeapon(Str(e, "id"), Str(e, "name"), Str(e, "cls")));
    }

    const string Merge = "resolution=merge-duplicates,return=minimal";

    public virtual Task SetTickAsync(string clientId, string camoId, bool on) => on
        ? SendAsync(HttpMethod.Post, "/rest/v1/ticks",
            JsonSerializer.Serialize(new[] { new { client_id = clientId, camo_id = camoId } }), Merge)
        : SendAsync(HttpMethod.Delete, $"/rest/v1/ticks?client_id=eq.{clientId}&camo_id=eq.{Uri.EscapeDataString(camoId)}");

    public virtual Task SetLevelAsync(string clientId, string weaponKey, int level) =>
        SendAsync(HttpMethod.Post, "/rest/v1/levels",
            JsonSerializer.Serialize(new[] { new { client_id = clientId, weapon_key = weaponKey, level } }), Merge);

    public virtual Task SetClassAsync(string clientId, string weaponKey, string cls) => cls.Length > 0
        ? SendAsync(HttpMethod.Post, "/rest/v1/classes",
            JsonSerializer.Serialize(new[] { new { client_id = clientId, weapon_key = weaponKey, cls } }), Merge)
        : SendAsync(HttpMethod.Delete, $"/rest/v1/classes?client_id=eq.{clientId}&weapon_key=eq.{Uri.EscapeDataString(weaponKey)}");

    public virtual async Task<CustomWeapon> AddCustomAsync(string clientId, string name, string cls)
    {
        var body = await SendAsync(HttpMethod.Post, "/rest/v1/custom_weapons",
            JsonSerializer.Serialize(new[] { new { client_id = clientId, name, cls } }), "return=representation");
        using var d = JsonDocument.Parse(body);
        var e = d.RootElement[0];
        return new CustomWeapon(Str(e, "id"), Str(e, "name"), Str(e, "cls"));
    }

    public virtual Task RemoveCustomAsync(string id) =>
        SendAsync(HttpMethod.Delete, $"/rest/v1/custom_weapons?id=eq.{id}");
}
