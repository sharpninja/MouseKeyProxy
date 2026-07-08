using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace MouseKeyProxy.Service.Pairing;

/// <summary>
/// TR-MKP-SEC-001: the pure authorization decision behind the gRPC interceptor. Pairing-bootstrap
/// RPCs (which establish the credential) are allowed unauthenticated; every other (effect-bearing)
/// RPC requires a client certificate that this CA issued and that maps to a paired, non-revoked peer.
/// </summary>
public sealed class PairingAuthorizer
{
    /// <summary>RPCs that must be reachable before a credential exists.</summary>
    private static readonly HashSet<string> BootstrapMethods = new(StringComparer.Ordinal)
    {
        "Pair",
        "RequestPairingCode",
    };

    private readonly IPairingCertificateAuthority _ca;
    private readonly IPairedPeerStore _store;

    /// <summary>Creates the authorizer over the pairing CA and paired-peer store.</summary>
    /// <param name="ca">The pairing certificate authority.</param>
    /// <param name="store">The paired-peer store.</param>
    public PairingAuthorizer(IPairingCertificateAuthority ca, IPairedPeerStore store)
    {
        _ca = ca;
        _store = store;
    }

    /// <summary>
    /// Decides whether a call is authorized. Returns null when allowed, or a short error code when denied.
    /// </summary>
    /// <param name="methodName">The bare gRPC method name (e.g. "InjectInput").</param>
    /// <param name="clientCertificate">The client certificate presented over mTLS, or null.</param>
    /// <returns>Null when authorized; otherwise a denial reason code.</returns>
    public string? Authorize(string methodName, X509Certificate2? clientCertificate)
    {
        if (BootstrapMethods.Contains(methodName))
        {
            return null;
        }

        if (clientCertificate is null)
        {
            return "NO_CLIENT_CERTIFICATE";
        }

        if (!_ca.IsIssuedByThisCa(clientCertificate))
        {
            return "UNTRUSTED_CERTIFICATE";
        }

        if (!_store.IsAuthorized(clientCertificate.Thumbprint))
        {
            return "UNPAIRED_OR_REVOKED";
        }

        return null;
    }
}
