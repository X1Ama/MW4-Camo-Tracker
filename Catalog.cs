namespace MasteryTracker;

public sealed record Mode(string Id, string Name, bool Track, string[] Camos);
public sealed record Profile(string Id, string Email, string DisplayName, string Role)
{
    public string Label => Catalog.Cap(string.IsNullOrWhiteSpace(DisplayName) ? Email.Replace(Catalog.UserDomain, "") : DisplayName);
    public bool IsStaff => Role is "admin" or "owner";
    /// <summary>True while that person has the app open (kept up to date by the realtime presence channel).</summary>
    public bool IsOnline => Presence.Online.Contains(Id);
    /// <summary>
    /// The user name as it should be written. User names are stored in lower case, so when the display name is the
    /// same word (ignoring case), its capitals are used; otherwise just the first letter is raised.
    /// </summary>
    public string UserName
    {
        get
        {
            var u = Email.Replace(Catalog.UserDomain, "");
            return string.Equals(u, DisplayName, StringComparison.OrdinalIgnoreCase) ? DisplayName : Catalog.Cap(u);
        }
    }
    public string RoleLabel => Catalog.RoleLabel(Role);
}
public sealed record CustomWeapon(string Id, string Name, string Cls);

/// <summary>Static weapon and camo data. Pre-launch names come from community trackers; edit here once MW4 is out.</summary>
public static class Catalog
{
    public const int MaxLevel = 50;

    // The server needs an email per account. Users type a plain user name; this suffix is added behind the scenes.
    public const string UserDomain = "@tracker.local";

    public static readonly (string Code, string Name)[] Classes =
    {
        ("ar", "Assault rifles"), ("smg", "SMGs"), ("sg", "Shotguns"), ("lmg", "LMGs"),
        ("mm", "Marksman rifles"), ("sn", "Sniper rifles"), ("pi", "Pistols"), ("la", "Launchers"), ("me", "Melee"),
    };

    /// <summary>Names shown on screen. The stored role values stay client, admin and owner.</summary>
    public static string RoleLabel(string role) => role switch
    {
        "admin" => "Grinder",
        "owner" => "Owner",
        "client" => "Client",
        _ => role.Length == 0 ? role : char.ToUpperInvariant(role[0]) + role[1..],
    };

    /// <summary>First letter upper case, the rest left as typed ("konsti" becomes "Konsti").</summary>
    public static string Cap(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string ClassName(string code) =>
        Classes.FirstOrDefault(c => c.Code == code).Name ?? "Unsorted";

    // Empty class means the type is not known yet; staff can set it in the app.
    public static readonly (string Name, string Cls)[] Weapons =
    {
        // assault rifles
        ("Hyeon Burst", "ar"), ("Kastov 762", "ar"), ("Axion", "ar"), ("Han 86", "ar"),
        ("M4", "ar"), ("Patriot XMR", "ar"), ("Cronen Squall", "ar"), ("Oden", "ar"),
        // SMGs
        ("Vel 46", "smg"), ("PPSH-41", "smg"), ("X-58 NYX", "smg"), ("WZ.55 Striga", "smg"), ("ISO Nightshade", "smg"),
        // shotguns
        ("Rezi 12", "sg"), ("Warlord", "sg"),
        // LMGs
        ("APOC-338", "lmg"), ("Finn LMG", "lmg"), ("Type 73", "lmg"),
        // marksman rifles
        ("Mosin-Nagant", "mm"), ("Mar-9", "mm"), ("Oris 8.6", "mm"),
        // sniper rifles
        ("Astaroth .57", "sn"), ("Signal 50", "sn"), ("KG-7 Vulcan", "sn"),
        // pistols
        (".50 GS", "pi"), ("Sang 9mm", "pi"), ("Mojave .357", "pi"), ("Krait P68", "pi"),
        // launchers
        ("Pila", "la"), ("RPG-8", "la"), ("JOKR", "la"), ("PM-40 Anvil", "la"),
        // melee
        ("Combat Knife", "me"), ("FSS Carbon Edge 1095", "me"),
    };
    /// <summary>The DMZ completionist camos, shown as their own group on the DMZ tab.</summary>
    public static readonly string[] DmzCompletionist = { "Gold", "Platinum", "Harmonic Break", "Divergence" };

    /// <summary>
    /// The DMZ special camos differ per gun (keyed by the weapon's slug, see Slug). Shown as a group above the
    /// completionist camos on the DMZ tab. A gun that is not listed has no special camos yet.
    /// </summary>
    static readonly Dictionary<string, string[]> DmzSpecialByGun = new()
    {
        ["han-86"] = new[] { "Smoke", "Stormwave", "Red" },
        ["kastov-762"] = new[] { "Samnim", "Raster Aux", "Yellow" },
    };

    /// <summary>What it takes to unlock a camo, shown when hovering its tile. Null when we do not know it.</summary>
    public static string? Challenge(string camo, string cls, string gunName) => camo switch
    {
        "Mercurial Drift" => "Get 10 Kills in a match 250 times",
        "Polyatomic Reforged" => "Get 3 Kills without taking Damage 300 times",
        "Orion Reforged" => "Get 5 Kills without dying 500 times",
        "Chiral" => "Get 2500 Tier 3+ enemy kills",
        "Ripple Tide" => "Exfil 2 LT dog tags in a single deployment 50 times",
        "Helio" => "Get 25000 Kills using a Legendary Weapon on a Max Level Operator",
        "Gold" => "Get 100 Enemy Kills in a single match in DMZ",
        "Platinum" => "Get 100 Headshots against Enemies",
        "Harmonic Break" => "Get 50 Double Kills against Enemies",
        "Divergence" => "Complete 33 Harmonic Break Camo Challenges",
        "Smoke" => "Get 75 Kills while Mounted against Enemies",
        "Stormwave" => "Get 125 Enemy Kills while using a silencer",
        "Red" => "Get 3 Enemy Kills without taking damage 50 times",
        "Samnim" => "Get 5 Enemy Kills without taking damage 25 times",
        "Raster Aux" => "Get 75 Enemy Longshot Kills",
        "Yellow" => "Get 100 Enemy Hipfire Kills",
        _ => null,
    };
    public static string[] DmzSpecialFor(string weaponKey) =>
        DmzSpecialByGun.TryGetValue(weaponKey, out var list) ? list : Array.Empty<string>();

    public static readonly Mode[] Modes =
    {
        new("mp",  "Multiplayer", true,  new[] { "Mercurial Drift", "Polyatomic Reforged", "Orion Reforged" }),
        new("wz",  "Warzone",     true,  new[] { "Parallax", "Damascus Reforged", "Empyros" }),
        new("dmz", "DMZ",         true,  new[] { "Chiral", "Ripple Tide", "Helio" }),
        new("pr",  "Prestige",    false, new[] { "Pyroclast", "Pyroclast Verge", "Pyroclast Scatter" }),
        // the mode mastery camos of Multiplayer and DMZ together (ticks are shared with those tabs)
        new("mm",  "Mode Mastery", false, Array.Empty<string>()),
    };

    public static string Slug(string s)
    {
        var sb = new System.Text.StringBuilder();
        bool dash = false;
        foreach (var ch in s.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) { sb.Append(ch); dash = false; }
            else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
        }
        return sb.ToString().Trim('-');
    }
}
