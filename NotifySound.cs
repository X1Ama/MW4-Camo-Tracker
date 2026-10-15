using System.IO;
using System.Media;

namespace MasteryTracker;

/// <summary>A short two-note chime for the "client came online" banner, generated in memory (no sound file to ship).</summary>
public static class NotifySound
{
    static SoundPlayer? player;
    static readonly object Gate = new();

    public static void Play()
    {
        _ = Task.Run(() =>
        {
            try
            {
                lock (Gate)
                {
                    player ??= new SoundPlayer(new MemoryStream(Build()));
                    player.PlaySync();
                }
            }
            catch (Exception ex)
            {
                SessionStore.Log("sound could not play: " + ex.Message);   // no sound device or sound muted at the system level
            }
        });
    }

    static byte[] Build()
    {
        const int rate = 22050;
        // a soft rising pair of notes, each with a quick fade so it does not click
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
