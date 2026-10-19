namespace MasteryTracker;

/// <summary>
/// Offline stand-in for the server, used only when the app is started with --demo.
/// Everything lives in memory and is lost on exit. It lets you look at the app without a Supabase project.
/// </summary>
public sealed class DemoApi : Api
{
    public static readonly Profile Client = new("u-hoodie", "hoodie" + Catalog.UserDomain, "Hoodie", "client");
    public static readonly Profile Owner = new("u-owner", "demo-owner" + Catalog.UserDomain, "Demo Owner", "owner");

    readonly List<Profile> people = new()
    {
        Owner,
        new("u-marcus", "marcus" + Catalog.UserDomain, "Marcus", "admin"),
        new("u-hoodie", "hoodie" + Catalog.UserDomain, "Hoodie", "client"),
        new("u-konsti", "konsti" + Catalog.UserDomain, "Konsti", "client"),
        new("u-anna", "anna" + Catalog.UserDomain, "Anna", "client"),
    };

    readonly List<(string AdminId, string ClientId)> links = new() { ("u-marcus", "u-hoodie"), ("u-marcus", "u-anna") };
    readonly Dictionary<string, ClientData> store = new();

    public DemoApi() : base(new ServerConfig())
    {
        var hoodie = Data("u-hoodie");
        foreach (var id in new[] { "track-han-86", "mp-han-86-mercurial-drift", "mp-han-86-polyatomic-reforged", "mp-han-86-orion-reforged" })
            hoodie.Ticks.Add(id);
        hoodie.Levels["han-86"] = 34;
        hoodie.Levels["rezi-12"] = 12;
        Data("u-konsti").Ticks.Add("track-finn-lmg");
        Data("u-konsti").Levels["finn-lmg"] = 7;
    }

    public override bool SupportsRealtime => false;

    public override Task ChangePasswordAsync(string email, string currentPassword, string newPassword)
    {
        if (currentPassword == newPassword) throw new ApiException(400, "same password");
        return Task.CompletedTask; // the demo accepts any current password and saves nothing
    }

    ClientData Data(string id) => store.TryGetValue(id, out var d) ? d : store[id] = new ClientData();

    public override Task<List<Profile>> GetManagedClientsAsync() =>
        Task.FromResult(people.Where(p => p.Role == "client").OrderBy(p => p.Label).ToList());

    public override Task<List<Profile>> GetAllProfilesAsync() => Task.FromResult(people.ToList());

    public override Task<List<(string AdminId, string ClientId)>> GetAssignmentsAsync() => Task.FromResult(links.ToList());

    public override Task LoadAsync(string clientId, ClientData into, string? only = null)
    {
        var d = Data(clientId);
        into.Ticks.Clear(); into.Levels.Clear(); into.Classes.Clear(); into.Customs.Clear();
        foreach (var t in d.Ticks) into.Ticks.Add(t);
        foreach (var kv in d.Levels) into.Levels[kv.Key] = kv.Value;
        foreach (var kv in d.Classes) into.Classes[kv.Key] = kv.Value;
        into.Customs.AddRange(d.Customs);
        return Task.CompletedTask;
    }

    public override Task SetTickAsync(string clientId, string camoId, bool on)
    {
        if (on) Data(clientId).Ticks.Add(camoId); else Data(clientId).Ticks.Remove(camoId);
        return Task.CompletedTask;
    }

    public override Task ResetTicksAsync(string clientId, IReadOnlyCollection<string> camoIds)
    {
        foreach (var id in camoIds) Data(clientId).Ticks.Remove(id);
        return Task.CompletedTask;
    }

    public override Task ResetLevelsAsync(string clientId)
    {
        Data(clientId).Levels.Clear();
        return Task.CompletedTask;
    }

    public override Task RestoreAsync(string clientId, IReadOnlyCollection<string> camoIds, IReadOnlyDictionary<string, int> levels)
    {
        foreach (var id in camoIds) Data(clientId).Ticks.Add(id);
        foreach (var kv in levels) Data(clientId).Levels[kv.Key] = kv.Value;
        return Task.CompletedTask;
    }

    public override Task SetLevelAsync(string clientId, string weaponKey, int level)
    {
        Data(clientId).Levels[weaponKey] = level;
        return Task.CompletedTask;
    }

    public override Task SetClassAsync(string clientId, string weaponKey, string cls)
    {
        if (cls.Length > 0) Data(clientId).Classes[weaponKey] = cls; else Data(clientId).Classes.Remove(weaponKey);
        return Task.CompletedTask;
    }

    public override Task<CustomWeapon> AddCustomAsync(string clientId, string name, string cls)
    {
        var c = new CustomWeapon(Guid.NewGuid().ToString(), name, cls);
        Data(clientId).Customs.Add(c);
        return Task.FromResult(c);
    }

    public override Task RemoveCustomAsync(string id)
    {
        foreach (var d in store.Values) d.Customs.RemoveAll(c => c.Id == id);
        return Task.CompletedTask;
    }

    public override Task CreateUserAsync(string username, string password, string displayName, string role)
    {
        if (people.Any(p => p.Email.Equals(username + Catalog.UserDomain, StringComparison.OrdinalIgnoreCase)))
            throw new ApiException(400, "That user name is already taken.");
        if (password.Length < 6) throw new ApiException(400, "Password must be at least 6 characters.");
        people.Add(new Profile("u-" + Guid.NewGuid().ToString("N")[..8], username.ToLowerInvariant() + Catalog.UserDomain, displayName, role));
        return Task.CompletedTask;
    }

    public override Task SetPasswordAsync(string userId, string password) => Task.CompletedTask;

    public override Task SetDisplayNameAsync(string userId, string name)
    {
        var i = people.FindIndex(p => p.Id == userId);
        if (i >= 0) people[i] = people[i] with { DisplayName = name };
        return Task.CompletedTask;
    }

    public override Task SetRoleAsync(string userId, string role)
    {
        var i = people.FindIndex(p => p.Id == userId);
        if (i >= 0) people[i] = people[i] with { Role = role };
        return Task.CompletedTask;
    }

    public override Task AssignAsync(string adminId, string clientId, bool on)
    {
        links.RemoveAll(l => l.AdminId == adminId && l.ClientId == clientId);
        if (on) links.Add((adminId, clientId));
        return Task.CompletedTask;
    }

    public override Task DeleteUserAsync(string userId)
    {
        people.RemoveAll(p => p.Id == userId);
        links.RemoveAll(l => l.AdminId == userId || l.ClientId == userId);
        store.Remove(userId);
        return Task.CompletedTask;
    }
}
