using System.IO;
using System.Text.Json;

namespace MasteryTracker;

/// <summary>The order a list of people is shown in, chosen by dragging names. One small file per list, kept on this PC only.</summary>
public static class PeopleOrder
{
    static string FilePath(string name) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasteryCamoTracker", name + ".json");

    public static List<string> Load(string name = "people-order")
    {
        try { return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath(name))) ?? new(); }
        catch { return new(); }
    }

    public static void Save(IEnumerable<string> ids, string name = "people-order")
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath(name))!);
            File.WriteAllText(FilePath(name), JsonSerializer.Serialize(ids));
        }
        catch { /* the order is a convenience: losing it is not an error */ }
    }
}