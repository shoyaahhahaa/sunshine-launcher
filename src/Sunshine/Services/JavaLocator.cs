using System.Diagnostics;
using System.IO;
using Sunshine.Models;

namespace Sunshine.Services;

public static class JavaLocator
{
    /// <summary>
    /// Finds a javaw.exe to launch with, preferring the Mojang-bundled runtime that matches
    /// the version's requested component (so mod-loaded versions get the Java build they expect).
    /// </summary>
    public static string? Find(string minecraftDir, JavaVersionRef? requested)
    {
        if (requested?.Component is { Length: > 0 } component)
        {
            // Mojang's launcher lays this out as runtime/<component>/windows/<component>/bin;
            // the "windows-x64"/"windows-x86" platform keys only show up in the download
            // manifest, not the local folder name, but check them too just in case.
            foreach (var platformDir in new[] { "windows", "windows-x64", "windows-x86" })
            {
                var bundled = Path.Combine(minecraftDir, "runtime", component, platformDir, component, "bin", "javaw.exe");
                if (File.Exists(bundled))
                    return bundled;
            }
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var candidate = Path.Combine(javaHome, "bin", "javaw.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return FindOnPath();
    }

    private static string? FindOnPath()
    {
        try
        {
            var psi = new ProcessStartInfo("where", "javaw.exe")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return first != null && File.Exists(first) ? first : null;
        }
        catch
        {
            return null;
        }
    }
}
