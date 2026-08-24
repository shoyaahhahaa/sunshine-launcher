using System.IO;
using Sunshine.Models;

namespace Sunshine.Services;

public sealed record ResolvedLibrary(string JarPath);
public sealed record ResolvedNative(string JarPath);

/// <summary>
/// Evaluates library "rules" (OS gating) and resolves each library entry to either a
/// classpath jar, a natives jar to extract, or nothing (not applicable to this OS).
/// </summary>
public static class LibraryResolver
{
    public static (List<ResolvedLibrary> Classpath, List<ResolvedNative> Natives) Resolve(
        IEnumerable<LibraryEntry> libraries, string librariesDir)
    {
        var classpath = new List<ResolvedLibrary>();
        var natives = new List<ResolvedNative>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lib in libraries)
        {
            if (!RuleAllows(lib.Rules))
                continue;

            // Modern (LWJGL 3, MC 1.13+) natives ship as regular library entries whose maven
            // name/classifier is "natives-windows" - they belong on the classpath like any other
            // jar; LWJGL's own SharedLibraryLoader finds and self-extracts them at runtime. Only
            // the legacy pre-1.13 "natives"/"downloads.classifiers" scheme (below) needs manual
            // extraction into a java.library.path folder.
            var artifactPath = lib.Downloads?.Artifact?.Path ?? DerivePathFromMavenName(lib.Name);
            if (artifactPath != null)
            {
                var fullPath = Path.Combine(librariesDir, artifactPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath) && seenPaths.Add(fullPath))
                    classpath.Add(new ResolvedLibrary(fullPath));
            }

            // Legacy natives (pre-1.19): "natives": { "windows": "natives-windows" } + downloads.classifiers.
            if (lib.Natives != null && lib.Natives.TryGetValue("windows", out var classifierKey))
            {
                classifierKey = classifierKey.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                var classifierArtifact = lib.Downloads?.Classifiers?.GetValueOrDefault(classifierKey);
                if (classifierArtifact?.Path != null)
                {
                    var fullPath = Path.Combine(librariesDir, classifierArtifact.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath) && seenPaths.Add(fullPath))
                        natives.Add(new ResolvedNative(fullPath));
                }
            }
        }

        return (classpath, natives);
    }

    private static bool RuleAllows(List<RuleEntry>? rules)
    {
        if (rules == null || rules.Count == 0)
            return true;

        bool allowed = false;
        foreach (var rule in rules)
        {
            bool osMatches = rule.Os == null ||
                string.IsNullOrEmpty(rule.Os.Name) ||
                string.Equals(rule.Os.Name, "windows", StringComparison.OrdinalIgnoreCase);

            // We never activate optional features (demo mode, custom resolution, quick play),
            // so any rule that requires a feature to be true never matches here.
            bool featureMatches = rule.Features == null || rule.Features.Count == 0;

            if (osMatches && featureMatches)
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }

    private static string? DerivePathFromMavenName(string? name)
    {
        // group:artifact:version[:classifier] -> group/with/slashes/artifact/version/artifact-version[-classifier].jar
        if (string.IsNullOrEmpty(name))
            return null;

        var parts = name.Split(':');
        if (parts.Length < 3)
            return null;

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";

        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }
}
