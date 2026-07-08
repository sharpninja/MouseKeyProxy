namespace MouseKeyProxy.Common;

/// <summary>
/// Peer topology resolution. Prefers explicit configuration (MKP_LOCAL_PEER / MKP_REMOTE_PEER),
/// still recognizes the named developer lab machines, and otherwise runs standalone rather than
/// failing, so the product is usable on any machine (not only the two lab boxes).
/// </summary>
public static class LabTopology
{
    /// <summary>Named lab machine (Legion2).</summary>
    public const string Legion2 = "payton-legion2";

    /// <summary>Named lab machine (Desktop).</summary>
    public const string Desktop = "payton-desktop";

    /// <summary>Environment variable overriding the local peer name.</summary>
    public const string LocalPeerEnv = "MKP_LOCAL_PEER";

    /// <summary>Environment variable overriding the remote peer name.</summary>
    public const string RemotePeerEnv = "MKP_REMOTE_PEER";

    /// <summary>Default gRPC port (override with MKP_GRPC_PORT).</summary>
    public const int GrpcPort = 50051;

    /// <summary>
    /// Resolves (localPeer, remotePeer) from the current machine and environment. Never throws;
    /// an unconfigured machine returns (thisMachine, "") and runs standalone until paired.
    /// </summary>
    public static (string LocalPeer, string RemotePeer) ResolvePeers()
        => ResolvePeers(
            Environment.MachineName,
            Environment.GetEnvironmentVariable(LocalPeerEnv),
            Environment.GetEnvironmentVariable(RemotePeerEnv));

    /// <summary>
    /// Pure resolution over an explicit machine name and optional overrides (testable seam).
    /// Precedence: explicit local+remote overrides, then the named lab machines, then standalone.
    /// </summary>
    /// <param name="machineName">The local machine name.</param>
    /// <param name="localOverride">Optional explicit local peer name.</param>
    /// <param name="remoteOverride">Optional explicit remote peer name.</param>
    /// <returns>The resolved (localPeer, remotePeer); remotePeer is empty when none is configured.</returns>
    public static (string LocalPeer, string RemotePeer) ResolvePeers(string machineName, string? localOverride, string? remoteOverride)
    {
        if (!string.IsNullOrWhiteSpace(localOverride) && !string.IsNullOrWhiteSpace(remoteOverride))
        {
            return (localOverride!.Trim(), remoteOverride!.Trim());
        }

        var machine = machineName ?? string.Empty;
        if (machine.Equals(Legion2, StringComparison.OrdinalIgnoreCase))
        {
            return (Legion2, Desktop);
        }

        if (machine.Equals(Desktop, StringComparison.OrdinalIgnoreCase))
        {
            return (Desktop, Legion2);
        }

        // Not a configured peer: run standalone (this machine, no remote until paired).
        return (machine.ToLowerInvariant(), string.Empty);
    }

    /// <summary>Builds the gRPC URL for a host on the configured port.</summary>
    /// <param name="host">Target host name or address.</param>
    /// <returns>The gRPC URL, or empty when <paramref name="host"/> is empty.</returns>
    public static string GrpcUrl(string host) =>
        string.IsNullOrWhiteSpace(host) ? string.Empty : $"http://{host}:{GrpcPort}";
}