using System.IO;
using System.Text.Json;
using Sunshine.Models;

namespace Sunshine.Services;

/// <summary>
/// Reads already-installed version folders under &lt;.minecraft&gt;/versions and merges
/// inheritsFrom chains (Fabric/Quilt loader profiles inherit from a vanilla version).
/// This launcher never downloads game files itself - it only launches installs that
/// already exist on disk (created by the vanilla launcher, Fabric/Quilt installer, etc).
/// </summary>
public sealed class VersionResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string MinecraftDir { get; }

    public VersionResolver(string minecraftDir)
    {
        MinecraftDir = minecraftDir;
    }

    public List<string> ListInstalledVersionIds()
    {
        var versionsDir = Path.Combine(MinecraftDir, "versions");
        if (!Directory.Exists(versionsDir))
            return new List<string>();

        var result = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(versionsDir))
        {
            var id = Path.GetFileName(dir);
            var jsonPath = Path.Combine(dir, id + ".json");
            if (File.Exists(jsonPath))
                result.Add(id);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public ResolvedVersion Resolve(string versionId)
    {
        var chain = new List<VersionJson>();
        var jarSearchOrder = new List<string>();

        var currentId = versionId;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrEmpty(currentId))
        {
            if (!visited.Add(currentId))
                throw new InvalidOperationException($"Circular inheritsFrom chain detected at '{currentId}'.");

            var json = LoadRaw(currentId);
            chain.Add(json);
            jarSearchOrder.Add(currentId);
            currentId = json.InheritsFrom;
        }

        // chain[0] is the most specific (the one the user picked), last is the root vanilla version.
        var mainClass = chain.Select(c => c.MainClass).FirstOrDefault(m => !string.IsNullOrEmpty(m))
            ?? throw new InvalidOperationException($"No mainClass found for '{versionId}' or its parents.");

        var assetIndexId = chain.Select(c => c.AssetIndex?.Id ?? c.Assets).FirstOrDefault(a => !string.IsNullOrEmpty(a));
        var versionType = chain.Select(c => c.Type).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "release";
        var minecraftArguments = chain.Select(c => c.MinecraftArguments).FirstOrDefault(a => !string.IsNullOrEmpty(a));
        var javaVersion = chain.Select(c => c.JavaVersion).FirstOrDefault(j => j != null);

        var libraries = new List<LibraryEntry>();
        var gameArgs = new List<JsonElement>();
        var jvmArgs = new List<JsonElement>();

        // Walk from root (vanilla) to leaf (profile) so JVM/game args append in a sane order.
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var v = chain[i];
            libraries.AddRange(v.Libraries);

            if (v.Arguments is { } argsElement && argsElement.ValueKind == JsonValueKind.Object)
            {
                if (argsElement.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Array)
                    gameArgs.AddRange(game.EnumerateArray());
                if (argsElement.TryGetProperty("jvm", out var jvm) && jvm.ValueKind == JsonValueKind.Array)
                    jvmArgs.AddRange(jvm.EnumerateArray());
            }
        }

        var clientJarPath = FindClientJar(jarSearchOrder)
            ?? throw new FileNotFoundException($"No client jar found for '{versionId}' or any parent version.");

        return new ResolvedVersion
        {
            Id = versionId,
            MainClass = mainClass,
            ClientJarPath = clientJarPath,
            Libraries = libraries,
            AssetIndexId = assetIndexId,
            MinecraftArguments = minecraftArguments,
            GameArguments = gameArgs,
            JvmArguments = jvmArgs,
            VersionType = versionType,
            JavaVersion = javaVersion,
        };
    }

    private VersionJson LoadRaw(string versionId)
    {
        var jsonPath = Path.Combine(MinecraftDir, "versions", versionId, versionId + ".json");
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"Version json not found: {jsonPath}");

        using var stream = File.OpenRead(jsonPath);
        return JsonSerializer.Deserialize<VersionJson>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse {jsonPath}");
    }

    private string? FindClientJar(List<string> idsFromLeafToRoot)
    {
        foreach (var id in idsFromLeafToRoot)
        {
            var jarPath = Path.Combine(MinecraftDir, "versions", id, id + ".jar");
            if (File.Exists(jarPath))
                return jarPath;
        }
        return null;
    }
}
