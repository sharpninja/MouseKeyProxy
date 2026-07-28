namespace MouseKeyProxy.Commands.ShareMount;

/// <summary>Result of a host virtual-drive mount or unmount operation.</summary>
/// <param name="Ok">True when the operation succeeded.</param>
/// <param name="ErrorCode">Stable machine code (e.g. <c>WINFSP_RUNTIME_MISSING</c>, <c>ALREADY_MOUNTED</c>).</param>
/// <param name="Message">Human-readable detail suitable for CLI/UI status.</param>
/// <param name="MountPoint">Resolved mount point when mounted; otherwise empty.</param>
public sealed record ShareMountResult(bool Ok, string ErrorCode, string Message, string MountPoint = "")
{
    /// <summary>Successful result.</summary>
    public static ShareMountResult Success(string message, string mountPoint = "")
        => new(true, string.Empty, message, mountPoint);

    /// <summary>Failed result.</summary>
    public static ShareMountResult Failure(string errorCode, string message, string mountPoint = "")
        => new(false, errorCode, message, mountPoint);
}
