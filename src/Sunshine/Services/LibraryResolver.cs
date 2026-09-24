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
    public static (List<ResolvedLibrary> Classpath, List<ResolvedNative> Natives, List<string> Missing) Resolve(
        IEnumerable<LibraryEntry> libraries, string librariesDir)
    {
        var classpath = new List<ResolvedLibrary>();
        var natives = new List<ResolvedNative>();
        var missing = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lib in libraries)
        {
            if (!RuleEvaluator.Allows(lib.Rules))
                continue;

            // Modern (LWJGL 3, MC 1.13+) natives ship as regular library entries whose maven
            // name/classifier is "natives-windows" - they belong on the classpath like any other
            // jar; LWJGL's own SharedLibraryLoader finds and self-extracts them at runtime. Only
            // the legacy pre-1.13 "natives"/"downloads.classifiers" scheme (below) needs manual
            // extraction into a java.library.path folder.
            var candidates = new[]
                {
                    lib.Downloads?.Artifact?.Path,
                    lib.Artifact?.Path,
                    DerivePathFromMavenName(lib.Name),
                }
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => Path.Combine(librariesDir, p!.Replace('/', Path.DirectorySeparatorChar)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var found = candidates.FirstOrDefault(File.Exists);
            if (found != null)
            {
                if (seenPaths.Add(found))
                    classpath.Add(new ResolvedLibrary(found));
            }
            else if (candidates.Count > 0 && (lib.Natives == null || lib.Downloads?.Artifact != null || lib.Artifact != null))
            {
                // Natives-only legacy entries have no main jar, so a missing one there is expected.
                missing.Add(candidates[0]);
            }

            // Legacy natives (pre-1.19): "natives": { "windows": "natives-windows" } + downloads.classifiers.
            if (lib.Natives != null && lib.Natives.TryGetValue("windows", out var classifierKey))
            {
                classifierKey = classifierKey.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                var classifierPath = lib.Downloads?.Classifiers?.GetValueOrDefault(classifierKey)?.Path
                    ?? DerivePathFromMavenName(lib.Name + ":" + classifierKey);
                if (classifierPath != null)
                {
                    var fullPath = Path.Combine(librariesDir, classifierPath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fullPath))
                        missing.Add(fullPath);
                    else if (seenPaths.Add(fullPath))
                        natives.Add(new ResolvedNative(fullPath));
                }
            }
        }

        return (classpath, natives, missing);
    }

    private static string? DerivePathFromMavenName(string? name)
    {
        // group:artifact:version[:classifier][@ext] -> group/with/slashes/artifact/version/artifact-version[-classifier].ext
        if (string.IsNullOrEmpty(name))
            return null;

        var extension = "jar";
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            extension = name[(at + 1)..];
            name = name[..at];
        }

        var parts = name.Split(':');
        if (parts.Length < 3)
            return null;

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";

        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{extension}";
    }
}
