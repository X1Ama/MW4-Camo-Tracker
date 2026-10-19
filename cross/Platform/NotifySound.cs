using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MasteryTracker;

/// <summary>
/// A short two-note chime for the "client came online" banner, generated in memory and played with whatever the
/// system provides: Windows' own player, afplay on macOS, paplay / pw-play / aplay on Linux.
/// </summary>
public static class NotifySound
{
    static string? file;
    static readonly object Gate = new();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern bool PlaySound(string name, IntPtr module, uint flags);

    public static void Play()
    {
        _ = Task.Run(() =>
        {
            try
            {
                string path;
                lock (Gate)
                {
                    file ??= WriteFile();
                    path = file;
                }
                if (OperatingSystem.IsWindows()) PlaySound(path, IntPtr.Zero, 0x00020000 | 0x0001);   // SND_FILENAME | SND_ASYNC
                else if (OperatingSystem.IsMacOS()) Start("afplay", path);
                else foreach (var player in new[] { "paplay", "pw-play", "aplay" }) if (Start(player, path)) break;
            }
            catch (Exception ex)
            {
                SessionStore.Log("sound could not play: " + ex.Message);
            }
        });
    }

    static bool Start(string program, string arg)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { arg } });
            return p != null;
        }
        catch { return false; }          // that player is not installed: try the next one
    }

    static string WriteFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "mw4-camo-tracker-chime.wav");
        File.WriteAllBytes(path, Build());
        return path;
    }

    static byte[] Build()
    {
        const int rate = 22050;
        var notes = new (double Hz, double Seconds)[] { (880.0, 0.11), (1318.5, 0.2) };
        int total = notes.Sum(n => (int)(n.Seconds * rate));
        var pcm = new short[total];
        int pos = 0;
        foreach (var (hz, seconds) in notes)
        {
            int count = (int)(seconds * rate);
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / rate;
                double attack = Math.Min(1.0, i / (rate * 0.008));
                double decay = Math.Exp(-6.0 * i / count);
                double tone = Math.Sin(2 * Math.PI * hz * t) + 0.25 * Math.Sin(4 * Math.PI * hz * t);
                pcm[pos++] = (short)(tone * attack * decay * 0.32 * short.MaxValue);
            }
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = pcm.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var s in pcm) w.Write(s);
        return ms.ToArray();
    }
}
