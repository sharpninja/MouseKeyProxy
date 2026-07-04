namespace MouseKeyProxy.Common;

/// <summary>
/// Fixed two-node lab pair for PLAN-MKP-006 Transition validation.
/// </summary>
public static class LabTopology
{
    public const string Legion2 = "payton-legion2";
    public const string Desktop = "payton-desktop";
    public const int GrpcPort = 50051;

    public static (string LocalPeer, string RemotePeer) ResolvePeers()
    {
        var machine = Environment.MachineName;
        if (machine.Equals("PAYTON-LEGION2", StringComparison.OrdinalIgnoreCase) ||
            machine.Equals(Legion2, StringComparison.OrdinalIgnoreCase))
        {
            return (Legion2, Desktop);
        }

        if (machine.Equals("PAYTON-DESKTOP", StringComparison.OrdinalIgnoreCase) ||
            machine.Equals(Desktop, StringComparison.OrdinalIgnoreCase))
        {
            return (Desktop, Legion2);
        }

        throw new InvalidOperationException(
            $"Machine '{machine}' is not a configured lab peer. Expected {Legion2} or {Desktop}.");
    }

    public static string GrpcUrl(string host) => $"http://{host}:{GrpcPort}";
}