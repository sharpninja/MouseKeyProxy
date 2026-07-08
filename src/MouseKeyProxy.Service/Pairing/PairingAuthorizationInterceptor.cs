using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace MouseKeyProxy.Service.Pairing;

/// <summary>
/// TR-MKP-SEC-001: gRPC server interceptor that enforces pairing on every effect-bearing RPC.
/// It extracts the mTLS client certificate from the call and delegates to <see cref="PairingAuthorizer"/>;
/// pairing-bootstrap RPCs pass through, everything else is rejected with Unauthenticated unless the
/// caller presents a CA-issued certificate for a paired, non-revoked peer.
/// </summary>
public sealed class PairingAuthorizationInterceptor : Interceptor
{
    private readonly PairingAuthorizer _authorizer;
    private readonly ILogger<PairingAuthorizationInterceptor> _logger;

    /// <summary>Creates the interceptor over the pairing authorizer.</summary>
    /// <param name="authorizer">The authorization decision.</param>
    /// <param name="logger">Diagnostics logger.</param>
    public PairingAuthorizationInterceptor(PairingAuthorizer authorizer, ILogger<PairingAuthorizationInterceptor> logger)
    {
        _authorizer = authorizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Enforce(context);
        return continuation(request, context);
    }

    /// <inheritdoc />
    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enforce(context);
        return continuation(requestStream, context);
    }

    /// <inheritdoc />
    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enforce(context);
        return continuation(request, responseStream, context);
    }

    /// <inheritdoc />
    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enforce(context);
        return continuation(requestStream, responseStream, context);
    }

    private void Enforce(ServerCallContext context)
    {
        var method = BareMethod(context.Method);
        var clientCert = context.GetHttpContext().Connection.ClientCertificate;
        var denial = _authorizer.Authorize(method, clientCert);
        if (denial is not null)
        {
            _logger.LogWarning("Rejected unauthorized gRPC call {Method}: {Reason}", method, denial);
            throw new RpcException(new Status(StatusCode.Unauthenticated, denial));
        }
    }

    private static string BareMethod(string fullMethod)
    {
        var slash = fullMethod.LastIndexOf('/');
        return slash >= 0 ? fullMethod[(slash + 1)..] : fullMethod;
    }
}
