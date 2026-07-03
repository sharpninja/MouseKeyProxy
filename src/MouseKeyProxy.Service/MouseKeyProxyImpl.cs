using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Service;

/// <summary>
/// gRPC service implementation with full ILogger usage.
/// All key operations are logged so they appear in Windows Event Viewer.
/// </summary>
public class MouseKeyProxyImpl : MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyBase
{
    private readonly ILogger<MouseKeyProxyImpl> _logger;

    public MouseKeyProxyImpl(ILogger<MouseKeyProxyImpl> logger)
    {
        _logger = logger;
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
}