using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MasteryTracker;

public sealed record UpdateInfo(Version Version, string Url, string Notes, bool Required, IReadOnlyList<string> Highlights, string? Sha256 = null);

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
            if (!IsTrustedDownload(url)) return null;
            var sha = r.TryGetProperty("sha256", out var shaProp) && shaProp.ValueKind == JsonValueKind.String ? shaProp.GetString()?.Trim().ToLowerInvariant() : null;
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
            return version > Current ? new UpdateInfo(version, url, notes, required, highlights, sha) : null;
        }
        catch
        {
            return null; // offline or no manifest yet: carry on without updating
        }
    }

    /// <summary>The new exe may only be fetched over HTTPS from GitHub (where the releases are published).</summary>
    static bool IsTrustedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the file looks like a Windows program and, when the manifest gives a hash, matches it.</summary>
    static bool LooksRight(string path, string? sha256)
    {
        using var s = File.OpenRead(path);
        if (s.Length < 1_000_000 || s.ReadByte() != 'M' || s.ReadByte() != 'Z') return false;     // not an .exe
        if (string.IsNullOrEmpty(sha256)) return true;
        s.Position = 0;
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    // ---- background download ------------------------------------------------------
    // The new exe is fetched as soon as an update is found, while the person keeps working, so pressing
    // UPDATE NOW only has to swap the file and restart.

    static Task<bool>? prefetch;
    static string? prefetchVersion;
    static IProgress<double>? sink;
    static double lastProgress;

    static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the running program.");

    /// <summary>Starts downloading the update quietly in the background (once per version).</summary>
    public static void Prefetch(UpdateInfo info)
    {
        if (Preview) return;
        var ver = info.Version.ToString();
        if (prefetch != null && prefetchVersion == ver && !(prefetch.IsCompleted && !prefetch.Result)) return;
        prefetchVersion = ver;
        lastProgress = 0;
        prefetch = Task.Run(() => FetchAsync(info, ver));
    }

    static async Task<bool> FetchAsync(UpdateInfo info, string ver)
    {
        try
        {
            var exe = ExePath;
            var fresh = exe + ".new";
            var marker = fresh + ".ver";
            // already downloaded earlier (for example before a restart)?
            if (File.Exists(fresh) && File.Exists(marker) && File.ReadAllText(marker).Trim() == ver
                && LooksRight(fresh, info.Sha256))
            {
                lastProgress = 1; sink?.Report(1);
                return true;
            }

            var part = exe + ".part";
            using (var res = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength ?? 0;
                await using var src = await res.Content.ReadAsStreamAsync();
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                var buf = new byte[1 << 20];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    if (total > 0)
                    {
                        lastProgress = (double)done / total;
                        sink?.Report(lastProgress);
                    }
                }
            }

            if (!LooksRight(part, info.Sha256))        // truncated, not an exe, or not the file the release says it is
            {
                File.Delete(part);
                return false;
            }
            File.Move(part, fresh, true);
            File.WriteAllText(marker, ver);
            lastProgress = 1; sink?.Report(1);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Makes sure the new exe is downloaded (it usually already is), then swaps it in and restarts.</summary>
    public static async Task DownloadAndRestartAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        if (Preview)
        {
            for (int i = 1; i <= 10; i++) { await Task.Delay(120); progress?.Report(i / 10.0); }
            throw new InvalidOperationException("Preview only: nothing was downloaded or changed.");
        }

        var exe = ExePath;
        var fresh = exe + ".new";
        var ver = info.Version.ToString();

        sink = progress;
        Prefetch(info);
        progress?.Report(lastProgress);
        bool ok = await prefetch!;
        if (!ok)
        {
            // the background attempt failed: try once more right now so the real error can be shown
            prefetch = null;
            Prefetch(info);
            ok = await prefetch!;
        }
        sink = null;
        if (!ok) throw new InvalidOperationException("The download did not finish. Check the connection and try again.");

        // A tiny script waits for this program to exit, replaces the exe, and starts it again.
        var script = Path.Combine(Path.GetTempPath(), "mastery-camo-tracker-update.cmd");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("set tries=0");
        sb.AppendLine(":retry");
        sb.AppendLine($"move /y \"{fresh}\" \"{exe}\" >nul 2>&1");
        sb.AppendLine("if not errorlevel 1 goto swapped");
        sb.AppendLine("set /a tries+=1");
        sb.AppendLine("if %tries% geq 150 exit /b 1");
        sb.AppendLine("ping -n 1 -w 100 192.0.2.1 >nul");   // about a tenth of a second, then try again
        sb.AppendLine("goto retry");
        sb.AppendLine(":swapped");
        sb.AppendLine($"del \"{fresh}.ver\" >nul 2>&1");
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
    }}
