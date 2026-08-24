using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sunshine.Models;

public sealed class VersionJson
{
    public string? Id { get; set; }

    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonPropertyName("mainClass")]
    public string? MainClass { get; set; }

    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    public JsonElement? Arguments { get; set; }

    public List<LibraryEntry> Libraries { get; set; } = new();

    [JsonPropertyName("assetIndex")]
    public AssetIndexRef? AssetIndex { get; set; }

    public string? Assets { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionRef? JavaVersion { get; set; }

    public string? Type { get; set; }
}

public sealed class LibraryEntry
{
    public string? Name { get; set; }
    public List<RuleEntry>? Rules { get; set; }
    public LibraryDownloads? Downloads { get; set; }
    public Dictionary<string, string>? Natives { get; set; }
}

public sealed class LibraryDownloads
{
    public ArtifactRef? Artifact { get; set; }
    public Dictionary<string, ArtifactRef>? Classifiers { get; set; }
}

public sealed class ArtifactRef
{
    public string? Path { get; set; }
    public string? Url { get; set; }
}

public sealed class RuleEntry
{
    public string? Action { get; set; }
    public OsRule? Os { get; set; }
    public Dictionary<string, bool>? Features { get; set; }
}

public sealed class OsRule
{
    public string? Name { get; set; }
    public string? Arch { get; set; }
}

public sealed class AssetIndexRef
{
    public string? Id { get; set; }
}

public sealed class JavaVersionRef
{
    public string? Component { get; set; }

    // Some third-party launchers (e.g. TLauncher) write this as a float ("21.0")
    // instead of a plain integer, so accept either.
    public double MajorVersion { get; set; }
}
