namespace MouseKeyProxy.Service.Pairing;

/// <summary>
/// TR-MKP-SEC-001: pairing behavior options resolved from configuration/environment and injected into
/// the gRPC service. Trust-on-first-use enables the plug-n-play path (accept the first peer without a
/// code while unpaired).
/// </summary>
public sealed class ServicePairingOptions
{
    /// <summary>When true, the first Pair on an unpaired device is accepted without a one-time code.</summary>
    public bool TrustOnFirstUse { get; init; }
}
