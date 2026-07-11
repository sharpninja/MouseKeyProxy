using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;
using MouseKeyProxy.Network.V1;
using MouseKeyProxy.Service.Pairing;
using CommonScreenshotTarget = MouseKeyProxy.Common.ScreenshotTarget;
using WireScreenshotTarget = MouseKeyProxy.Network.V1.ScreenshotTarget;

namespace MouseKeyProxy.Service;

/// <summary>
/// gRPC service implementation with full ILogger usage.
/// All key operations are logged so they appear in Windows Event Viewer.
/// Wires dispatcher for receive->inject (AC4/AC5).
/// </summary>
public class MouseKeyProxyImpl : MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyBase
{
    private const string AgentIpcUnavailable = "AGENT_IPC_UNAVAILABLE";
    private const int ScreenshotChunkSize = 64 * 1024;

    /// <summary>TR-MKP-SEC-001: default pairing-code lifetime when the caller does not specify one.</summary>
    private static readonly TimeSpan DefaultPairingCodeTtl = TimeSpan.FromMinutes(5);

    /// <summary>TR-MKP-SEC-001: lifetime of an issued per-peer client certificate.</summary>
    private static readonly TimeSpan PeerCertValidity = TimeSpan.FromDays(365);

    private readonly ILogger<MouseKeyProxyImpl> _logger;
    private readonly SessionFrameDispatcher? _dispatcher;
    private readonly IRemoteDesktopController? _desktopController;
    private readonly IEmergencyReleaseController? _emergencyReleaseController;
    private readonly IModifierReleaseController? _modifierReleaseController;
    private readonly IScreenshotCapture? _screenshotCapture;
    private readonly ISystemPowerController _powerController;
    private readonly IPairedPeerStore _pairedPeerStore;
    private readonly IPairingCertificateAuthority _certificateAuthority;
    private readonly bool _trustOnFirstUse;
    private readonly DeviceFunctionCoordinator? _deviceFunctions;
    private readonly IFolderShareStore? _folderShare;
    private readonly IShareAccessAllowlist? _shareAllowlist;
    private readonly IClientPairingCodeIssuer? _clientPairingCodes;
    private readonly Device.ISmbShareController? _smb;
    private readonly IInstallTicketStore? _installTickets;
    private readonly IClientInstallIntroMailbox? _clientIntros;

    /// <summary>Default TTL for clipboard intro mailbox entries.</summary>
    private static readonly TimeSpan DefaultIntroTtl = TimeSpan.FromMinutes(5);

