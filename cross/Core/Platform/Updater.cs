using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace MasteryTracker;

public sealed record UpdateInfo(Version Version, string Url, string Notes, bool Required, IReadOnlyList<string> Highlights);

/// <summary>
/// Checks update.json (a GitHub Release asset), downloads the new program for this operating system quietly in the
/// background, and swaps it in on request. The manifest has a "url" for Windows and an "assets" map for the others:
/// { "version": "0.2.0", "url": "...exe", "assets": { "linux-x64": "...", "osx-arm64": "..." }, "notes": [...] }
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
        new Version(9, 9, 9, 0), "https://example.invalid/MasteryCamoTracker", "", false,
        new[]
        {
            "Changes appear on your friend's screen almost instantly",
            "The admin panel opens inside the main window",
            "Clients can change their own password",
            "Weapon levels can be typed in",
        });

    /// <summary>The name this system's file goes by in the manifest.</summary>
    public static string Rid
    {
        get
        {
            if (OperatingSystem.IsAndroid()) return "android";
            bool arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            if (OperatingSystem.IsWindows()) return "win-x64";
            if (OperatingSystem.IsMacOS()) return arm ? "osx-arm64" : "osx-x64";
            return arm ? "linux-arm64" : "linux-x64";
        }
    }

    public static Version Current
    {
        get
        {
            // the version of the shared library (stamped at release time), which exists on phones too
            var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
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

            string url = "";
            if (OperatingSystem.IsWindows() && r.TryGetProperty("url", out var u0)) url = u0.GetString() ?? "";
            if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Object
                && assets.TryGetProperty(Rid, out var u1) && u1.ValueKind == JsonValueKind.String)
                url = u1.GetString() ?? url;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;   // nothing built for this system

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
                        var s = line.Trim().TrimStart('-', '*', '•').Trim();
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

    // ---- background download ------------------------------------------------------

    static Task<bool>? prefetch;
    static string? prefetchVersion;
    static IProgress<double>? sink;
    static double lastProgress;

    static string ExePath => OperatingSystem.IsAndroid()
        ? Path.Combine(Path.GetTempPath(), "MasteryCamoTracker.apk")      // phones install an update package, they do not swap a file
        : Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the running program.");

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
            if (File.Exists(fresh) && File.Exists(marker) && File.ReadAllText(marker).Trim() == ver
                && new FileInfo(fresh).Length > 1_000_000)
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

            if (new FileInfo(part).Length < 1_000_000)
            {
                File.Delete(part);
                return false;
            }
            File.Move(part, fresh, true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(fresh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.WriteAllText(marker, ver);
            lastProgress = 1; sink?.Report(1);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Makes sure the new program is downloaded (it usually already is), then swaps it in and restarts.</summary>
    public static async Task DownloadAndRestartAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        if (Preview)
        {
            for (int i = 1; i <= 10; i++) { await Task.Delay(120); progress?.Report(i / 10.0); }
            throw new InvalidOperationException("Preview only: nothing was downloaded or changed.");
        }

        var exe = ExePath;
        var fresh = exe + ".new";

        sink = progress;
        Prefetch(info);
        progress?.Report(lastProgress);
        bool ok = await prefetch!;
        if (!ok)
        {
            prefetch = null;
            Prefetch(info);
            ok = await prefetch!;
        }
        sink = null;
        if (!ok) throw new InvalidOperationException("The download did not finish. Check the connection and try again.");

        if (OperatingSystem.IsAndroid())
        {
            // hand the downloaded package to Android's installer, which asks the person to confirm
            File.Move(fresh, exe, true);
            PlatformHooks.InstallPackage?.Invoke(exe);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            // a tiny script waits for this program to exit, replaces the exe, and starts it again
            var script = Path.Combine(Path.GetTempPath(), "mastery-camo-tracker-update.cmd");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("set tries=0");
            sb.AppendLine(":retry");
            sb.AppendLine($"move /y \"{fresh}\" \"{exe}\" >nul 2>&1");
            sb.AppendLine("if not errorlevel 1 goto swapped");
            sb.AppendLine("set /a tries+=1");
            sb.AppendLine("if %tries% geq 150 exit /b 1");
            sb.AppendLine("ping -n 1 -w 100 192.0.2.1 >nul");
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
            });
        }
        else
        {
            // a tiny shell command waits for this program to exit, swaps the file and starts it again
            int pid = Environment.ProcessId;
            static string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";
            // macOS ships the program together with its native libraries as a .zip: unpack it over the old files
            string swap = info.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? $"unzip -oq {Q(fresh)} -d {Q(Path.GetDirectoryName(exe)!)}; rm -f {Q(fresh)}"
                : $"mv -f {Q(fresh)} {Q(exe)}";
            var cmd = $"while kill -0 {pid} 2>/dev/null; do sleep 0.1; done; " +
                      $"{swap} && chmod +x {Q(exe)}; rm -f {Q(fresh + ".ver")}; " +
                      $"nohup {Q(exe)} >/dev/null 2>&1 &";
            Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", cmd }, UseShellExecute = false, CreateNoWindow = true });
        }

        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
