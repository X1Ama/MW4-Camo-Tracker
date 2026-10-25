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

    /// <summary>The top weapon level differs per gun (keyed by the weapon's slug). Guns not listed use MaxLevel.</summary>
    static readonly Dictionary<string, int> MaxLevelByGun = new()
    {
        ["han-86"] = 71, ["m4"] = 71, ["hyeon-burst"] = 67, ["kastov-762"] = 67,
        ["patriot-xmr"] = 71, ["oden"] = 64,
        ["sang-9mm"] = 37, ["krait-p68"] = 37, ["50-gs"] = 36, ["mojave-357"] = 37,   // Mojave .357: placeholder (still locked)
        ["pila"] = 46, ["rpg-8"] = 46, ["jokr"] = 46, ["pm-40-anvil"] = 46,          // JOKR, PM-40 Anvil: placeholders (still locked)
        ["rezi-12"] = 59, ["warlord"] = 59,
        ["finn-lmg"] = 58, ["type-73"] = 64, ["apoc-338"] = 55,
        ["mar-9"] = 72, ["oris-8-6"] = 69, ["mosin-nagant"] = 68,
        ["kg-7-vulcan"] = 74, ["astaroth-57"] = 53, ["signal-50"] = 60,         // Signal 50: placeholder (still locked)
        ["iso-nightshade"] = 63, ["vel-46"] = 59, ["wz-55-striga"] = 59, ["ppsh-41"] = 61,
        ["axion"] = 71, ["cronen-squall"] = 71, ["x-58-nyx"] = 52,
    };

    public static int MaxLevelFor(string weaponKey) =>
        MaxLevelByGun.TryGetValue(weaponKey, out var m) ? m : MaxLevel;

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
        ("Han 86", "ar"), ("M4", "ar"), ("Hyeon Burst", "ar"), ("Kastov 762", "ar"),
        ("Patriot XMR", "ar"), ("Oden", "ar"), ("Axion", "ar"), ("Cronen Squall", "ar"),
        // SMGs
        ("ISO Nightshade", "smg"), ("Vel 46", "smg"), ("WZ.55 Striga", "smg"), ("PPSH-41", "smg"), ("X-58 NYX", "smg"),
        // shotguns
        ("Rezi 12", "sg"), ("Warlord", "sg"),
        // LMGs
        ("Finn LMG", "lmg"), ("Type 73", "lmg"), ("APOC-338", "lmg"),
        // marksman rifles
        ("Mar-9", "mm"), ("Oris 8.6", "mm"), ("Mosin-Nagant", "mm"),
        // sniper rifles
        ("KG-7 Vulcan", "sn"), ("Astaroth .57", "sn"), ("Signal 50", "sn"),
        // pistols
        ("Sang 9mm", "pi"), ("Krait P68", "pi"), (".50 GS", "pi"), ("Mojave .357", "pi"),
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
        ["hyeon-burst"] = new[] { "Ponderosa", "Red Rain", "Kitty Kaos" },
        ["axion"] = new[] { "Splinter Rock", "Sunset Tropic", "Cyan" },
        ["m4"] = new[] { "Myriad", "Night Ops", "Green" },
        ["cronen-squall"] = new[] { "Oni", "Urban Smoke", "Optical Reverb" },
        ["patriot-xmr"] = new[] { "Dune Sea", "Scorch", "Disperse" },
        ["oden"] = new[] { "Dark Moss", "Shale", "Hot Rod" },
        ["vel-46"] = new[] { "Jungle Shadow", "Ghast", "Gunmetal" },
        ["x-58-nyx"] = new[] { "Sniper Veil", "Carbonated", "Lime" },
        ["ppsh-41"] = new[] { "Blackwell", "Iron", "Currents" },
        ["iso-nightshade"] = new[] { "Asphalt", "Black Topo", "Brown" },
        ["wz-55-striga"] = new[] { "Stellation", "Rattlesnake", "Dark Tan" },
        ["rezi-12"] = new[] { "Struck Oil", "Brushstroke", "White Pine" },
        ["warlord"] = new[] { "Redshift", "Corrupted Clay", "Magma Ire" },
        ["apoc-338"] = new[] { "Phantom Flux", "Mineral", "Tiger Swipe" },
        ["finn-lmg"] = new[] { "Desert Tactics", "Green Tiger", "Thunderhead" },
        ["type-73"] = new[] { "Ash Pine", "Outcrop", "Foxtrail" },
        ["mar-9"] = new[] { "Mangrove", "Fungal Grid", "White" },
        ["mosin-nagant"] = new[] { "Ice Sheer", "Fractal Snow", "Sticker Shock" },
        ["oris-8-6"] = new[] { "Hibiscus", "Fire Topo", "Deep Space" },
        ["astaroth-57"] = new[] { "Dry Brush", "Backcountry", "Dread Plague" },
        ["signal-50"] = new[] { "Anaconda", "Ripplewave", "Sunburn" },
        ["kg-7-vulcan"] = new[] { "Treeline", "Spidersilk", "Tension" },
        ["50-gs"] = new[] { "Frosted", "Splatter Ops", "Red Tiger" },
        ["sang-9mm"] = new[] { "Autumn", "Snakeskin", "Space Time" },
        ["mojave-357"] = new[] { "Miami", "Wallpaper", "Garland" },
        ["krait-p68"] = new[] { "Off Grid", "Urban Flamingo", "Smokescreen" },
    };

    // What each camo asks for in Multiplayer and in DMZ, where it differs per gun (from the CODMunity tracker).
    // key: weapon slug + "|" + camo slug.
    static readonly Dictionary<string, (string Mp, string Dmz)> GunTexts = BuildGunTexts();

    static Dictionary<string, (string Mp, string Dmz)> BuildGunTexts()
    {
        var d = new Dictionary<string, (string Mp, string Dmz)>();
        void T(string gun, string camo, string mp, string dmz) => d[gun + "|" + Slug(camo)] = (mp, dmz);

        T("kg-7-vulcan", "Treeline", "Get 10 Hipfire Kills against Players", "Get 0 Kills without taking Damage 25 times");
        T("kg-7-vulcan", "Spidersilk", "Get 20 Headshot Kills against Players", "Get 75 Kills while Focused");
        T("kg-7-vulcan", "Tension", "Get 2 Player Kills without taking damage 20 times", "Get 2 Enemy Kills without taking damage 30 times");
        T("kg-7-vulcan", "Platinum", "Get 75 Player Kills while Prone", "Get 150 Enemy Kills while Prone");
        T("kg-7-vulcan", "Harmonic Break", "Get 75 One Shot Kills against Players", "Get 150 One Shot Kills against Enemies");

        T("rezi-12", "Struck Oil", "Get 20 Point Blank Kills against Players", "Get 75 Point Blank Kills against Enemies");
        T("rezi-12", "Brushstroke", "Get 10 Longshot Kills against Players", "Get 50 Longshot Kills against Enemies");
        T("rezi-12", "White Pine", "Get 40 Player Kills while Jumping", "Get 100 Enemy Kills while Jumping");
        T("rezi-12", "Platinum", "Get 50 Hipfire Kills against Players", "Get 125 Hipfire Kills against Enemies");
        T("rezi-12", "Harmonic Break", "Get 2 Player Kills without Reloading 25 times", "Get 2 Enemy Kills without Reloading 50 times");

        T("iso-nightshade", "Asphalt", "Get 50 Player Kills while Aiming Down Sights", "Get 125 Kills while Aiming Down Sights");
        T("iso-nightshade", "Brown", "Get 3 Player Kills without dying 15 times", "Get 3 Enemy Kills without taking damage 50 times");
        T("iso-nightshade", "Black Topo", "Get 10 Double Kills against Players", "Get 30 Double Kills against Enemies");
        T("iso-nightshade", "Platinum", "Get 25 Player Kills from Behind", "Get 75 Enemy Kills from Behind");
        T("iso-nightshade", "Harmonic Break", "Get 50 Hipfire Kills against Players", "Get 100 Hipfire Kills against Enemies");

        T("finn-lmg", "Desert Tactics", "Get 20 Point Blank Kills against Players", "Get 75 Point Blank Kills against Enemies");
        T("finn-lmg", "Green Tiger", "Get 2 Kills without Reloading 10 times against Players", "Get 2 Kills without Reloading 30 times against Enemies");
        T("finn-lmg", "Thunderhead", "Get 30 Longshot Kills against Players", "Get 100 Longshot Kills against Enemies");
        T("finn-lmg", "Platinum", "Get 3 Player Kills without dying 15 times", "Get 3 Enemy Kills without taking damage 50 times");
        T("finn-lmg", "Harmonic Break", "Get 2 Player Kills without releasing the trigger 25 times", "Get 2 Enemy Kills without releasing the trigger 75 times");

        T("mar-9", "Mangrove", "Get 20 Mounted Kills against Players", "Get 75 Mounted Kills against Enemies");
        T("mar-9", "Fungal Grid", "Get 25 Player Kills while Crouched", "Get 75 Enemy Kills while Crouched");
        T("mar-9", "White", "Get 30 Longshot Kills against Players", "Get 100 Longshot Kills against Enemies");

        T("sang-9mm", "Autumn", "Get 25 Player Kills while Aiming Down Sights", "Get 75 Enemy Kills while Aiming Down Sights");
        T("sang-9mm", "Snakeskin", "Get 3 Player Kills without dying 5 times", "Get 3 Enemy Kills without taking damage 15 times");
        T("sang-9mm", "Space Time", "Get 50 Player Kills while using Akimbo", "Get 125 Enemy Kills while using Akimbo");
        T("sang-9mm", "Platinum", "Get 20 Player Kills from Behind", "Get 75 Enemy Kills from Behind");
        T("sang-9mm", "Harmonic Break", "Get 40 Hipfire Kills against Players", "Get 100 Hipfire Kills against Enemies");

        T("astaroth-57", "Backcountry", "Get 20 Mounted Kills against Players", "Get 75 Mounted Kills against Enemies");
        return d;
    }

    /// <summary>What it takes to unlock a camo in this tab ("mp" Multiplayer, "dmz" DMZ), shown when hovering its tile.</summary>
    public static string? Challenge(string camo, string cls, string gunName, string modeId = "dmz")
    {
        var gun = Slug(gunName);
        if (GunTexts.TryGetValue(gun + "|" + Slug(camo), out var t)) return modeId == "mp" ? t.Mp : t.Dmz;
        if (modeId == "mp")
        {
            if (camo == "Gold") return "Get 15 Player Kills in a single match";
            if (DmzCompletionist.Contains(camo) || DmzSpecialFor(gun).Contains(camo)) return "Multiplayer requirement not added yet";
        }
        return ChallengeBase(camo, cls, gunName);
    }

    static string? ChallengeBase(string camo, string cls, string gunName) => camo switch
    {
        "Mercurial Drift" => "Get 10 Kills in a match 250 times",
        "Polyatomic Reforged" => "Get 3 Kills without taking Damage 300 times",
        "Orion Reforged" => "Get 5 Kills without dying 500 times",
        "Chiral" => "Get 2500 Tier 3+ enemy kills",
        "Ripple Tide" => "Exfil 2 LT dog tags in a single deployment 50 times",
        "Helio" => "Get 25000 Kills using a Legendary Weapon on a Max Level Operator",
        "Pyroclast" => "Prestige the " + gunName,
        "Pyroclast Terminus" => "Prestige the " + gunName + " and reach Prestige Master Level 69",
        "Pyroclast Scatter" => "Prestige the " + gunName + " and reach Prestige Master Level 150",
        "Pyroclast Cascade" => "Prestige the " + gunName + " and reach Prestige Master Level 250",
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
        "Ponderosa" => "Get 2 Kills without Reloading 30 times against Enemies",
        "Red Rain" => "Get 75 Longshot Kills against Enemies",
        "Kitty Kaos" => "Get 250 Enemy Kills while using a silencer",
        "Splinter Rock" => "Get 75 Longshot Kills against Enemies",
        "Sunset Tropic" => "Get 75 Enemy Kills with Miri 40MM Grenade Launcher",
        "Cyan" => "Get 2 Kills without Reloading 50 times against Enemies",
        "Myriad" => "Get 75 Point Blank Kills against Enemies",
        "Night Ops" => "Get 75 Mounted Kills against Enemies",
        "Green" => "Get 100 Kills from behind Enemies",
        "Oni" => "Get 75 Point Blank Kills against Enemies",
        "Urban Smoke" => "Get 75 Mounted Kills against Enemies",
        "Optical Reverb" => "Get 50 Kills while ADS against Enemies",
        "Dune Sea" => "Get 75 Hipfire Kills against Enemies",
        "Scorch" => "Get 125 Enemy Kills using a Silencer",
        "Disperse" => "Get 100 Longshot Kills against Enemies",
        "Dark Moss" => "Get 75 Enemy Kills with Miri 40MM Grenade Launcher",
        "Shale" => "Get 2 Kills without Reloading 30 times against Enemies",
        "Hot Rod" => "Get 250 Kills while ADS against Enemies",
        "Jungle Shadow" => "Get 125 Enemy Kills while Aiming Down Sight",
        "Ghast" => "Get 100 Enemy Kills while Prone",
        "Gunmetal" => "Get 75 Headshots against Enemies",
        "Sniper Veil" => "Get 75 Kills while Moving",
        "Carbonated" => "Get 75 Longshot Kills against Enemies",
        "Lime" => "Get 100 Longshot Kills against Enemies",
        "Blackwell" => "Get 30 Double Kills against Players",
        "Iron" => "Get 75 Longshot Kills",
        "Currents" => "Get 250 Kills while Aiming Down Sights",
        "Asphalt" => "Get 125 Kills while Aiming Down Sights",
        "Black Topo" => "Get 30 Double Kills Against Enemies",
        "Brown" => "Get 3 Enemy Kills without taking damage 50 times",
        "Stellation" => "Get 125 Enemy Kills using a Silencer",
        "Rattlesnake" => "Get 50 Enemy Kills while Jumping",
        "Dark Tan" => "Get 100 Point Blank Kills",
        "Struck Oil" => "Get 75 Point Blank Kills against Enemies",
        "Brushstroke" => "Get 50 Longshot Kills against Enemies",
        "White Pine" => "Get 100 Enemy Kills while Jumping",
        "Redshift" => "Get 75 Enemy Kills while moving",
        "Corrupted Clay" => "Get 30 Double Kills against Enemies",
        "Magma Ire" => "Get 125 Enemy Kills while Prone",
        "Phantom Flux" => "Get 75 Hipfire Kills against Enemies",
        "Mineral" => "Get 75 Enemy Longshot Kills",
        "Tiger Swipe" => "Get 250 Enemy Kills while Prone",
        "Desert Tactics" => "Get 75 Point Blank Kills against Enemies",
        "Green Tiger" => "Get 2 Kills without Reloading 30 times against Enemies",
        "Thunderhead" => "Get 100 Longshot Kills against Enemies",
        "Ash Pine" => "Get 75 Mounted Kills against Enemies",
        "Outcrop" => "Get 30 Double Kills against Enemies",
        "Foxtrail" => "Get 100 Headshots against Enemies",
        "Mangrove" => "Get 75 Mounted Kills against Enemies",
        "Fungal Grid" => "Get 75 Enemy Kills while Crouched",
        "White" => "Get 100 Longshot Kills against Enemies",
        "Ice Sheer" => "Get 75 Headshot Kills against Enemies",
        "Fractal Snow" => "Get 2 Enemy Kills without Reloading 30 times",
        "Sticker Shock" => "Get 250 Enemy Kills while Focused",
        "Hibiscus" => "Get 50 Quickscope Kills against Enemies",
        "Fire Topo" => "Get 50 Double Kills against Enemies",
        "Deep Space" => "Get 3 Enemy Kills without taking damage 50 times",
        "Dry Brush" => "Get 3 Enemy Kills without taking Damage 25 times",
        "Backcountry" => "Get 75 Mounted Kills against Enemies",
        "Dread Plague" => "Get 2 Enemy Kills within 5 Seconds 50 times",
        "Anaconda" => "Get 2 Kills within 5 Seconds 25 times",
        "Ripplewave" => "Get 30 Quickscope Kills against Enemies",
        "Sunburn" => "Get 100 One Shot Kills",
        "Treeline" => "Get 0 Kills without taking Damage 25 times",      // as shown in the game screenshot (the number looks wrong there)
        "Spidersilk" => "Get 75 Kills while Focused",
        "Tension" => "Get 2 Enemy Kills without taking damage 30 times",
        "Frosted" => "Get 75 Headshots Kills against Enemies",
        "Splatter Ops" => "Get 20 Double Kills against Enemies",
        "Red Tiger" => "Get 75 Point Blank Kills against Enemies",
        "Autumn" => "Get 75 Enemy Kills while Aiming Down Sight",
        "Snakeskin" => "Get 3 Enemy Kills without taking damage 15 times",
        "Space Time" => "Get 125 Enemy Kills while using Akimbo",
        "Miami" => "Get 50 One Shot Kills against Enemies",
        "Wallpaper" => "Get 2 Enemy Kills without Reloading 20 times",
        "Garland" => "Get 75 Point Blank Kills against Enemies",
        "Off Grid" => "Get 50 Enemy Kills while Ledge Hanging",
        "Urban Flamingo" => "Get 50 Point Blank Kills against Enemies",
        "Smokescreen" => "Get 125 Enemy Kills while using a Silencer",
        _ => null,
    };
    public static string[] DmzSpecialFor(string weaponKey) =>
        DmzSpecialByGun.TryGetValue(weaponKey, out var list) ? list : Array.Empty<string>();

    /// <summary>The tabs along the top: the old Multiplayer tab is hidden (its camos still feed Mode Mastery).</summary>
    public static IEnumerable<Mode> Tabs => Modes;

    public static readonly Mode[] Modes =
    {
        new("mp",  "Multiplayer", true,  new[] { "Mercurial Drift", "Polyatomic Reforged", "Orion Reforged" }),
        new("wz",  "Warzone",     true,  new[] { "Parallax", "Damascus Reforged", "Empyros" }),
        new("dmz", "DMZ",         true,  new[] { "Chiral", "Ripple Tide", "Helio" }),
        new("pr",  "Prestige",    false, new[] { "Pyroclast", "Pyroclast Terminus", "Pyroclast Scatter", "Pyroclast Cascade" }),
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
