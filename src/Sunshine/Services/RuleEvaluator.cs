using System.Runtime.InteropServices;
using System.Text.Json;
using Sunshine.Models;

namespace Sunshine.Services;

/// <summary>
/// Evaluates version.json "rules" arrays for this machine (Windows, current arch/build).
/// Optional features (demo mode, custom resolution, quick play) are never active.
/// An absent or empty rules array means "always allowed" - TLauncher writes "rules": []
/// on every argument, so treating empty as deny drops the classpath and all game args.
/// </summary>
public static class RuleEvaluator
{
    public static bool Allows(List<RuleEntry>? rules)
    {
        if (rules == null || rules.Count == 0)
            return true;

        bool allowed = false;
        foreach (var rule in rules)
        {
            bool featureMatches = rule.Features == null || rule.Features.Count == 0;
            if (featureMatches && OsMatches(rule.Os?.Name, rule.Os?.Arch, rule.Os?.VersionRange?.Min, rule.Os?.VersionRange?.Max))
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }

    public static bool Allows(JsonElement rulesEl)
    {
        if (rulesEl.ValueKind != JsonValueKind.Array)
            return true;

        bool any = false, allowed = false;
        foreach (var rule in rulesEl.EnumerateArray())
        {
            any = true;
            if (rule.ValueKind != JsonValueKind.Object)
                continue;

            string? name = null, arch = null, min = null, max = null;
            if (rule.TryGetProperty("os", out var osEl) && osEl.ValueKind == JsonValueKind.Object)
            {
                name = GetString(osEl, "name");
                arch = GetString(osEl, "arch");
                if (osEl.TryGetProperty("versionRange", out var rangeEl) && rangeEl.ValueKind == JsonValueKind.Object)
                {
                    min = GetString(rangeEl, "min");
                    max = GetString(rangeEl, "max");
                }
            }

            bool featureMatches = !rule.TryGetProperty("features", out var featuresEl)
                || featuresEl.ValueKind != JsonValueKind.Object
                || !featuresEl.EnumerateObject().Any();

            if (featureMatches && OsMatches(name, arch, min, max))
                allowed = string.Equals(GetString(rule, "action"), "allow", StringComparison.OrdinalIgnoreCase);
        }
        return !any || allowed;
    }

    private static bool OsMatches(string? name, string? arch, string? minVersion, string? maxVersion)
    {
        if (!string.IsNullOrEmpty(name) && !string.Equals(name, "windows", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(arch))
        {
            var osArch = RuntimeInformation.OSArchitecture;
            bool archMatches = arch.ToLowerInvariant() switch
            {
                "x86" => osArch == Architecture.X86,
                "x64" or "amd64" or "x86_64" => osArch == Architecture.X64,
                "arm64" or "aarch64" => osArch == Architecture.Arm64,
                _ => false,
            };
            if (!archMatches)
                return false;
        }

        var current = Environment.OSVersion.Version;
        if (Version.TryParse(minVersion, out var min) && current < min)
            return false;
        if (Version.TryParse(maxVersion, out var max) && current >= max)
            return false;

        return true;
    }

    private static string? GetString(JsonElement el, string property) =>
        el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
