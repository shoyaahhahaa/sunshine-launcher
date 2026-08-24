using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Sunshine.Models;

namespace Sunshine.Services;

public sealed class LaunchResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public Process? Process { get; init; }
    public string? LogPath { get; init; }
}

/// <summary>
/// Builds the java command line for an already-installed version and starts it.
/// This launcher does not download or modify game files - it only launches what's
/// already on disk under the .minecraft folder (vanilla, Fabric, or Quilt installs).
/// </summary>
public sealed class GameLauncher
{
    private const string LauncherName = "Sunshine";
    private const string LauncherVersion = "1.0";

    private readonly string _minecraftDir;
    private readonly VersionResolver _versionResolver;

    public GameLauncher(string minecraftDir)
    {
        _minecraftDir = minecraftDir;
        _versionResolver = new VersionResolver(minecraftDir);
    }

    public async Task<LaunchResult> LaunchAsync(LaunchProfile profile, Action<string>? onStatus = null)
    {
        try
        {
            onStatus?.Invoke("Resolving version...");
            var resolved = _versionResolver.Resolve(profile.VersionId);

            var librariesDir = Path.Combine(_minecraftDir, "libraries");
            var (classpathLibs, nativeLibs) = LibraryResolver.Resolve(resolved.Libraries, librariesDir);

            onStatus?.Invoke("Locating Java...");
            var javaExe = JavaLocator.Find(_minecraftDir, resolved.JavaVersion);
            if (javaExe == null)
                return Fail("No Java runtime found. Install Java or set JAVA_HOME.");

            onStatus?.Invoke("Extracting natives...");
            var nativesDir = Path.Combine(Path.GetTempPath(), "Sunshine", "natives", $"{resolved.Id}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(nativesDir);
            ExtractNatives(nativeLibs, nativesDir);

            var classpath = string.Join(';',
                classpathLibs.Select(l => l.JarPath).Append(resolved.ClientJarPath));

            var uuid = OfflineAuth.OfflineUuid(profile.Username);

            var placeholders = new Dictionary<string, string>
            {
                ["auth_player_name"] = profile.Username,
                ["version_name"] = resolved.Id,
                ["game_directory"] = _minecraftDir,
                ["assets_root"] = Path.Combine(_minecraftDir, "assets"),
                ["assets_index_name"] = resolved.AssetIndexId ?? "legacy",
                ["auth_uuid"] = uuid.ToString(),
                ["auth_access_token"] = "0",
                ["clientid"] = "",
                ["auth_xuid"] = "",
                ["user_type"] = "legacy",
                ["version_type"] = resolved.VersionType ?? "release",
                ["natives_directory"] = nativesDir,
                ["launcher_name"] = LauncherName,
                ["launcher_version"] = LauncherVersion,
                ["classpath"] = classpath,
            };

            var jvmArgs = BuildJvmArgs(profile, nativesDir, resolved, placeholders);
            var gameArgs = BuildGameArgs(resolved, placeholders);

            var psi = new ProcessStartInfo
            {
                FileName = javaExe,
                WorkingDirectory = _minecraftDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in jvmArgs) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(resolved.MainClass);
            foreach (var a in gameArgs) psi.ArgumentList.Add(a);

            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sunshine", "logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "latest.log");
            var logWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
            logWriter.WriteLine($"[Sunshine] java: {javaExe}");
            logWriter.WriteLine($"[Sunshine] mainClass: {resolved.MainClass}");
            logWriter.WriteLine($"[Sunshine] classpath entries: {classpathLibs.Count + 1}");
            logWriter.WriteLine($"[Sunshine] args: {string.Join(' ', psi.ArgumentList)}");

            onStatus?.Invoke("Starting Minecraft...");
            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) logWriter.WriteLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) logWriter.WriteLine(e.Data); };
            process.Exited += (_, _) => { logWriter.Dispose(); TryDeleteDirectory(nativesDir); };

            if (!process.Start())
                return Fail("Failed to start the java process.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Give it a moment: if it dies immediately, that's almost always a bad classpath/args.
            var exitedEarly = await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(3000)) == process.WaitForExitAsync();
            if (exitedEarly && process.HasExited && process.ExitCode != 0)
            {
                return Fail($"Minecraft exited immediately (code {process.ExitCode}). See log: {logPath}", logPath);
            }

            return new LaunchResult { Success = true, Message = "Launched.", Process = process, LogPath = logPath };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static LaunchResult Fail(string message, string? logPath = null) =>
        new() { Success = false, Message = message, LogPath = logPath };

    private static List<string> BuildJvmArgs(LaunchProfile profile, string nativesDir, ResolvedVersion resolved, Dictionary<string, string> placeholders)
    {
        var args = new List<string>
        {
            $"-Xms{profile.MinRamMb}M",
            $"-Xmx{profile.MaxRamMb}M",
            $"-Djava.library.path={nativesDir}",
            "-Dfile.encoding=UTF-8",
            $"-Dminecraft.launcher.brand={LauncherName}",
            $"-Dminecraft.launcher.version={LauncherVersion}",
        };

        if (profile.PerformanceFlags)
        {
            args.AddRange(new[]
            {
                "-XX:+UseG1GC",
                "-XX:+ParallelRefProcEnabled",
                "-XX:MaxGCPauseMillis=200",
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:+DisableExplicitGC",
                "-XX:+AlwaysPreTouch",
                "-XX:G1NewSizePercent=30",
                "-XX:G1MaxNewSizePercent=40",
                "-XX:G1HeapRegionSize=8M",
                "-XX:G1ReservePercent=20",
                "-XX:G1HeapWastePercent=5",
                "-XX:G1MixedGCCountTarget=4",
                "-XX:InitiatingHeapOccupancyPercent=15",
                "-XX:G1MixedGCLiveThresholdPercent=90",
                "-XX:G1RSetUpdatingPauseTimePercent=5",
                "-XX:SurvivorRatio=32",
                "-XX:+PerfDisableSharedMem",
                "-XX:MaxTenuringThreshold=1",
            });
        }

        if (resolved.JvmArguments.Count > 0)
        {
            // Modern version.json already includes "-cp" "${classpath}" in this array.
            foreach (var token in ExtractTokens(resolved.JvmArguments))
                args.Add(Substitute(token, placeholders));
        }
        else
        {
            // Very old (pre-1.13) version jsons have no "jvm" array - add the bare minimum ourselves.
            args.Add("-cp");
            args.Add(placeholders["classpath"]);
        }

        return args;
    }

    private static List<string> BuildGameArgs(ResolvedVersion resolved, Dictionary<string, string> placeholders)
    {
        var args = new List<string>();

        if (resolved.GameArguments.Count > 0)
        {
            foreach (var token in ExtractTokens(resolved.GameArguments))
                args.Add(Substitute(token, placeholders));
        }
        else if (!string.IsNullOrEmpty(resolved.MinecraftArguments))
        {
            foreach (var token in resolved.MinecraftArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(Substitute(token, placeholders));
        }
        else
        {
            args.AddRange(new[]
            {
                "--username", placeholders["auth_player_name"],
                "--version", placeholders["version_name"],
                "--gameDir", placeholders["game_directory"],
                "--assetsDir", placeholders["assets_root"],
                "--assetIndex", placeholders["assets_index_name"],
                "--uuid", placeholders["auth_uuid"],
                "--accessToken", placeholders["auth_access_token"],
                "--userType", placeholders["user_type"],
                "--versionType", placeholders["version_type"],
            });
        }

        return args;
    }

    /// <summary>
    /// Walks a version.json "game"/"jvm" argument array, yielding only unconditional string
    /// tokens and tokens gated by rules that match (no optional features are ever active here).
    /// </summary>
    private static IEnumerable<string> ExtractTokens(List<JsonElement> elements)
    {
        foreach (var el in elements)
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                yield return el.GetString()!;
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                if (!el.TryGetProperty("rules", out var rulesEl) || RuleElementAllows(rulesEl))
                {
                    // Mojang's schema uses singular "value" (string or array); some third-party
                    // launchers (e.g. TLauncher) instead write plural "values" (always an array).
                    // Accept whichever is present.
                    if (!el.TryGetProperty("value", out var valueEl))
                        el.TryGetProperty("values", out valueEl);

                    if (valueEl.ValueKind == JsonValueKind.String)
                        yield return valueEl.GetString()!;
                    else if (valueEl.ValueKind == JsonValueKind.Array)
                        foreach (var v in valueEl.EnumerateArray())
                            if (v.ValueKind == JsonValueKind.String)
                                yield return v.GetString()!;
                }
            }
        }
    }

    private static bool RuleElementAllows(JsonElement rulesEl)
    {
        if (rulesEl.ValueKind != JsonValueKind.Array)
            return true;

        bool allowed = false;
        foreach (var rule in rulesEl.EnumerateArray())
        {
            bool osMatches = true;
            if (rule.TryGetProperty("os", out var osEl) && osEl.TryGetProperty("name", out var nameEl))
                osMatches = string.Equals(nameEl.GetString(), "windows", StringComparison.OrdinalIgnoreCase);

            // Feature-gated tokens (custom resolution, demo, quick play) are never active.
            bool featureMatches = !rule.TryGetProperty("features", out var featuresEl) || featuresEl.EnumerateObject().Count() == 0;

            if (osMatches && featureMatches && rule.TryGetProperty("action", out var actionEl))
                allowed = string.Equals(actionEl.GetString(), "allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }

    private static string Substitute(string token, Dictionary<string, string> placeholders)
    {
        if (!token.Contains("${"))
            return token;

        foreach (var (key, value) in placeholders)
            token = token.Replace("${" + key + "}", value);
        return token;
    }

    private static void ExtractNatives(List<ResolvedNative> natives, string destinationDir)
    {
        foreach (var native in natives)
        {
            using var archive = ZipFile.OpenRead(native.JarPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(entry.Name)) // directory entry
                    continue;

                var destPath = Path.Combine(destinationDir, Path.GetFileName(entry.FullName));
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort cleanup */ }
    }
}
