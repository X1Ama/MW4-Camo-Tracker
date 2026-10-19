using System.IO;
using System.Text.Json;

namespace MasteryTracker;

/// <summary>Copies of a client's ticks and weapon levels kept on disk, so an accidental reset can be undone.</summary>
public static class Backups
{
    public sealed record Snapshot(string Path, DateTime Saved, List<string> Ticks, Dictionary<string, int> Levels);

    sealed class FileModel
    {
        public string Client { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime Saved { get; set; }
        public List<string> Ticks { get; set; } = new();
        public Dictionary<string, int> Levels { get; set; } = new();
    }

    // Documents (not AppData) so the files are easy to find and are never hidden by app virtualization
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MW4 Camo Tracker", "Backups");

    static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    public static string Save(string clientId, string clientName, ClientData data, string tag = "save")
    {
        Directory.CreateDirectory(Folder);
        var now = DateTime.Now;
        var model = new FileModel
        {
            Client = clientId, Name = clientName, Saved = now,
            Ticks = data.Ticks.OrderBy(t => t).ToList(),
            Levels = new Dictionary<string, int>(data.Levels),
        };
        var path = Path.Combine(Folder, $"{Safe(clientName)}_{clientId[..Math.Min(8, clientId.Length)]}_{now:yyyy-MM-dd_HH-mm-ss}_{tag}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
        Trim(clientId);
        return path;
    }

    /// <summary>The newest saved copy for this client, or null.</summary>
    public static Snapshot? Latest(string clientId)
    {
        try
        {
            if (!Directory.Exists(Folder)) return null;
            Snapshot? best = null;
            foreach (var f in Directory.EnumerateFiles(Folder, "*.json"))
            {
                try
                {
                    var m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(f));
                    if (m == null || m.Client != clientId) continue;
                    if (best == null || m.Saved > best.Saved) best = new Snapshot(f, m.Saved, m.Ticks, m.Levels);
                }
                catch { /* not one of ours */ }
            }
            return best;
        }
        catch { return null; }
    }

    // keep the newest 30 per client
    static void Trim(string clientId)
    {
        try
        {
            var mine = new List<(string F, DateTime T)>();
            foreach (var f in Directory.EnumerateFiles(Folder, "*.json"))
            {
                try
                {
                    var m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(f));
                    if (m != null && m.Client == clientId) mine.Add((f, m.Saved));
                }
                catch { }
            }
            foreach (var old in mine.OrderByDescending(x => x.T).Skip(30)) File.Delete(old.F);
        }
        catch { }
    }
}
