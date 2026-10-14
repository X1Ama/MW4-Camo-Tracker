using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MasteryTracker;

public sealed record UpdateInfo(Version Version, string Url, string Notes, bool Required, IReadOnlyList<string> Highlights);

/// <summary>
/// Checks a small update.json file (hosted anywhere over HTTPS, for example a GitHub Release asset),
/// downloads the new exe and swaps it in. The manifest looks like:
/// { "version": "0.2.0", "url": "https://.../MasteryCamoTracker.exe", "notes": ["First change", "Second change"], "required": false }
/// ("notes" may also be a single string.)
/// </summary>
public static class Updater
{
    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MasteryCamoTracker-updater");
        return c;
    }

    /// <summary>Set by the --demo-update switch so the update screens can be previewed offline.</summary>
    public static bool Preview { get; set; }

    static readonly UpdateInfo PreviewUpdate = new(
        new Version(9, 9, 9, 0), "https://example.invalid/MasteryCamoTracker.exe", "", false,
        new[]
        {
            "Changes appear on your friend's screen almost instantly",
            "The admin panel opens inside the main window",
            "Clients can change their own password",
            "Weapon levels can be typed in",
        });

    public static Version Current
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
            return Normalize(v);
        }
    }

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    static Version Normalize(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>Returns the update when the hosted version is newer than this build, otherwise null. Never throws.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string manifestUrl)
    {
        if (Preview) return PreviewUpdate;
        try
        {
            if (!manifestUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var json = await Http.GetStringAsync(manifestUrl, cts.Token);
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            var version = Normalize(Version.Parse(r.GetProperty("version").GetString()!));
            var url = r.GetProperty("url").GetString() ?? "";
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            var highlights = new List<string>();
            var notes = "";
            if (r.TryGetProperty("notes", out var n))
            {
                if (n.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in n.EnumerateArray())
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) highlights.Add(s.Trim());
                    }
                }
                else if (n.ValueKind == JsonValueKind.String)
                {
                    notes = n.GetString() ?? "";
                    foreach (var line in notes.Split('\n'))
                    {
                        var s = line.Trim().TrimStart('-', '*', '\u2022').Trim();
                        if (s.Length > 0) highlights.Add(s);
                    }
                }
            }
            if (notes.Length == 0) notes = string.Join("; ", highlights);
            var required = r.TryGetProperty("required", out var q) && q.ValueKind == JsonValueKind.True;
            return version > Current ? new UpdateInfo(version, url, notes, required, highlights) : null;
        }
        catch
        {
            return null; // offline or no manifest yet: carry on without updating
        }
    }

    /// <summary>Downloads the new exe next to the current one, then swaps it in after this process exits and restarts.</summary>
    public static async Task DownloadAndRestartAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        if (Preview)
        {
            for (int i = 1; i <= 10; i++) { await Task.Delay(120); progress?.Report(i / 10.0); }
            throw new InvalidOperationException("Preview only: nothing was downloaded or changed.");
        }
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the running program.");
        var fresh = exe + ".new";

        {
            using var res = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength ?? 0;
            await using var src = await res.Content.ReadAsStreamAsync();
            await using var dst = File.Create(fresh);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        if (new FileInfo(fresh).Length < 1_000_000)
        {
            File.Delete(fresh);
            throw new InvalidOperationException("The download looks incomplete. Try again.");
        }

        // A tiny script waits for this program to exit, replaces the exe, and starts it again.
        var script = Path.Combine(Path.GetTempPath(), "mastery-camo-tracker-update.cmd");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("set tries=0");
        sb.AppendLine(":retry");
        sb.AppendLine("ping -n 2 127.0.0.1 >nul");
        sb.AppendLine("set /a tries+=1");
        sb.AppendLine($"move /y \"{fresh}\" \"{exe}\" >nul 2>&1");
        sb.AppendLine("if errorlevel 1 (");
        sb.AppendLine("  if %tries% lss 30 goto retry");
        sb.AppendLine("  exit /b 1");
        sb.AppendLine(")");
        sb.AppendLine($"start \"\" \"{exe}\"");
        sb.AppendLine("del \"%~f0\"");
        File.WriteAllText(script, sb.ToString(), Encoding.ASCII);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        Application.Current.Shutdown();
    }
}
