using System.Diagnostics;

namespace SPenTablet.Host.Adb;

public static class AdbHelper
{
    public static string? Find(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return configuredPath;

        var sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "Android", "Sdk", "platform-tools", "adb.exe");
        if (File.Exists(sdk)) return sdk;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "adb.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { } // ignore malformed PATH entries
        }
        return null;
    }

    /// <summary>Runs "adb reverse". Returns false if adb is missing or no phone is attached.</summary>
    public static bool TryReverse(string? configuredPath, int port)
    {
        var adb = Find(configuredPath);
        if (adb is null) return false;

        var psi = new ProcessStartInfo(adb, $"reverse tcp:{port} tcp:{port}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi);
        if (proc is null) return false;
        if (!proc.WaitForExit(5000)) { proc.Kill(); return false; }
        return proc.ExitCode == 0;
    }
}
