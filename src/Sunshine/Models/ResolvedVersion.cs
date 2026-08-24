using System.Text.Json;

namespace Sunshine.Models;

/// <summary>
/// A version.json fully merged with its inheritsFrom ancestor chain (e.g. a Fabric
/// loader profile merged onto its vanilla parent), ready to build a launch command from.
/// </summary>
public sealed class ResolvedVersion
{
    public required string Id { get; init; }
    public required string MainClass { get; init; }
    public required string ClientJarPath { get; init; }
    public required List<LibraryEntry> Libraries { get; init; }
    public string? AssetIndexId { get; init; }
    public string? MinecraftArguments { get; init; }
    public List<JsonElement> GameArguments { get; init; } = new();
    public List<JsonElement> JvmArguments { get; init; } = new();
    public string? VersionType { get; init; }
    public JavaVersionRef? JavaVersion { get; init; }
}