    public MouseKeyProxyImpl(
        ILogger<MouseKeyProxyImpl> logger,
        SessionFrameDispatcher? dispatcher = null,
        IRemoteDesktopController? desktopController = null,
        IEmergencyReleaseController? emergencyReleaseController = null,
        IModifierReleaseController? modifierReleaseController = null,
        IScreenshotCapture? screenshotCapture = null,
        ISystemPowerController? powerController = null,
        IPairedPeerStore? pairedPeerStore = null,
        IPairingCertificateAuthority? certificateAuthority = null,
        ServicePairingOptions? pairingOptions = null,
        DeviceFunctionCoordinator? deviceFunctions = null,
        IFolderShareStore? folderShare = null,
        IShareAccessAllowlist? shareAllowlist = null,
        IClientPairingCodeIssuer? clientPairingCodes = null,
        Device.ISmbShareController? smb = null,
        IInstallTicketStore? installTickets = null,
        IClientInstallIntroMailbox? clientIntros = null)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _desktopController = desktopController;
        _emergencyReleaseController = emergencyReleaseController;
        _modifierReleaseController = modifierReleaseController;
        _screenshotCapture = screenshotCapture;
        _powerController = powerController ?? new UnsupportedPowerController();
        _pairedPeerStore = pairedPeerStore ?? new PairedPeerStore();
        _certificateAuthority = certificateAuthority ?? new PairingCertificateAuthority();
        _trustOnFirstUse = pairingOptions?.TrustOnFirstUse ?? false;
        _deviceFunctions = deviceFunctions;
        _folderShare = folderShare;
        _shareAllowlist = shareAllowlist;
        _clientPairingCodes = clientPairingCodes;
        _smb = smb;
        _installTickets = installTickets;
        _clientIntros = clientIntros;
    }

    /// <summary>
    /// FR-MKP-014 / TR-MKP-XFER-004: rejects folder-share RPCs when the caller IP is not
    /// in the UsbConnectedPc + PairedHost allowlist. When no allowlist is configured
    /// (tests), access is not IP-gated.
    /// </summary>
    private bool TryAuthorizeShareClient(ServerCallContext context, out string err, out string msg)
    {
        err = string.Empty;
        msg = string.Empty;
        if (_shareAllowlist is null)
        {
            return true;
        }

        var ip = context.Peer;
        // gRPC peer looks like "ipv4:192.168.1.10:54321" or "ipv6:[::1]:54321"
        var host = ExtractPeerHost(ip);
        if (_shareAllowlist.IsIpAllowed(host))
        {
            return true;
        }

        err = "SHARE_IP_DENIED";
        msg = $"Folder share is only available to the paired host and USB-connected PC (caller={host}).";
        _logger.LogWarning("Folder share denied for peer {Peer}", ip);
        return false;
    }

    private static string? ExtractPeerHost(string? peer)
    {
        if (string.IsNullOrWhiteSpace(peer))
        {
            return null;
        }

        var p = peer.Trim();
        if (p.StartsWith("ipv4:", StringComparison.OrdinalIgnoreCase))
        {
            p = p["ipv4:".Length..];
            var colon = p.LastIndexOf(':');
            return colon > 0 ? p[..colon] : p;
        }

        if (p.StartsWith("ipv6:", StringComparison.OrdinalIgnoreCase))
        {
            p = p["ipv6:".Length..];
            if (p.StartsWith('[') && p.Contains(']', StringComparison.Ordinal))
            {
                var end = p.IndexOf(']', StringComparison.Ordinal);
                return p[1..end];
            }
        }

        return p;
    }

    /// <summary>
    /// TR-MKP-SEC-001: mints a time-bound, single-use pairing code held in the paired-peer store.
    /// This is a bootstrap RPC (allowed before a credential exists); the operator relays the code
    /// out of band to the peer being paired.
    /// </summary>
    public override Task<RequestPairingCodeResponse> RequestPairingCode(RequestPairingCodeRequest request, ServerCallContext context)
    {
        var ttl = request.TtlSeconds > 0 ? TimeSpan.FromSeconds(request.TtlSeconds) : DefaultPairingCodeTtl;
        // FR-MKP-023: prefer ClientPairingCodeIssuer (typed on connecting machine); mirror into store for legacy.
        string code;
        if (_clientPairingCodes is not null)
        {
            code = _clientPairingCodes.IssueCode(ttl);
        }
        else
        {
            code = _pairedPeerStore.IssuePairingCode(ttl);
        }

        _logger.LogInformation("Issued pairing code valid for {TtlSeconds}s", (int)ttl.TotalSeconds);

        return Task.FromResult(new RequestPairingCodeResponse
        {
            Success = true,
            PairingCode = code,
            TtlSeconds = (int)ttl.TotalSeconds,
        });
    }

    /// <summary>
    /// TR-MKP-SEC-001 / FR-MKP-023: validates and consumes a single-use pairing code (or ToFU for
    /// the first USB peer only), issues a client cert, registers the peer, and updates the
    /// share/SMB IP allowlist (UsbConnectedPc vs PairedHost).
    /// </summary>
    public override Task<PairResponse> Pair(PairRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Pair request received from PeerId={PeerId}, ProtocolVersion={ProtocolVersion}",
            request.PeerId, request.ProtocolVersion);

        if (string.IsNullOrWhiteSpace(request.PeerId))
        {
            return Fail(request.PeerId, "MISSING_PEER_ID");
        }

        if (request.PublicInfo is null || request.PublicInfo.IsEmpty)
        {
            return Fail(request.PeerId, "MISSING_PUBLIC_KEY");
        }

        // ToFU only for the empty store (first USB-connected PC). Further clients need OTP or install ticket.
        var tofuAccept = _trustOnFirstUse && !_pairedPeerStore.HasPairedPeer();
        PeerShareRole role;
        if (tofuAccept)
        {
            role = PeerShareRole.UsbConnectedPc;
            _logger.LogWarning("Pair accepted via trust-on-first-use (USB host bootstrap) for PeerId={PeerId}", request.PeerId);
        }
        else
        {
            role = PeerShareRole.PairedHost;
            var codeOk = false;
            string reject = "INVALID_OR_EXPIRED_CODE";
            string ticketErr = string.Empty;

            // FR-MKP-025: MSI install ticket (multi-use until expiry) for USB clients after host ToFU closed.
            if (_installTickets is not null &&
                _installTickets.TryValidate(request.PairingCode, out ticketErr))
            {
                codeOk = true;
                role = PeerShareRole.UsbConnectedPc;
                _logger.LogInformation("Pair accepted via install ticket for PeerId={PeerId}", request.PeerId);
            }
            else if (_clientPairingCodes is not null)
            {
                if (_clientPairingCodes.TryConsume(request.PairingCode, out var otpErr, out _))
                {
                    codeOk = true;
                    _logger.LogInformation("Pair code accepted via ClientPairingCodeIssuer for PeerId={PeerId}", request.PeerId);
                }
                else
                {
                    reject = string.IsNullOrEmpty(otpErr) ? reject : otpErr;
                    if (!string.IsNullOrEmpty(ticketErr) && ticketErr != "INSTALL_TICKET_REQUIRED")
                    {
                        reject = ticketErr;
                    }
                }
            }
            else if (_pairedPeerStore.TryConsumePairingCode(request.PairingCode))
            {
                codeOk = true;
            }

            if (!codeOk)
            {
                _logger.LogWarning("Pair code rejected for PeerId={PeerId}: {Err}", request.PeerId, reject);
                return Fail(request.PeerId, "INVALID_OR_EXPIRED_CODE");
            }
        }

        X509Certificate2 peerCert;
        try
        {
            peerCert = _certificateAuthority.IssuePeerCertificate(
                request.PeerId, request.PublicInfo.ToByteArray(), PeerCertValidity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pair failed for PeerId={PeerId}: BAD_PUBLIC_KEY", request.PeerId);
            return Fail(request.PeerId, "BAD_PUBLIC_KEY");
        }

        _pairedPeerStore.RegisterPeer(request.PeerId, peerCert.Thumbprint);
        var remoteIp = ExtractPeerHost(context.Peer);
        if (_shareAllowlist is not null)
        {
            _shareAllowlist.SetPeer(request.PeerId, role, remoteIp);
            _ = RefreshSmbAllowlistAsync();
        }

        _logger.LogInformation(
            "Pair succeeded for PeerId={PeerId}, role={Role}, ip={Ip}, thumbprint={Thumbprint}",
            request.PeerId, role, remoteIp, peerCert.Thumbprint);

        var response = new PairResponse
        {
            Success = true,
            PeerCert = ByteString.CopyFrom(peerCert.Export(X509ContentType.Cert)),
            CaCertificate = ByteString.CopyFrom(_certificateAuthority.CaCertificate.Export(X509ContentType.Cert)),
        };
        peerCert.Dispose();
        return Task.FromResult(response);
    }

    private async Task RefreshSmbAllowlistAsync()
    {
        if (_smb is null || _shareAllowlist is null)
        {
            return;
        }

        try
        {
            await _smb.ApplyAllowlistAsync(_shareAllowlist.GetAllowedIps()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply SMB allowlist after pair");
        }
    }

    private Task<PairResponse> Fail(string peerId, string error)
    {
        _logger.LogWarning("Pair failed for PeerId={PeerId}: {Error}", peerId, error);
        return Task.FromResult(new PairResponse { Success = false, Error = error });
    }

    /// <summary>
    /// FR-MKP-026: USB client MSI bootstrap queues a clipboard intro for the already-paired control host.
    /// </summary>
    public override Task<CompleteClientInstallResponse> CompleteClientInstall(
        CompleteClientInstallRequest request,
        ServerCallContext context)
    {
        if (_clientIntros is null)
        {
            return Task.FromResult(new CompleteClientInstallResponse
            {
                Ok = false,
                Err = "INTRO_UNSUPPORTED",
                Msg = "Client install intro mailbox is not configured on this host.",
            });
        }

        var peerId = string.IsNullOrWhiteSpace(request.PeerId)
            ? ResolveCallerPeerId(context) ?? string.Empty
            : request.PeerId.Trim();
        if (string.IsNullOrWhiteSpace(peerId))
        {
            return Task.FromResult(new CompleteClientInstallResponse
            {
                Ok = false,
                Err = "MISSING_PEER_ID",
                Msg = "peerId is required.",
            });
        }

        if (string.IsNullOrWhiteSpace(request.ClipboardEndpoint) ||
            string.IsNullOrWhiteSpace(request.ClipboardIntroCode))
        {
            return Task.FromResult(new CompleteClientInstallResponse
            {
                Ok = false,
                Err = "MISSING_CLIPBOARD_INTRO",
                Msg = "clipboardEndpoint and clipboardIntroCode are required.",
            });
        }

        var ttl = request.IntroTtlSeconds > 0
            ? TimeSpan.FromSeconds(request.IntroTtlSeconds)
            : DefaultIntroTtl;
        if (ttl < TimeSpan.FromSeconds(30))
        {
            ttl = TimeSpan.FromSeconds(30);
        }

        if (ttl > TimeSpan.FromHours(1))
        {
            ttl = TimeSpan.FromHours(1);
        }

        var now = DateTimeOffset.UtcNow;
        _clientIntros.Queue(new ClientInstallIntro(
            peerId,
            request.ClipboardEndpoint.Trim(),
            request.ClipboardIntroCode.Trim(),
            now,
            now.Add(ttl)));

        var hostPeerId = FindControlHostPeerId() ?? string.Empty;
        _logger.LogInformation(
            "CompleteClientInstall queued clipboard intro for client={Client} host={Host}",
            peerId, hostPeerId);

        return Task.FromResult(new CompleteClientInstallResponse
        {
            Ok = true,
            Msg = string.IsNullOrEmpty(hostPeerId)
                ? "Clipboard intro queued (no control host paired yet)."
                : "Clipboard intro queued for control host.",
            HostPeerId = hostPeerId,
            ClipboardIntroQueued = true,
        });
    }

    /// <summary>
    /// FR-MKP-026: control host lists pending USB-client clipboard intros.
    /// </summary>
    public override Task<GetPendingClientIntrosResponse> GetPendingClientIntros(
        GetPendingClientIntrosRequest request,
        ServerCallContext context)
    {
        if (_clientIntros is null)
        {
            return Task.FromResult(new GetPendingClientIntrosResponse
            {
                Ok = false,
                Err = "INTRO_UNSUPPORTED",
                Msg = "Client install intro mailbox is not configured on this host.",
            });
        }

        var response = new GetPendingClientIntrosResponse { Ok = true, Msg = "ok" };
        foreach (var intro in _clientIntros.PeekPending())
        {
            response.Intros.Add(ToWireIntro(intro));
        }

        return Task.FromResult(response);
    }

    /// <summary>
    /// FR-MKP-026: control host claims one pending intro (single-use).
    /// </summary>
    public override Task<ClaimClientIntroResponse> ClaimClientIntro(
        ClaimClientIntroRequest request,
        ServerCallContext context)
    {
        if (_clientIntros is null)
        {
            return Task.FromResult(new ClaimClientIntroResponse
            {
                Ok = false,
                Err = "INTRO_UNSUPPORTED",
                Msg = "Client install intro mailbox is not configured on this host.",
            });
        }

        if (string.IsNullOrWhiteSpace(request.ClientPeerId))
        {
            return Task.FromResult(new ClaimClientIntroResponse
            {
                Ok = false,
                Err = "MISSING_PEER_ID",
                Msg = "clientPeerId is required.",
            });
        }

        var claimed = _clientIntros.Claim(request.ClientPeerId);
        if (claimed is null)
        {
            return Task.FromResult(new ClaimClientIntroResponse
            {
                Ok = false,
                Err = "INTRO_NOT_FOUND",
                Msg = "No pending intro for that client (missing or already claimed).",
            });
        }

        _logger.LogInformation("ClaimClientIntro client={Client}", claimed.ClientPeerId);
        return Task.FromResult(new ClaimClientIntroResponse
        {
            Ok = true,
            Msg = "claimed",
            Intro = ToWireIntro(claimed),
        });
    }

    private static PendingClientIntro ToWireIntro(ClientInstallIntro intro) => new()
    {
        ClientPeerId = intro.ClientPeerId,
        ClipboardEndpoint = intro.ClipboardEndpoint,
        ClipboardIntroCode = intro.ClipboardIntroCode,
        ExpiresUnixTimeSeconds = intro.ExpiresUtc.ToUnixTimeSeconds(),
    };

    private string? FindControlHostPeerId()
    {
        // First non-revoked peer is treated as control host (lab: LEGION2 paired first).
        foreach (var peer in _pairedPeerStore.ExportPeers())
        {
            if (!peer.Revoked)
            {
                return peer.PeerId;
            }
        }

        return null;
    }

    private string? ResolveCallerPeerId(ServerCallContext context)
    {
        var cert = context.GetHttpContext().Connection.ClientCertificate;
        if (cert is null)
        {
            return null;
        }

        return _pairedPeerStore.FindByThumbprint(cert.Thumbprint)?.PeerId;
    }

    /// <summary>
    /// FR-MKP-023: revoke the calling peer (or all peers when <c>clearAll</c>) so the device can
    /// re-open ToFU pairing and share allowlist entries can be removed.
    /// </summary>
    public override Task<CommandResult> Unpair(UnpairRequest request, ServerCallContext context)
    {
        var cert = context.GetHttpContext().Connection.ClientCertificate;
        if (cert is null)
        {
            return Task.FromResult(new CommandResult
            {
                Ok = false,
                Err = "NO_CLIENT_CERTIFICATE",
                Msg = "Unpair requires a paired client certificate.",
            });
        }

        if (request.ClearAll)
        {
            var n = _pairedPeerStore.RevokeAll();
            _shareAllowlist?.GetAllowedIps(); // refresh path below
            // Drop all share allowlist entries by reconstructing from remaining authorized peers (none).
            if (_shareAllowlist is not null)
            {
                // Remove known roles by clearing via RemovePeer for requested ids if we tracked them;
                // best-effort: set no peers by removing peerId from request when present.
                if (!string.IsNullOrWhiteSpace(request.PeerId))
                {
                    _shareAllowlist.RemovePeer(request.PeerId);
                }
            }

            _ = RefreshSmbAllowlistAsync();
            _logger.LogWarning("Unpair clearAll revoked {Count} peers (caller thumbprint={Thumb})", n, cert.Thumbprint);
            return Task.FromResult(new CommandResult
            {
                Ok = true,
                Msg = n == 0 ? "No active peers to revoke." : $"Revoked {n} peer(s). ToFU pairing is available again if none remain.",
            });
        }

        var self = _pairedPeerStore.FindByThumbprint(cert.Thumbprint);
        // Prefer self (from client cert). Explicit peerId may only match self.
        var targetId = self?.PeerId;
        if (!string.IsNullOrWhiteSpace(request.PeerId))
        {
            var requested = request.PeerId.Trim();
            if (self is not null &&
                !string.Equals(self.PeerId, requested, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CommandResult
                {
                    Ok = false,
                    Err = "UNPAIR_SELF_ONLY",
                    Msg = "Clients may only unpair themselves (use clearAll to reset the device pairing store).",
                });
            }

            targetId ??= requested;
        }

        if (string.IsNullOrWhiteSpace(targetId))
        {
            return Task.FromResult(new CommandResult
            {
                Ok = false,
                Err = "PEER_UNKNOWN",
                Msg = "Could not resolve peer id for unpair (certificate not in store).",
            });
        }

        var revoked = _pairedPeerStore.Revoke(targetId);
        _shareAllowlist?.RemovePeer(targetId);
        _ = RefreshSmbAllowlistAsync();
        _logger.LogInformation("Unpair peerId={PeerId} revoked={Revoked}", targetId, revoked);
        return Task.FromResult(new CommandResult
        {
            Ok = true,
            Msg = revoked
                ? $"Peer '{targetId}' unpaired on device."
                : $"Peer '{targetId}' was not an active pairing (already clear).",
        });
    }

    public override async Task OpenSession(
        IAsyncStreamReader<SessionFrame> requestStream,
        IServerStreamWriter<SessionFrame> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("OpenSession started (remote connected)");

        try
        {
            await foreach (var frame in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (frame.Input != null && frame.Input.Events.Count > 0)
                {
                    _logger.LogDebug("Received InputBatch with {Count} events, baseSeq={BaseSeq}",
                        frame.Input.Events.Count, frame.Input.BaseSeq);
                    if (_dispatcher != null)
                    {
                        var evts = new List<MouseKeyProxy.Common.InputEvent>();
                        foreach (var we in frame.Input.Events)
                        {
                            evts.Add(ToCommonInputEvent(we));
                        }
                        await _dispatcher.HandleInputBatchAsync(evts, context.CancellationToken);
                    }
                }
                else if (frame.Control?.Mods is not null)
                {
                    _logger.LogInformation("Received ModResync with {Count} key-up events", frame.Control.Mods.Ups.Count);
                    if (_dispatcher != null)
                    {
                        await _dispatcher.HandleModifierResyncAsync(frame.Control.Mods.Ups, context.CancellationToken);
                    }
                }
                else if (frame.Control?.Hello is not null)
                {
                    var clientVersion = frame.Control.Hello.MyVer;
                    _logger.LogInformation("Received VersionHello myVer={MyVer} peerVer={PeerVer}",
                        clientVersion, frame.Control.Hello.PeerVer);

                    var mismatch = VersionHandshake.CheckCompatibility(VersionHandshake.CurrentVersion, clientVersion);
                    if (mismatch is not null)
                    {
                        _logger.LogWarning("OpenSession rejected: {Mismatch}", mismatch);
                        throw new RpcException(new Status(StatusCode.FailedPrecondition, mismatch));
                    }
                }
                else if (frame.Control?.Toggle is not null)
                {
                    _logger.LogInformation("Received Toggle active={Active} seq={Seq}", frame.Control.Toggle.Active, frame.Control.Seq);
                    _dispatcher?.HandleToggle(context.Peer);
                }
                else if (frame.Clipboard?.Entry != null)
                {
                    _logger.LogInformation("Received ClipboardPush seq={Seq} from source={Source}",
                        frame.Clipboard.Seq, frame.Clipboard.Entry.Source);
                    if (_dispatcher != null)
                    {
                        var entry = ToCommonClipboardEntry(frame.Clipboard.Entry, frame.Clipboard.Seq);
                        await _dispatcher.HandleClipboardAsync(entry, context.CancellationToken);
                    }
                }

                var ack = new SessionFrame { Seq = frame.Seq, Ack = new Ack { Last = frame.Seq } };
                await responseStream.WriteAsync(ack);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSession terminated with error");
            throw;
        }
        finally
        {
            // TR-MKP-RELI-001: a remote that drops mid-hold must not leave modifiers stuck down on
            // the receiving host. Clear them on session teardown regardless of how the stream ended.
            if (_dispatcher is not null)
            {
                try
                {
                    await _dispatcher.ClearModifiersAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OpenSession teardown modifier clear failed");
                }
            }

            _logger.LogInformation("OpenSession ended (remote disconnected)");
        }
    }

    // AC-5: full overrides for all advanced controls (use CommandResult where defined in proto)
    public override Task<CommandResult> SetMousePosition(SetMousePositionRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "SetMousePosition peerId={PeerId} displayId={DisplayId} x={X} y={Y} correlationId={CorrelationId}",
            request.PeerId,
            request.DisplayId,
            request.X,
            request.Y,
            request.CorrelationId);

        if (_desktopController is null)
        {
            _logger.LogWarning("SetMousePosition failed: {ErrorCode}", AgentIpcUnavailable);
            return Task.FromResult(UnavailableResult());
        }

        var result = _desktopController.SetMousePosition(request.DisplayId, request.X, request.Y);
        return Task.FromResult(ToCommandResult(result));
    }

    public override Task<LocateProcessResponse> LocateProcess(LocateProcessRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "LocateProcess peerId={PeerId} processName={ProcessName} pid={Pid}",
            request.PeerId,
            request.ProcessName,
            request.Pid);

        if (_desktopController is null)
        {
            _logger.LogWarning("LocateProcess failed: {ErrorCode}", AgentIpcUnavailable);
            return Task.FromResult(new LocateProcessResponse
            {
                ErrorCode = AgentIpcUnavailable
            });
        }

        var response = new LocateProcessResponse { ErrorCode = "0" };
        foreach (var node in _desktopController.LocateProcess(request.ProcessName, request.Pid))
        {
            response.Nodes.Add(ToHwndNode(node));
        }

        return Task.FromResult(response);
    }

    public override Task<CommandResult> SetFocusByHwnd(SetFocusByHwndRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "SetFocusByHwnd peerId={PeerId} hwnd={Hwnd} bringToFront={BringToFront} correlationId={CorrelationId}",
            request.PeerId,
            request.Hwnd,
            request.BringToFront,
            request.CorrelationId);

        if (_desktopController is null)
        {
            _logger.LogWarning("SetFocusByHwnd failed: {ErrorCode}", AgentIpcUnavailable);
            return Task.FromResult(UnavailableResult());
        }

        var result = _desktopController.SetFocusByHwnd(request.Hwnd, request.BringToFront);
        return Task.FromResult(ToCommandResult(result));
    }

    public override async Task<CommandResult> InjectInput(InjectInputRequest request, ServerCallContext context)
    {
        _logger.LogInformation("InjectInput events={C} (real dispatch path for AC5)", request.Events?.Count ?? 0);
        if (_dispatcher != null && request.Events != null && request.Events.Count > 0)
        {
            var evts = new List<MouseKeyProxy.Common.InputEvent>();
            foreach (var we in request.Events)
            {
                evts.Add(ToCommonInputEvent(we));
            }
            await _dispatcher.HandleInputBatchAsync(evts, context.CancellationToken);
        }
        return new CommandResult { Ok = true, Msg = "ok" };
    }

    public override async Task<CommandResult> ClearModifiers(ClearModifiersRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "ClearModifiers peerId={PeerId} correlationId={CorrelationId}",
            request.PeerId,
            request.CorrelationId);

        if (_modifierReleaseController is not null)
        {
            return ToCommandResult(_modifierReleaseController.ClearModifiers(request.PeerId, request.CorrelationId));
        }

        if (_dispatcher is not null)
        {
            await _dispatcher.ClearModifiersAsync(context.CancellationToken);
            return new CommandResult { Ok = true, Err = "0", Msg = "modifiers cleared" };
        }

        _logger.LogWarning("ClearModifiers failed: {ErrorCode}", AgentIpcUnavailable);
        return UnavailableResult();
    }

    public override async Task CaptureScreenshot(
        CaptureScreenshotRequest request,
        IServerStreamWriter<ScreenshotChunk> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "CaptureScreenshot peerId={PeerId} target={Target} hwnd={Hwnd} correlationId={CorrelationId}",
            request.PeerId,
            request.Target,
            request.Hwnd,
            request.CorrelationId);

        if (_screenshotCapture is null)
        {
            _logger.LogWarning("CaptureScreenshot failed: {ErrorCode}", AgentIpcUnavailable);
            throw new RpcException(new Status(StatusCode.Unavailable, "Screenshot capture is not configured."));
        }

        var capture = _screenshotCapture.Capture(new ScreenshotCaptureRequest(
            ToCommonScreenshotTarget(request.Target),
            request.Hwnd,
            string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId,
            request.IncludeCursor));

        var metadata = ToWireMetadata(capture.Metadata);
        var offset = 0;
        var index = 0u;
        while (offset < capture.Png.Length)
        {
            var count = Math.Min(ScreenshotChunkSize, capture.Png.Length - offset);
            var chunk = new ScreenshotChunk
            {
                Index = index++,
                Last = offset + count >= capture.Png.Length,
                Metadata = offset == 0 ? metadata : null,
                Data = Google.Protobuf.ByteString.CopyFrom(capture.Png, offset, count)
            };
            await responseStream.WriteAsync(chunk);
            offset += count;
        }

        if (capture.Png.Length == 0)
        {
            await responseStream.WriteAsync(new ScreenshotChunk { Index = 0, Last = true, Metadata = metadata });
        }
    }

    public override Task<CommandResult> EmergencyRelease(
        EmergencyReleaseRequest request,
        ServerCallContext context)
    {
        _logger.LogWarning(
            "EmergencyRelease requested by PeerId={PeerId}, correlationId={CorrelationId}",
            request.PeerId,
            request.CorrelationId);
        if (_emergencyReleaseController is null)
        {
            _logger.LogWarning("EmergencyRelease failed: {ErrorCode}", AgentIpcUnavailable);
            return Task.FromResult(UnavailableResult());
        }

        var result = _emergencyReleaseController.EmergencyRelease(request.PeerId, request.CorrelationId);
        return Task.FromResult(ToCommandResult(result));
    }

    /// <summary>
    /// Linux/Pi appliance only: safely powers off or reboots the device. On non-Linux hosts the
    /// injected power controller reports PLATFORM_NOT_SUPPORTED, so the Windows service never
    /// powers down the machine.
    /// </summary>
    public override Task<CommandResult> Shutdown(
        ShutdownRequest request,
        ServerCallContext context)
    {
        var reboot = request.Mode == ShutdownMode.Reboot;
        _logger.LogWarning(
            "Shutdown requested by PeerId={PeerId}, mode={Mode}, correlationId={CorrelationId}",
            request.PeerId,
            request.Mode,
            request.CorrelationId);

        var result = _powerController.Trigger(reboot);
        if (result.Ok)
        {
            _logger.LogInformation("Shutdown initiated ({Action})", reboot ? "reboot" : "poweroff");
        }
        else
        {
            _logger.LogWarning("Shutdown not performed: {Error}", result.Error);
        }

        return Task.FromResult(new CommandResult
        {
            Ok = result.Ok,
            Err = result.Ok ? string.Empty : result.Error,
            Msg = result.Ok ? (reboot ? "rebooting" : "powering off") : string.Empty
        });
    }

    /// <summary>
    /// FR-MKP-013: enable/disable keyboard, mouse, and mass-storage FS independently; set FS RO/RW.
    /// Events are processed locally via <see cref="DeviceFunctionCoordinator"/> and mirrored to the host.
    /// </summary>
    public override async Task<ConfigureDeviceResponse> ConfigureDevice(
        ConfigureDeviceRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "ConfigureDevice peerId={PeerId} correlationId={CorrelationId} kb={Kb} mouse={Mouse} fs={Fs} fsAccess={FsAccess} cdrom={Cd} floppy={Fl}",
            request.PeerId,
            request.CorrelationId,
            request.HasKeyboardEnabled ? request.KeyboardEnabled.ToString() : "unchanged",
            request.HasMouseEnabled ? request.MouseEnabled.ToString() : "unchanged",
            request.HasFsEnabled ? request.FsEnabled.ToString() : "unchanged",
            request.FsAccess,
            request.HasCdromEnabled ? request.CdromEnabled.ToString() : "unchanged",
            request.HasFloppyEnabled ? request.FloppyEnabled.ToString() : "unchanged");

        if (_deviceFunctions is null)
        {
            return new ConfigureDeviceResponse
            {
                Ok = false,
                Err = "PLATFORM_NOT_SUPPORTED",
                Msg = "Device function configuration is not available on this host.",
                State = ToWireState(new DeviceFunctionState(true, true, false, DeviceFsAccess.ReadOnly)),
            };
        }

        var config = new DeviceFunctionConfig(
            KeyboardEnabled: request.HasKeyboardEnabled ? request.KeyboardEnabled : null,
            MouseEnabled: request.HasMouseEnabled ? request.MouseEnabled : null,
            FsEnabled: request.HasFsEnabled ? request.FsEnabled : null,
            FsAccess: request.FsAccess switch
            {
                DeviceFsAccessMode.FsReadOnly => DeviceFsAccess.ReadOnly,
                DeviceFsAccessMode.FsReadWrite => DeviceFsAccess.ReadWrite,
                _ => null,
            },
            CdromEnabled: request.HasCdromEnabled ? request.CdromEnabled : null,
            CdromMedia: request.HasUpdateCdromMedia ? FromWireMedia(request.CdromMedia) : null,
            FloppyEnabled: request.HasFloppyEnabled ? request.FloppyEnabled : null,
            FloppyMedia: request.HasUpdateFloppyMedia ? FromWireMedia(request.FloppyMedia) : null);

        var result = await _deviceFunctions.ConfigureAsync(config, context.CancellationToken).ConfigureAwait(false);
        var response = new ConfigureDeviceResponse
        {
            Ok = result.Ok,
            Err = result.ErrorCode,
            Msg = result.Message,
            State = ToWireState(result.State),
        };
        foreach (var e in result.EmittedEvents)
        {
            response.Events.Add(ToWireEvent(e));
        }

        return response;
    }

    /// <summary>FR-MKP-013: returns the current keyboard/mouse/FS enablement and FS access mode.</summary>
    public override Task<GetDeviceConfigurationResponse> GetDeviceConfiguration(
        GetDeviceConfigurationRequest request,
        ServerCallContext context)
    {
        if (_deviceFunctions is null)
        {
            return Task.FromResult(new GetDeviceConfigurationResponse
            {
                Ok = false,
                Err = "PLATFORM_NOT_SUPPORTED",
                Msg = "Device function configuration is not available on this host.",
            });
        }

        return Task.FromResult(new GetDeviceConfigurationResponse
        {
            Ok = true,
            Msg = "ok",
            State = ToWireState(_deviceFunctions.State),
        });
    }

    private static DeviceFunctionStateMsg ToWireState(DeviceFunctionState state)
    {
        var msg = new DeviceFunctionStateMsg
        {
            KeyboardEnabled = state.KeyboardEnabled,
            MouseEnabled = state.MouseEnabled,
            FsEnabled = state.FsEnabled,
            FsAccess = state.FsAccess == DeviceFsAccess.ReadWrite
                ? DeviceFsAccessMode.FsReadWrite
                : DeviceFsAccessMode.FsReadOnly,
            CdromEnabled = state.CdromEnabled,
            FloppyEnabled = state.FloppyEnabled,
        };
        if (state.CdromMedia is not null)
        {
            msg.CdromMedia = ToWireMedia(state.CdromMedia);
        }

        if (state.FloppyMedia is not null)
        {
            msg.FloppyMedia = ToWireMedia(state.FloppyMedia);
        }

        return msg;
    }

    private static StorageMediaSpec FromWireMedia(StorageMediaSpecMsg? wire)
    {
        if (wire is null)
        {
            return new StorageMediaSpec(Common.DeviceMediaSource.Device, string.Empty);
        }

        return new StorageMediaSpec(
            wire.Source == global::MouseKeyProxy.Network.V1.DeviceMediaSource.MediaSourceHost
                ? Common.DeviceMediaSource.Host
                : Common.DeviceMediaSource.Device,
            wire.Path ?? string.Empty);
    }

    private static StorageMediaSpecMsg ToWireMedia(StorageMediaSpec media)
        => new()
        {
            Source = media.Source == Common.DeviceMediaSource.Host
                ? global::MouseKeyProxy.Network.V1.DeviceMediaSource.MediaSourceHost
                : global::MouseKeyProxy.Network.V1.DeviceMediaSource.MediaSourceDevice,
            Path = media.Path ?? string.Empty,
        };

    private static DeviceEventMsg ToWireEvent(DeviceEvent e)
        => new()
        {
            Kind = (global::MouseKeyProxy.Network.V1.DeviceEventKind)(int)e.Kind,
            AtUnixMs = e.AtUtc.ToUnixTimeMilliseconds(),
            CorrelationId = e.CorrelationId.ToString("N"),
            Detail = e.Detail ?? string.Empty,
            Path = e.Path ?? string.Empty,
            OldPath = e.OldPath ?? string.Empty,
        };

    /// <summary>FR-MKP-014: folder share metadata for discovery/connect.</summary>
    public override Task<GetFolderShareInfoResponse> GetFolderShareInfo(
        GetFolderShareInfoRequest request,
        ServerCallContext context)
    {
        if (_folderShare is null)
        {
            return Task.FromResult(new GetFolderShareInfoResponse
            {
                Ok = false,
                Err = "SHARE_DISABLED",
                Msg = "Folder share is not enabled on this device.",
            });
        }

        if (!TryAuthorizeShareClient(context, out var ipErr, out var ipMsg))
        {
            return Task.FromResult(new GetFolderShareInfoResponse
            {
                Ok = false,
                Err = ipErr,
                Msg = ipMsg,
                Enabled = false,
            });
        }

        var info = _folderShare.GetInfo();
        return Task.FromResult(new GetFolderShareInfoResponse
        {
            Ok = info.Enabled,
            Err = info.Enabled ? string.Empty : "SHARE_DISABLED",
            Msg = info.Enabled ? "ok" : "Folder share is not enabled on this device.",
            ShareName = info.ShareName,
            RootLabel = info.RootLabel,
            ReadWrite = info.ReadWrite,
            Enabled = info.Enabled,
        });
    }

    /// <summary>FR-MKP-014: list a relative directory under the device folder share.</summary>
    public override Task<ListFolderShareResponse> ListFolderShare(
        ListFolderShareRequest request,
        ServerCallContext context)
    {
        if (_folderShare is null)
        {
            return Task.FromResult(new ListFolderShareResponse
            {
                Ok = false,
                Err = "SHARE_DISABLED",
                Msg = "Folder share is not enabled on this device.",
            });
        }

        if (!TryAuthorizeShareClient(context, out var ipErr, out var ipMsg))
        {
            return Task.FromResult(new ListFolderShareResponse
            {
                Ok = false,
                Err = ipErr,
                Msg = ipMsg,
            });
        }

        var result = _folderShare.List(request.RelativeDirectory ?? string.Empty, out var entries);
        var response = new ListFolderShareResponse
        {
            Ok = result.Ok,
            Err = result.ErrorCode,
            Msg = result.Message,
        };
        if (result.Ok)
        {
            foreach (var e in entries)
            {
                response.Entries.Add(new FolderShareEntryMsg
                {
                    Name = e.Name,
                    RelativePath = e.RelativePath,
                    IsDirectory = e.IsDirectory,
                    SizeBytes = e.SizeBytes,
                    ModifiedUnixMs = e.ModifiedUtc.ToUnixTimeMilliseconds(),
                });
            }
        }

        return Task.FromResult(response);
    }

    /// <summary>FR-MKP-014: download a file from the device folder share.</summary>
    public override async Task DownloadFolderShareFile(
        DownloadFolderShareFileRequest request,
        IServerStreamWriter<FolderShareChunk> responseStream,
        ServerCallContext context)
    {
        if (_folderShare is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "SHARE_DISABLED"));
        }

        if (!TryAuthorizeShareClient(context, out var ipErr, out var ipMsg))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"{ipErr}: {ipMsg}"));
        }

        var open = _folderShare.OpenRead(request.RelativePath ?? string.Empty, out var stream, out var length);
        if (!open.Ok || stream is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"{open.ErrorCode}: {open.Message}"));
        }

        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[64 * 1024];
            uint index = 0;
            long remaining = length;
            string? sha = null;
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                while (remaining > 0)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var toRead = (int)Math.Min(buffer.Length, remaining);
                    var n = await stream.ReadAsync(buffer.AsMemory(0, toRead), context.CancellationToken).ConfigureAwait(false);
                    if (n <= 0)
                    {
                        break;
                    }

                    hasher.TransformBlock(buffer, 0, n, null, 0);
                    remaining -= n;
                    var last = remaining <= 0;
                    if (last)
                    {
                        hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        sha = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
                    }

                    await responseStream.WriteAsync(new FolderShareChunk
                    {
                        Data = Google.Protobuf.ByteString.CopyFrom(buffer, 0, n),
                        Index = index++,
                        Last = last,
                        TotalSize = length,
                        Sha256 = last ? sha ?? string.Empty : string.Empty,
                    }).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>FR-MKP-014: upload a file into the device folder share.</summary>
    public override async Task<CommandResult> UploadFolderShareFile(
        IAsyncStreamReader<UploadFolderShareFileRequest> requestStream,
        ServerCallContext context)
    {
        if (_folderShare is null)
        {
            return new CommandResult { Ok = false, Err = "SHARE_DISABLED", Msg = "Folder share is not enabled." };
        }

        if (!TryAuthorizeShareClient(context, out var ipErr, out var ipMsg))
        {
            return new CommandResult { Ok = false, Err = ipErr, Msg = ipMsg };
        }

        string? relativePath = null;
        long totalSize = 0;
        Stream? writeStream = null;
        long written = 0;
        string? expectedSha = null;
        using var hasher = System.Security.Cryptography.SHA256.Create();

        try
        {
            await foreach (var msg in requestStream.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
            {
                if (relativePath is null)
                {
                    relativePath = msg.RelativePath ?? string.Empty;
                    totalSize = msg.TotalSize;
                    var open = _folderShare.OpenWrite(relativePath, totalSize, out writeStream);
                    if (!open.Ok || writeStream is null)
                    {
                        return new CommandResult { Ok = false, Err = open.ErrorCode, Msg = open.Message };
                    }
                }

                if (msg.Data is { Length: > 0 })
                {
                    var bytes = msg.Data.ToByteArray();
                    await writeStream!.WriteAsync(bytes, context.CancellationToken).ConfigureAwait(false);
                    hasher.TransformBlock(bytes, 0, bytes.Length, null, 0);
                    written += bytes.Length;
                }

                if (msg.Last)
                {
                    expectedSha = string.IsNullOrEmpty(msg.Sha256) ? null : msg.Sha256.ToLowerInvariant();
                    break;
                }
            }

            if (writeStream is null || relativePath is null)
            {
                return new CommandResult { Ok = false, Err = "EMPTY_UPLOAD", Msg = "No upload frames received." };
            }

            await writeStream.FlushAsync(context.CancellationToken).ConfigureAwait(false);
            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var actual = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
            if (expectedSha is not null && !string.Equals(expectedSha, actual, StringComparison.Ordinal))
            {
                return new CommandResult { Ok = false, Err = "HASH_MISMATCH", Msg = $"expected {expectedSha} actual {actual}" };
            }

            if (totalSize > 0 && written != totalSize)
            {
                return new CommandResult { Ok = false, Err = "SIZE_MISMATCH", Msg = $"expected {totalSize} wrote {written}" };
            }

            return new CommandResult { Ok = true, Msg = $"uploaded {relativePath} ({written} bytes)" };
        }
        finally
        {
            if (writeStream is not null)
            {
                await writeStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static CommandResult ToCommandResult(RemoteControlResult result)
    {
        return new CommandResult
        {
            Ok = result.Ok,
            Err = result.ErrorCode,
            Msg = result.Message
        };
    }

    private static CommandResult UnavailableResult()
    {
        return new CommandResult
        {
            Ok = false,
            Err = AgentIpcUnavailable,
            Msg = "Remote desktop controller is not configured."
        };
    }

    private static HwndNode ToHwndNode(RemoteWindowNode node)
    {
        var result = new HwndNode
        {
            Hwnd = node.Hwnd,
            Title = node.Title,
            ClassName = node.ClassName,
            ProcessId = node.ProcessId
        };

        foreach (var child in node.Children)
        {
            result.Children.Add(ToHwndNode(child));
        }

        return result;
    }

    private static MouseKeyProxy.Common.ClipboardEntry ToCommonClipboardEntry(global::MouseKeyProxy.Network.V1.ClipboardEntry wire, ulong pushSeq)
    {
        var formats = new List<MouseKeyProxy.Common.ClipboardFormat>();
        foreach (var f in wire.Formats)
        {
            formats.Add(new MouseKeyProxy.Common.ClipboardFormat(f.Name, f.Data.ToByteArray()));
        }

        var timestamp = wire.TsMs == 0
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.FromUnixTimeMilliseconds((long)wire.TsMs);

        return new MouseKeyProxy.Common.ClipboardEntry(wire.Id, timestamp, wire.Source, formats, pushSeq);
    }

    private static MouseKeyProxy.Common.InputEvent ToCommonInputEvent(global::MouseKeyProxy.Network.V1.InputEvent wireEvent)
    {
        return new MouseKeyProxy.Common.InputEvent(
            (MouseKeyProxy.Common.InputKind)wireEvent.Kind,
            Vk: wireEvent.Vk,
            Scan: wireEvent.Scan,
            Flags: wireEvent.Flags,
            Dx: wireEvent.Dx,
            Dy: wireEvent.Dy,
            WheelDelta: wireEvent.WheelDelta,
            XButton: wireEvent.Xbutton,
            Text: wireEvent.Text,
            TsMs: wireEvent.TsMs);
    }

    private static global::MouseKeyProxy.Network.V1.ScreenshotMetadata ToWireMetadata(MouseKeyProxy.Common.ScreenshotMetadata metadata)
    {
        return new global::MouseKeyProxy.Network.V1.ScreenshotMetadata
        {
            CapturedAtUtc = metadata.CapturedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            SourceHost = metadata.SourceHost,
            CorrelationId = metadata.CorrelationId,
            Target = ToWireScreenshotTarget(metadata.Target),
            Hwnd = metadata.Hwnd,
            Width = (uint)metadata.Width,
            Height = (uint)metadata.Height,
            Sha256 = metadata.Sha256
        };
    }

    private static CommonScreenshotTarget ToCommonScreenshotTarget(WireScreenshotTarget target)
    {
        return target switch
        {
            WireScreenshotTarget.Foreground => CommonScreenshotTarget.Foreground,
            WireScreenshotTarget.Hwnd => CommonScreenshotTarget.Hwnd,
            _ => CommonScreenshotTarget.Desktop
        };
    }

    private static WireScreenshotTarget ToWireScreenshotTarget(CommonScreenshotTarget target)
    {
        return target switch
        {
            CommonScreenshotTarget.Foreground => WireScreenshotTarget.Foreground,
            CommonScreenshotTarget.Hwnd => WireScreenshotTarget.Hwnd,
            _ => WireScreenshotTarget.Desktop
        };
    }
}