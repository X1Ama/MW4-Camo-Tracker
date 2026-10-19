namespace MasteryTracker;

/// <summary>
/// Things only the phone shell knows how to do (play a sound, hand an update to Android's installer). The Android
/// app fills these in when it starts; on desktop they stay empty.
/// </summary>
public static class PlatformHooks
{
    /// <summary>Plays the "client came online" chime.</summary>
    public static Action? PlayChime { get; set; }

    /// <summary>Opens Android's package installer for a downloaded update (.apk file path).</summary>
    public static Action<string>? InstallPackage { get; set; }
}
