using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MouseKeyProxy.Commands.ShareMount;

/// <summary>
/// TR-MKP-SHARE-WINFSP: detects whether the WinFsp kernel/user runtime is installed on this host.
/// Mount entry points call this so missing runtime yields a clear error instead of a crash.
/// </summary>
public static class WinFspRuntime
{
    /// <summary>
    /// Human-readable install hint shown when the runtime is missing.
    /// </summary>
    public const string InstallHint =
        "Install WinFsp from https://winfsp.dev/rel/ (includes the kernel driver), then retry. " +
        "MouseKeyProxy uses the open-source winfsp.net API under the WinFsp FLOSS exception.";

    /// <summary>
    /// Returns true when this process is on Windows and a WinFsp native DLL can be located.
    /// Does not load the driver; only checks for the installed runtime files.
    /// </summary>
    public static bool IsAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return TryFindNativeDll(out _);
    }

    /// <summary>Describes availability for CLI/UI status lines.</summary>
    public static string DescribeAvailability()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "WinFsp virtual drives are only supported on Windows hosts.";
        }

        if (TryFindNativeDll(out var path))
        {
            return $"WinFsp runtime found: {path}";
        }

        return "WinFsp runtime not found. " + InstallHint;
    }

    /// <summary>Locates winfsp-x64.dll / winfsp-x86.dll / winfsp-a64.dll under Program Files.</summary>
    public static bool TryFindNativeDll(out string path)
    {
        path = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "winfsp-x64.dll",
            Architecture.X86 => "winfsp-x86.dll",
            Architecture.Arm64 => "winfsp-a64.dll",
            _ => "winfsp-x64.dll",
        };

        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"C:\Program Files (x86)",
            @"C:\Program Files",
        ];

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var candidate = Path.Combine(root, "WinFsp", "bin", arch);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        // Also accept PATH resolution (some installs register the bin directory).
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), arch);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
        }
        catch
        {
            // ignore PATH scan failures
        }

        return false;
    }
}
