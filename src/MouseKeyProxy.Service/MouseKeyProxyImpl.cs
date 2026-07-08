using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
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

    public MouseKeyProxyImpl(
        ILogger<MouseKeyProxyImpl> logger,
        SessionFrameDispatcher? dispatcher = null,
        IRemoteDesktopController? desktopController = null,
        IEmergencyReleaseController? emergencyReleaseController = null,
        IModifierReleaseController? modifierReleaseController = null,
        IScreenshotCapture? screenshotCapture = null,
        ISystemPowerController? powerController = null,
        IPairedPeerStore? pairedPeerStore = null,
        IPairingCertificateAuthority? certificateAuthority = null)
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
    }

    /// <summary>
    /// TR-MKP-SEC-001: mints a time-bound, single-use pairing code held in the paired-peer store.
    /// This is a bootstrap RPC (allowed before a credential exists); the operator relays the code
    /// out of band to the peer being paired.
    /// </summary>
    public override Task<RequestPairingCodeResponse> RequestPairingCode(RequestPairingCodeRequest request, ServerCallContext context)
    {
        var ttl = request.TtlSeconds > 0 ? TimeSpan.FromSeconds(request.TtlSeconds) : DefaultPairingCodeTtl;
        var code = _pairedPeerStore.IssuePairingCode(ttl);
        _logger.LogInformation("Issued pairing code valid for {TtlSeconds}s", (int)ttl.TotalSeconds);

        return Task.FromResult(new RequestPairingCodeResponse
        {
            Success = true,
            PairingCode = code,
            TtlSeconds = (int)ttl.TotalSeconds,
        });
    }

    /// <summary>
    /// TR-MKP-SEC-001: validates and consumes a single-use pairing code, binds the peer-supplied
    /// public key to a service-signed client certificate, registers the peer by cert thumbprint,
    /// and returns the issued certificate plus the CA certificate the peer must trust.
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

        if (!_pairedPeerStore.TryConsumePairingCode(request.PairingCode))
        {
            return Fail(request.PeerId, "INVALID_OR_EXPIRED_CODE");
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
        _logger.LogInformation("Pair succeeded for PeerId={PeerId}, thumbprint={Thumbprint}",
            request.PeerId, peerCert.Thumbprint);

        var response = new PairResponse
        {
            Success = true,
            PeerCert = ByteString.CopyFrom(peerCert.Export(X509ContentType.Cert)),
            CaCertificate = ByteString.CopyFrom(_certificateAuthority.CaCertificate.Export(X509ContentType.Cert)),
        };
        peerCert.Dispose();
        return Task.FromResult(response);
    }

    private Task<PairResponse> Fail(string peerId, string error)
    {
        _logger.LogWarning("Pair failed for PeerId={PeerId}: {Error}", peerId, error);
        return Task.FromResult(new PairResponse { Success = false, Error = error });
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
                else if (frame.Clipboard != null)
                {
                    _logger.LogInformation("Received ClipboardPush seq={Seq} from source={Source}",
                        frame.Clipboard.Seq, frame.Clipboard.Entry?.Source);
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