using System;
using System.Threading;
using System.Threading.Tasks;
using Cmn = MouseKeyProxy.Common;
using Wire = MouseKeyProxy.Network.V1;
using Client = MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient;

namespace MouseKeyProxy.Commands;

/// <summary>
/// FR-MKP-006 / TR-MKP-AGENTIPC-001: the single shared implementation of the effect RPCs that the REPL
/// and the tray agent both invoke, closing the audit finding that each re-implemented gRPC dispatch
/// inline. Callers supply a client factory (the REPL and agent build the client from their own
/// authenticated channel); a null client means "not paired" and yields a NOT_PAIRED result.
/// </summary>
public sealed class RemoteServiceCommands
{
    private const string NotPaired = "NOT_PAIRED";
    private const string ProtocolVersion = "v1";

    private readonly Func<Client?> _clientFactory;

    /// <summary>Creates the shared command surface over a gRPC client factory.</summary>
    /// <param name="clientFactory">Returns a connected client, or null when no paired credential exists.</param>
    public RemoteServiceCommands(Func<Client?> clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <summary>Clears held modifiers on the remote peer.</summary>
    /// <param name="peerId">The local peer id.</param>
    /// <param name="correlationId">Correlation id for tracing.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The remote result.</returns>
    public async Task<Cmn.RemoteControlResult> ClearModifiersAsync(string peerId, string correlationId, CancellationToken ct = default)
    {
        var client = _clientFactory();
        if (client is null)
        {
            return Cmn.RemoteControlResult.Failure(NotPaired, "No paired credential; pair before clearing remote modifiers.");
        }

        var response = await client.ClearModifiersAsync(
            new Wire.ClearModifiersRequest { ProtocolVersion = ProtocolVersion, PeerId = peerId, CorrelationId = correlationId },
            cancellationToken: ct);
        return new Cmn.RemoteControlResult(response.Ok, response.Err, response.Msg);
    }

    /// <summary>Requests an emergency modifier release on the remote peer.</summary>
    /// <param name="peerId">The local peer id.</param>
    /// <param name="correlationId">Correlation id for tracing.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The remote result.</returns>
    public async Task<Cmn.RemoteControlResult> EmergencyReleaseAsync(string peerId, string correlationId, CancellationToken ct = default)
    {
        var client = _clientFactory();
        if (client is null)
        {
            return Cmn.RemoteControlResult.Failure(NotPaired, "No paired credential; pair before requesting remote emergency release.");
        }

        var response = await client.EmergencyReleaseAsync(
            new Wire.EmergencyReleaseRequest { ProtocolVersion = ProtocolVersion, PeerId = peerId, CorrelationId = correlationId },
            cancellationToken: ct);
        return new Cmn.RemoteControlResult(response.Ok, response.Err, response.Msg);
    }

    /// <summary>Injects text to the remote peer over the bidi session transport.</summary>
    /// <param name="text">The text to inject.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The remote result.</returns>
    public async Task<Cmn.RemoteControlResult> InjectTextAsync(string text, CancellationToken ct = default)
    {
        var client = _clientFactory();
        if (client is null)
        {
            return Cmn.RemoteControlResult.Failure(NotPaired, "No paired credential; pair before injecting text.");
        }

        using var transport = new BidiSessionTransport(client);
        await InputCommandHandler.SendInputAsync(transport, Cmn.InputKind.TEXT_INPUT, text, ct);
        return Cmn.RemoteControlResult.Success($"injected {text?.Length ?? 0} chars");
    }
}
