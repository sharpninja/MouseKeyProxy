using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using MouseKeyProxy.Common;
using MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Commands;

/// <summary>
/// FR-MKP-014: client for device folder share RPCs (used by REPL and Agent on either machine).
/// </summary>
public sealed class FolderShareClient
{
    private readonly MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient _client;
    private readonly string _peerId;

    /// <summary>Creates a client over an authenticated gRPC channel.</summary>
    public FolderShareClient(MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient client, string? peerId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _peerId = peerId ?? Environment.MachineName;
    }

    /// <summary>Fetches share metadata.</summary>
    public async Task<GetFolderShareInfoResponse> GetInfoAsync(CancellationToken ct = default)
    {
        return await _client.GetFolderShareInfoAsync(new GetFolderShareInfoRequest
        {
            ProtocolVersion = "v1",
            PeerId = _peerId,
            CorrelationId = Guid.NewGuid().ToString("n"),
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
    }

    /// <summary>Lists a relative directory (empty = root).</summary>
    public async Task<ListFolderShareResponse> ListAsync(string relativeDirectory = "", CancellationToken ct = default)
    {
        return await _client.ListFolderShareAsync(new ListFolderShareRequest
        {
            ProtocolVersion = "v1",
            PeerId = _peerId,
            CorrelationId = Guid.NewGuid().ToString("n"),
            RelativeDirectory = relativeDirectory ?? string.Empty,
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
    }

    /// <summary>Downloads a remote relative path to a local file.</summary>
    public async Task<RemoteControlResult> DownloadAsync(string remoteRelativePath, string localPath, CancellationToken ct = default)
    {
        using var call = _client.DownloadFolderShareFile(new DownloadFolderShareFileRequest
        {
            ProtocolVersion = "v1",
            PeerId = _peerId,
            CorrelationId = Guid.NewGuid().ToString("n"),
            RelativePath = remoteRelativePath ?? string.Empty,
        }, cancellationToken: ct);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(localPath))!);
        await using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
        long total = 0;
        string? sha = null;
        while (await call.ResponseStream.MoveNext(ct).ConfigureAwait(false))
        {
            var chunk = call.ResponseStream.Current;
            if (chunk.Data is { Length: > 0 })
            {
                var bytes = chunk.Data.ToByteArray();
                await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
                total += bytes.Length;
            }

            if (chunk.Last)
            {
                sha = chunk.Sha256;
                break;
            }
        }

        return RemoteControlResult.Success($"downloaded {total} bytes sha256={sha}");
    }

    /// <summary>Uploads a local file to a remote relative path.</summary>
    public async Task<RemoteControlResult> UploadAsync(string localPath, string remoteRelativePath, CancellationToken ct = default)
    {
        if (!File.Exists(localPath))
        {
            return RemoteControlResult.Failure("NOT_FOUND", $"Local file not found: {localPath}");
        }

        var bytes = await File.ReadAllBytesAsync(localPath, ct).ConfigureAwait(false);
        using var call = _client.UploadFolderShareFile(cancellationToken: ct);
        const int chunkSize = 64 * 1024;
        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var n = Math.Min(chunkSize, bytes.Length - offset);
            var last = offset + n >= bytes.Length;
            var msg = new UploadFolderShareFileRequest
            {
                ProtocolVersion = "v1",
                PeerId = _peerId,
                CorrelationId = Guid.NewGuid().ToString("n"),
                RelativePath = remoteRelativePath ?? string.Empty,
                TotalSize = bytes.Length,
                Data = Google.Protobuf.ByteString.CopyFrom(bytes, offset, n),
                Last = last,
            };
            await call.RequestStream.WriteAsync(msg).ConfigureAwait(false);
        }

        if (bytes.Length == 0)
        {
            await call.RequestStream.WriteAsync(new UploadFolderShareFileRequest
            {
                ProtocolVersion = "v1",
                PeerId = _peerId,
                CorrelationId = Guid.NewGuid().ToString("n"),
                RelativePath = remoteRelativePath ?? string.Empty,
                TotalSize = 0,
                Last = true,
            }).ConfigureAwait(false);
        }

        await call.RequestStream.CompleteAsync().ConfigureAwait(false);
        var response = await call.ResponseAsync.ConfigureAwait(false);
        return new RemoteControlResult(response.Ok, response.Err, response.Msg);
    }

    /// <summary>Creates a directory (and parents) under the share root.</summary>
    public async Task<RemoteControlResult> CreateDirectoryAsync(string relativeDirectory, CancellationToken ct = default)
    {
        var response = await _client.CreateFolderShareDirectoryAsync(new CreateFolderShareDirectoryRequest
        {
            ProtocolVersion = "v1",
            PeerId = _peerId,
            CorrelationId = Guid.NewGuid().ToString("n"),
            RelativeDirectory = relativeDirectory ?? string.Empty,
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
        return new RemoteControlResult(response.Ok, response.Err, response.Msg);
    }

    /// <summary>Deletes a file or directory under the share root.</summary>
    public async Task<RemoteControlResult> DeleteAsync(string relativePath, bool recursive = false, CancellationToken ct = default)
    {
        var response = await _client.DeleteFolderShareEntryAsync(new DeleteFolderShareEntryRequest
        {
            ProtocolVersion = "v1",
            PeerId = _peerId,
            CorrelationId = Guid.NewGuid().ToString("n"),
            RelativePath = relativePath ?? string.Empty,
            Recursive = recursive,
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
        return new RemoteControlResult(response.Ok, response.Err, response.Msg);
    }

    /// <summary>Renames or moves an entry within the share root.</summary>
    public async Task<RemoteControlResult> RenameAsync(
        string relativePath,
        string newRelativePath,
        bool overwrite = false,
        CancellationToken ct = default)
    {
        var response = await _client.RenameFolderShareEntryAsync(new RenameFolderShareEntryRequest
        {
            ProtocolVersion = "v1",
            PeerId = _peerId,
            CorrelationId = Guid.NewGuid().ToString("n"),
            RelativePath = relativePath ?? string.Empty,
            NewRelativePath = newRelativePath ?? string.Empty,
            Overwrite = overwrite,
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
        return new RemoteControlResult(response.Ok, response.Err, response.Msg);
    }
}
