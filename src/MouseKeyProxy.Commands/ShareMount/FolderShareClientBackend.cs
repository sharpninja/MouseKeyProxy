using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Commands.ShareMount;

/// <summary>
/// TR-MKP-SHARE-WINFSP: <see cref="IShareFileBackend"/> over the remote gRPC
/// <see cref="FolderShareClient"/> (list/get/put/mkdir/rm/mv).
/// </summary>
public sealed class FolderShareClientBackend : IShareFileBackend
{
    private readonly FolderShareClient _client;
    private readonly TimeSpan _timeout;

    /// <summary>Creates a backend over <paramref name="client"/>.</summary>
    public FolderShareClientBackend(FolderShareClient client, TimeSpan? timeout = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
    }

    /// <inheritdoc />
    public FolderShareResult List(string relativeDirectory, out IReadOnlyList<FolderShareEntry> entries)
    {
        entries = Array.Empty<FolderShareEntry>();
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            var response = _client.ListAsync(relativeDirectory ?? string.Empty, cts.Token).GetAwaiter().GetResult();
            if (!response.Ok)
            {
                return new FolderShareResult(false, response.Err, response.Msg);
            }

            entries = response.Entries.Select(e => new FolderShareEntry(
                e.Name,
                e.RelativePath,
                e.IsDirectory,
                e.SizeBytes,
                e.ModifiedUnixMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(e.ModifiedUnixMs)
                    : DateTimeOffset.UtcNow)).ToList();
            return new FolderShareResult(true, string.Empty, "ok");
        }
        catch (Exception ex)
        {
            return new FolderShareResult(false, "RPC_ERROR", ex.Message);
        }
    }

    /// <inheritdoc />
    public FolderShareResult TryGetEntry(string relativePath, out FolderShareEntry? entry)
    {
        entry = null;
        relativePath ??= string.Empty;
        relativePath = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(relativePath))
        {
            entry = new FolderShareEntry(string.Empty, string.Empty, true, 0, DateTimeOffset.UtcNow);
            return new FolderShareResult(true, string.Empty, "ok");
        }

        var parent = SharePathMapper.GetParentRelative(relativePath);
        var leaf = SharePathMapper.GetLeafName(relativePath);
        var list = List(parent, out var entries);
        if (!list.Ok)
        {
            return list;
        }

        entry = entries.FirstOrDefault(e =>
            string.Equals(e.Name, leaf, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return new FolderShareResult(false, "NOT_FOUND", "Path not found.");
        }

        return new FolderShareResult(true, string.Empty, "ok");
    }

    /// <inheritdoc />
    public FolderShareResult ReadAllBytes(string relativePath, out byte[] data)
    {
        data = Array.Empty<byte>();
        var temp = Path.Combine(Path.GetTempPath(), "mkp-share-" + Guid.NewGuid().ToString("n"));
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            var result = _client.DownloadAsync(relativePath, temp, cts.Token).GetAwaiter().GetResult();
            if (!result.Ok)
            {
                return new FolderShareResult(false, result.ErrorCode, result.Message);
            }

            data = File.ReadAllBytes(temp);
            return new FolderShareResult(true, string.Empty, "ok");
        }
        catch (Exception ex)
        {
            return new FolderShareResult(false, "RPC_ERROR", ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // best-effort temp cleanup
            }
        }
    }

    /// <inheritdoc />
    public FolderShareResult WriteAllBytes(string relativePath, byte[] data)
    {
        data ??= Array.Empty<byte>();
        var temp = Path.Combine(Path.GetTempPath(), "mkp-share-" + Guid.NewGuid().ToString("n"));
        try
        {
            File.WriteAllBytes(temp, data);
            using var cts = new CancellationTokenSource(_timeout);
            var result = _client.UploadAsync(temp, relativePath, cts.Token).GetAwaiter().GetResult();
            return result.Ok
                ? new FolderShareResult(true, string.Empty, "ok")
                : new FolderShareResult(false, result.ErrorCode, result.Message);
        }
        catch (Exception ex)
        {
            return new FolderShareResult(false, "RPC_ERROR", ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // best-effort temp cleanup
            }
        }
    }

    /// <inheritdoc />
    public FolderShareResult CreateDirectory(string relativeDirectory)
    {
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            var result = _client.CreateDirectoryAsync(relativeDirectory, cts.Token).GetAwaiter().GetResult();
            return result.Ok
                ? new FolderShareResult(true, string.Empty, "ok")
                : new FolderShareResult(false, result.ErrorCode, result.Message);
        }
        catch (Exception ex)
        {
            return new FolderShareResult(false, "RPC_ERROR", ex.Message);
        }
    }

    /// <inheritdoc />
    public FolderShareResult Delete(string relativePath, bool recursive)
    {
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            var result = _client.DeleteAsync(relativePath, recursive, cts.Token).GetAwaiter().GetResult();
            return result.Ok
                ? new FolderShareResult(true, string.Empty, "ok")
                : new FolderShareResult(false, result.ErrorCode, result.Message);
        }
        catch (Exception ex)
        {
            return new FolderShareResult(false, "RPC_ERROR", ex.Message);
        }
    }

    /// <inheritdoc />
    public FolderShareResult Rename(string relativePath, string newRelativePath, bool overwrite)
    {
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            var result = _client.RenameAsync(relativePath, newRelativePath, overwrite, cts.Token)
                .GetAwaiter().GetResult();
            return result.Ok
                ? new FolderShareResult(true, string.Empty, "ok")
                : new FolderShareResult(false, result.ErrorCode, result.Message);
        }
        catch (Exception ex)
        {
            return new FolderShareResult(false, "RPC_ERROR", ex.Message);
        }
    }
}
