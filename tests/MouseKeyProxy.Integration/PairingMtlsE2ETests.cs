using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Common;
using MouseKeyProxy.Service;
using MouseKeyProxy.Service.Pairing;
using Wire = MouseKeyProxy.Network.V1;
using Xunit;

namespace MouseKeyProxy.Integration;

/// <summary>
/// TR-MKP-SEC-001 / TR-MKP-ARCH-001: end-to-end proof of the pairing + mTLS + authorization pipeline
/// over a real TLS listener. An unpaired peer is rejected before any effect; a peer that pairs via the
/// one-time code completes an authenticated InjectInput; a revoked peer is rejected again.
/// </summary>
public sealed class PairingMtlsE2ETests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private PairedPeerStore _store = null!;
    private PairingCertificateAuthority _ca = null!;
    private RecordingInjector _injector = null!;
    private int _port;

    private string Address => $"https://127.0.0.1:{_port}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Boots the real service host on a free loopback port with a recording injector.</summary>
    public async ValueTask InitializeAsync()
    {
        _port = GetFreePort();
        _store = new PairedPeerStore();
        _ca = new PairingCertificateAuthority();
        _injector = new RecordingInjector();

        _app = ServiceHost.Build(
            Array.Empty<string>(),
            port: _port,
            certificateAuthority: _ca,
            pairedPeerStore: _store,
            configureServices: services => services.AddSingleton<IInputInjector>(_injector),
            useWindowsServiceLifetime: false);

        await _app.StartAsync();
    }

    /// <summary>Shuts the host down.</summary>
    public async ValueTask DisposeAsync() => await _app.StopAsync();

    /// <summary>An unpaired peer that presents no client certificate is rejected before any injection.</summary>
    [Fact]
    [Trait("Category", "SecurityE2E")]
    public async Task Unpaired_InjectInput_IsRejected()
    {
        using var channel = Grpc.Net.Client.GrpcChannel.ForAddress(Address, InsecureClientOptions());
        var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.InjectInputAsync(new Wire.InjectInputRequest
            {
                ProtocolVersion = "v1",
                PeerId = "intruder",
                Events = { new Wire.InputEvent { Kind = Wire.InputKind.KeyDown, Vk = 0x41 } },
            }, cancellationToken: Ct));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
        Assert.Empty(_injector.Batches);
    }

    /// <summary>A peer that pairs with a valid one-time code can then inject over the mTLS channel.</summary>
    [Fact]
    [Trait("Category", "SecurityE2E")]
    public async Task PairedPeer_InjectInput_Succeeds()
    {
        var code = _store.IssuePairingCode(TimeSpan.FromMinutes(5));
        var credential = await PairingClient.PairAsync(Address, "peer-e2e", code, cancellationToken: Ct);

        using var channel = PairingClient.CreateAuthenticatedChannel(Address, credential);
        var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);

        var result = await client.InjectInputAsync(new Wire.InjectInputRequest
        {
            ProtocolVersion = "v1",
            PeerId = "peer-e2e",
            Events = { new Wire.InputEvent { Kind = Wire.InputKind.KeyDown, Vk = 0x42 } },
        }, cancellationToken: Ct);

        Assert.True(result.Ok);
        Assert.Contains(_injector.Batches.SelectMany(b => b), e => e.Vk == 0x42);
    }

    /// <summary>A revoked peer is rejected even though its certificate still chains to the CA.</summary>
    [Fact]
    [Trait("Category", "SecurityE2E")]
    public async Task RevokedPeer_InjectInput_IsRejected()
    {
        var code = _store.IssuePairingCode(TimeSpan.FromMinutes(5));
        var credential = await PairingClient.PairAsync(Address, "peer-revoke", code, cancellationToken: Ct);

        Assert.True(_store.Revoke("peer-revoke"));

        using var channel = PairingClient.CreateAuthenticatedChannel(Address, credential);
        var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.InjectInputAsync(new Wire.InjectInputRequest
            {
                ProtocolVersion = "v1",
                PeerId = "peer-revoke",
                Events = { new Wire.InputEvent { Kind = Wire.InputKind.KeyDown, Vk = 0x43 } },
            }, cancellationToken: Ct));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    /// <summary>A paired peer that opens a session declaring an incompatible major version is rejected.</summary>
    [Fact]
    [Trait("Category", "SecurityE2E")]
    public async Task PairedPeer_OpenSession_VersionMismatch_IsRejected()
    {
        var code = _store.IssuePairingCode(TimeSpan.FromMinutes(5));
        var credential = await PairingClient.PairAsync(Address, "peer-ver", code, cancellationToken: Ct);

        using var channel = PairingClient.CreateAuthenticatedChannel(Address, credential);
        var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);

        using var call = client.OpenSession(cancellationToken: Ct);
        await call.RequestStream.WriteAsync(new Wire.SessionFrame
        {
            Seq = 1,
            Control = new Wire.ControlMsg { Seq = 1, Hello = new Wire.VersionHello { MyVer = "2.0", PeerVer = "1.0" } },
        }, Ct);
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var _ in call.ResponseStream.ReadAllAsync(Ct))
            {
            }
        });

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Contains("VERSION_MISMATCH", ex.Status.Detail);
    }

    /// <summary>When a session ends, the receiving host clears modifiers so none stay stuck down.</summary>
    [Fact]
    [Trait("Category", "SecurityE2E")]
    public async Task OpenSession_Teardown_ClearsModifiers()
    {
        var code = _store.IssuePairingCode(TimeSpan.FromMinutes(5));
        var credential = await PairingClient.PairAsync(Address, "peer-teardown", code, cancellationToken: Ct);

        using var channel = PairingClient.CreateAuthenticatedChannel(Address, credential);
        var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);

        using var call = client.OpenSession(cancellationToken: Ct);
        await call.RequestStream.CompleteAsync();
        await foreach (var _ in call.ResponseStream.ReadAllAsync(Ct))
        {
        }

        // Teardown ran: a modifier-clear batch (all KEY_UP) was injected on the receiving host.
        Assert.Contains(_injector.Batches, b => b.Count > 0 && b.TrueForAll(e => e.Kind == InputKind.KEY_UP));
    }

    private static Grpc.Net.Client.GrpcChannelOptions InsecureClientOptions()
    {
        var handler = new System.Net.Http.SocketsHttpHandler
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            }
        };
        return new Grpc.Net.Client.GrpcChannelOptions { HttpHandler = handler };
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Records injected batches so tests can assert the effect actually reached the injector.</summary>
    private sealed class RecordingInjector : IInputInjector
    {
        public ConcurrentQueue<List<InputEvent>> Batches { get; } = new();

        public void Send(InputEvent evt) => Batches.Enqueue(new List<InputEvent> { evt });

        public bool TryInjectBatch(IEnumerable<InputEvent> events, out string? error)
        {
            Batches.Enqueue(events.ToList());
            error = null;
            return true;
        }
    }
}
