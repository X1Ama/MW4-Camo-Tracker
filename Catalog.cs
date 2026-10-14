namespace MasteryTracker;

public sealed record Mode(string Id, string Name, bool Track, string[] Camos);
public sealed record Profile(string Id, string Email, string DisplayName, string Role)
{
    public string Label => Catalog.Cap(string.IsNullOrWhiteSpace(DisplayName) ? Email.Replace(Catalog.UserDomain, "") : DisplayName);
    public bool IsStaff => Role is "admin" or "owner";
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
        ("Han 86", "ar"), ("KG-7 Vulcan", "sn"), ("Rezi 12", "sg"), ("Finn LMG", "lmg"),
        ("Mar-9", "mm"), ("Krait P68", "pi"), ("Combat Knife", "me"),
        ("Pila", ""), ("ISO Nightshade", ""), ("Type 73", ""), ("M4", ""), ("PPSH-41", ""),
        ("Oris 8.6", ""), ("Hyeon Burst", ""), ("Kastov 762", ""), (".50 GS", ""),
        ("Patriot XMR", ""), ("X-58 NYX", ""), ("Signal 50", ""),
    };

    public static readonly Mode[] Modes =
    {
        new("mp",  "Multiplayer", true,  new[] { "Mercurial Drift", "Polyatomic Reforged", "Orion Reforged" }),
        new("wz",  "Warzone",     true,  new[] { "Parallax", "Damascus Reforged", "Empyros" }),
        new("dmz", "DMZ",         true,  new[] { "Chiral", "Ripple Tide", "Helio" }),
        new("pr",  "Prestige",    false, new[] { "Pyroclast", "Pyroclast Verge", "Pyroclast Scatter" }),
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
