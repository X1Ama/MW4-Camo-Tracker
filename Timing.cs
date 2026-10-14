using System.Diagnostics;
using System.IO;
using System.Text;

namespace MasteryTracker;

/// <summary>
/// Records how long each step of signing in takes and writes it to last-signin.log beside the app,
/// so a slow sign-in can be traced to the step responsible. It stores no names, passwords or data.
/// </summary>
public static class Timing
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly List<(string Name, long Ms)> Marks = new();
    static readonly object Gate = new();
    static bool finished = true;

    /// <summary>Starts a new recording; everything is measured from this moment.</summary>
    public static void Start(string firstStep)
    {
        lock (Gate)
        {
            Marks.Clear();
            finished = false;
            Clock.Restart();
            Marks.Add((firstStep, 0));
        }
    }

    public static void Mark(string step)
    {
        lock (Gate)
        {
            if (finished) return;
            Marks.Add((step, Clock.ElapsedMilliseconds));
        }
    }

    /// <summary>Ends the recording and saves it.</summary>
    public static void Finish()
    {
        string text;
        lock (Gate)
        {
            if (finished || Marks.Count == 0) return;
            finished = true;
            var sb = new StringBuilder();
            sb.AppendLine("Sign-in timing (milliseconds since the Sign in button was pressed)");
            long prev = 0;
            foreach (var (name, ms) in Marks)
            {
                sb.AppendLine($"{ms,6} ms   (+{ms - prev,5})   {name}");
                prev = ms;
            }
            text = sb.ToString();
        }
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            File.WriteAllText(Path.Combine(dir, "last-signin.log"), text);
        }
        catch { /* the log is optional */ }
    }
}
