using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Grpc.Net.Client;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Commands.ShareMount;
using Wire = MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Agent;

/// <summary>
/// TR-MKP-SHARE-WINFSP: Agent-owned WinFsp mount of the paired appliance folder share.
/// Mounts live in the user-session Agent process so Explorer sees the drive letter.
/// Unmount on Agent exit via <see cref="UnmountQuiet"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AgentShareMount
{
    private static readonly object Gate = new();

    /// <summary>Default preferred letter when none is saved and M: is free.</summary>
    public const string DefaultPreferredLetter = "M:";

    /// <summary>Persisted drive-letter preference under LocalAppData.</summary>
    public static string PreferredLetterPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MouseKeyProxy",
            "share-mount-letter.txt");

    /// <summary>True when this Agent process currently owns a WinFsp share mount.</summary>
    public static bool IsMounted => ShareMountHost.IsMounted;

    /// <summary>Current mount point when mounted; otherwise empty.</summary>
    public static string CurrentMountPoint => ShareMountHost.CurrentMountPoint;

    /// <summary>Loads the last preferred letter (defaults to <see cref="DefaultPreferredLetter"/>).</summary>
    public static string LoadPreferredLetter()
    {
        try
        {
            var path = PreferredLetterPath();
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return NormalizeLetter(text);
                }
            }
        }
        catch
        {
            // ignore corrupt preference
        }

        return DefaultPreferredLetter;
    }

    /// <summary>Persists the preferred drive letter for the next mount prompt / auto-mount.</summary>
    public static void SavePreferredLetter(string letter)
    {
        try
        {
            var path = PreferredLetterPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, NormalizeLetter(letter));
        }
        catch
        {
            // best-effort preference write
        }
    }

    /// <summary>
    /// Mounts the paired appliance share at <paramref name="mountPoint"/> using an authenticated channel.
    /// </summary>
    /// <param name="channelFactory">Returns mTLS channel to the paired device (or null if unpaired).</param>
    /// <param name="peerId">RPC peer id header.</param>
    /// <param name="mountPoint">Drive letter such as <c>M:</c> or <c>Z:</c>.</param>
    /// <param name="volumeLabel">Volume label shown by Explorer.</param>
    public static ShareMountResult MountAppliance(
        Func<GrpcChannel?> channelFactory,
        string peerId,
        string mountPoint,
        string volumeLabel = "MouseKeyProxy")
    {
        if (channelFactory is null)
        {
            return ShareMountResult.Failure("INVALID_ARGUMENT", "Channel factory is required.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return ShareMountResult.Failure(
                "PLATFORM_UNSUPPORTED",
                "WinFsp virtual drives are only supported on Windows hosts.");
        }

        if (!WinFspRuntime.IsAvailable())
        {
            return ShareMountResult.Failure("WINFSP_RUNTIME_MISSING", WinFspRuntime.DescribeAvailability());
        }

        lock (Gate)
        {
            if (ShareMountHost.IsMounted)
            {
                return ShareMountResult.Failure(
                    "ALREADY_MOUNTED",
                    $"A share volume is already mounted at {ShareMountHost.CurrentMountPoint}. Unmount it first.",
                    ShareMountHost.CurrentMountPoint);
            }

            GrpcChannel? channel;
            try
            {
                channel = channelFactory();
            }
            catch (Exception ex)
            {
                return ShareMountResult.Failure("CHANNEL_FAILED", ex.Message);
            }

            if (channel is null)
            {
                return ShareMountResult.Failure(
                    "NOT_PAIRED",
                    "Not paired to a device. Pair the Agent first, then mount the appliance share.");
            }

            try
            {
                var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
                var share = new FolderShareClient(client, peerId);
                // Quick enable check so users get a clear error before WinFsp attaches.
                try
                {
                    var info = share.GetInfoAsync().GetAwaiter().GetResult();
                    if (!info.Ok || !info.Enabled)
                    {
                        return ShareMountResult.Failure(
                            string.IsNullOrEmpty(info.Err) ? "SHARE_DISABLED" : info.Err,
                            string.IsNullOrEmpty(info.Msg)
                                ? "Folder share is not enabled on the appliance (set MKP_FOLDER_SHARE=1)."
                                : info.Msg);
                    }
                }
                catch (Exception ex)
                {
                    return ShareMountResult.Failure(
                        "SHARE_UNAVAILABLE",
                        $"Could not query appliance share: {ex.Message}");
                }

                var backend = new FolderShareClientBackend(share);
                var result = ShareMountHost.Mount(backend, mountPoint, volumeLabel);
                if (result.Ok)
                {
                    SavePreferredLetter(result.MountPoint.Length > 0 ? result.MountPoint : mountPoint);
                }

                // Keep channel open for the lifetime of the mount: FolderShareClientBackend
                // issues RPCs on each FS op. Pin channel on the backend via wrapper.
                if (result.Ok)
                {
                    PinnedChannel.Hold(channel);
                }
                else
                {
                    try { channel.Dispose(); } catch { /* ignore */ }
                }

                return result;
            }
            catch (Exception ex)
            {
                try { channel.Dispose(); } catch { /* ignore */ }
                return ShareMountResult.Failure("MOUNT_FAILED", ex.Message);
            }
        }
    }

    /// <summary>Unmounts the Agent-owned volume if present.</summary>
    public static ShareMountResult Unmount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ShareMountResult.Failure(
                "PLATFORM_UNSUPPORTED",
                "WinFsp virtual drives are only supported on Windows hosts.");
        }

        lock (Gate)
        {
            var result = ShareMountHost.Unmount();
            PinnedChannel.Release();
            return result;
        }
    }

    /// <summary>Best-effort unmount for Agent shutdown (never throws).</summary>
    public static void UnmountQuiet()
    {
        try
        {
            if (OperatingSystem.IsWindows() && ShareMountHost.IsMounted)
            {
                Unmount();
            }
        }
        catch
        {
            // shutdown path
        }
    }

    /// <summary>
    /// Picks a free drive letter: preferred if free, else first free among common candidates.
    /// </summary>
    public static string SuggestFreeLetter(string? preferred = null)
    {
        preferred = NormalizeLetter(preferred ?? LoadPreferredLetter());
        var candidates = new[]
        {
            preferred, "M:", "Z:", "Y:", "X:", "W:", "V:", "U:", "T:", "S:", "R:", "Q:", "P:", "O:", "N:",
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var letter in candidates)
        {
            if (!Directory.Exists(letter + "\\"))
            {
                return letter;
            }
        }

        return preferred;
    }

    private static string NormalizeLetter(string letter)
    {
        letter = (letter ?? string.Empty).Trim();
        if (letter.Length == 1 && char.IsLetter(letter[0]))
        {
            return char.ToUpperInvariant(letter[0]) + ":";
        }

        if (letter.Length >= 2 && char.IsLetter(letter[0]) && letter[1] == ':')
        {
            return char.ToUpperInvariant(letter[0]) + ":";
        }

        return letter;
    }

    /// <summary>
    /// Holds the gRPC channel for the mount lifetime so FS callbacks can still RPC.
    /// </summary>
    private static class PinnedChannel
    {
        private static GrpcChannel? _channel;

        public static void Hold(GrpcChannel channel)
        {
            Release();
            _channel = channel;
        }

        public static void Release()
        {
            try { _channel?.Dispose(); } catch { /* ignore */ }
            _channel = null;
        }
    }
}
