using System.IO;
using System.Text.Json;

namespace MasteryTracker;

/// <summary>The order the admin panel's people list is shown in, chosen by dragging names. Kept on this PC only.</summary>
public static class PeopleOrder
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasteryCamoTracker", "people-order.json");

    public static List<string> Load()
    {
        try { return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public static void Save(IEnumerable<string> ids)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(ids));
        }
        catch { /* the order is a convenience: losing it is not an error */ }
    }
}