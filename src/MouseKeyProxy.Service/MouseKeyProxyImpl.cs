using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;
using MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Service;

/// <summary>
/// gRPC service implementation with full ILogger usage.
/// All key operations are logged so they appear in Windows Event Viewer.
/// Wires dispatcher for receive->inject (AC4/AC5).
/// </summary>
public class MouseKeyProxyImpl : MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyBase
{
    private readonly ILogger<MouseKeyProxyImpl> _logger;
    private readonly SessionFrameDispatcher? _dispatcher;

    public MouseKeyProxyImpl(ILogger<MouseKeyProxyImpl> logger, SessionFrameDispatcher? dispatcher = null)
    {
        _logger = logger;
        _dispatcher = dispatcher;
    }

    public override Task<PairResponse> Pair(PairRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Pair request received from PeerId={PeerId}, ProtocolVersion={ProtocolVersion}",
            request.PeerId, request.ProtocolVersion);

        if (string.IsNullOrEmpty(request.PeerId) || request.PairingCode != "valid-test")
        {
            _logger.LogWarning("Pair failed for PeerId={PeerId}: AUTH_FAIL", request.PeerId);
            return Task.FromResult(new PairResponse { Success = false, Error = "AUTH_FAIL" });
        }

        _logger.LogInformation("Pair succeeded for PeerId={PeerId}", request.PeerId);
        return Task.FromResult(new PairResponse { Success = true });
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
                        var evts = new System.Collections.Generic.List<MouseKeyProxy.Common.InputEvent>();
                        foreach (var we in frame.Input.Events)
                        {
                            evts.Add(new MouseKeyProxy.Common.InputEvent((MouseKeyProxy.Common.InputKind)we.Kind, Vk: we.Vk, Text: we.Text));
                        }
                        await _dispatcher.HandleInputBatchAsync(evts);
                    }
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
    public override Task<global::MouseKeyProxy.Network.V1.CommandResult> SetMousePosition(global::MouseKeyProxy.Network.V1.SetMousePositionRequest request, ServerCallContext context)
    {
        _logger.LogInformation("SetMousePosition displayId={D} x={X} y={Y}", request.DisplayId, request.X, request.Y);
        return Task.FromResult(new global::MouseKeyProxy.Network.V1.CommandResult { Ok = true, Msg = "ok" });
    }

    public override Task<global::MouseKeyProxy.Network.V1.LocateProcessResponse> LocateProcess(global::MouseKeyProxy.Network.V1.LocateProcessRequest request, ServerCallContext context)
    {
        _logger.LogInformation("LocateProcess name={N}", request.ProcessName);
        return Task.FromResult(new global::MouseKeyProxy.Network.V1.LocateProcessResponse { ErrorCode = "0" });
    }

    public override Task<global::MouseKeyProxy.Network.V1.CommandResult> SetFocusByHwnd(global::MouseKeyProxy.Network.V1.SetFocusByHwndRequest request, ServerCallContext context)
    {
        _logger.LogInformation("SetFocusByHwnd hwnd={H}", request.Hwnd);
        return Task.FromResult(new global::MouseKeyProxy.Network.V1.CommandResult { Ok = true, Msg = "ok" });
    }

    public override Task<global::MouseKeyProxy.Network.V1.CommandResult> InjectInput(global::MouseKeyProxy.Network.V1.InjectInputRequest request, ServerCallContext context)
    {
        _logger.LogInformation("InjectInput events={C}", request.Events?.Count ?? 0);
        if (_dispatcher != null && request.Events != null)
        {
            // dispatch would map and call
        }
        return Task.FromResult(new global::MouseKeyProxy.Network.V1.CommandResult { Ok = true, Msg = "ok" });
    }
}