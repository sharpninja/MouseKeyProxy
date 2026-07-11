using System;
using MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Agent;

/// <summary>
/// FR-MKP-018 / TR-MKP-UI-001: testable mappers between gRPC device config messages and UI state.
/// Keeps WinForms free of proto mapping logic for unit testing (TEST-MKP-041/043).
/// </summary>
public sealed class DeviceConfigUiModel
{
    /// <summary>Keyboard HID enabled.</summary>
    public bool KeyboardEnabled { get; set; }

    /// <summary>Mouse HID enabled.</summary>
    public bool MouseEnabled { get; set; }

    /// <summary>Disk FS LUN enabled.</summary>
    public bool FsEnabled { get; set; }

    /// <summary>True when FS is read-write.</summary>
    public bool FsReadWrite { get; set; }

    /// <summary>CD-ROM enabled.</summary>
    public bool CdromEnabled { get; set; }

    /// <summary>True when CD media source is host inbox.</summary>
    public bool CdromFromHost { get; set; }

    /// <summary>CD media path or name.</summary>
    public string CdromPath { get; set; } = string.Empty;

    /// <summary>When true, Apply sends UpdateCdromMedia.</summary>
    public bool UpdateCdromMedia { get; set; }

    /// <summary>Floppy enabled.</summary>
    public bool FloppyEnabled { get; set; }

    /// <summary>True when floppy media source is host inbox.</summary>
    public bool FloppyFromHost { get; set; }

    /// <summary>Floppy media path or name.</summary>
    public string FloppyPath { get; set; } = string.Empty;

    /// <summary>When true, Apply sends UpdateFloppyMedia.</summary>
    public bool UpdateFloppyMedia { get; set; }
}

/// <summary>Static mappers for device configuration UI.</summary>
public static class DeviceConfigViewModel
{
    /// <summary>Maps GetDeviceConfiguration state into a UI model.</summary>
    /// <param name="state">Wire state (must not be null).</param>
    public static DeviceConfigUiModel FromState(DeviceFunctionStateMsg state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new DeviceConfigUiModel
        {
            KeyboardEnabled = state.KeyboardEnabled,
            MouseEnabled = state.MouseEnabled,
            FsEnabled = state.FsEnabled,
            FsReadWrite = state.FsAccess == DeviceFsAccessMode.FsReadWrite,
            CdromEnabled = state.CdromEnabled,
            CdromFromHost = state.CdromMedia?.Source == DeviceMediaSource.MediaSourceHost,
            CdromPath = state.CdromMedia?.Path ?? string.Empty,
            FloppyEnabled = state.FloppyEnabled,
            FloppyFromHost = state.FloppyMedia?.Source == DeviceMediaSource.MediaSourceHost,
            FloppyPath = state.FloppyMedia?.Path ?? string.Empty,
        };
    }

    /// <summary>Builds a ConfigureDeviceRequest from UI model fields.</summary>
    /// <param name="model">UI model.</param>
    /// <param name="peerId">Local peer id for the request.</param>
    /// <param name="correlationId">Correlation id (new guid when null/empty).</param>
    public static ConfigureDeviceRequest ToConfigureRequest(
        DeviceConfigUiModel model,
        string peerId,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var request = new ConfigureDeviceRequest
        {
            ProtocolVersion = "v1",
            PeerId = string.IsNullOrWhiteSpace(peerId) ? Environment.MachineName : peerId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("n")
                : correlationId,
            KeyboardEnabled = model.KeyboardEnabled,
            MouseEnabled = model.MouseEnabled,
            FsEnabled = model.FsEnabled,
            FsAccess = model.FsReadWrite
                ? DeviceFsAccessMode.FsReadWrite
                : DeviceFsAccessMode.FsReadOnly,
            CdromEnabled = model.CdromEnabled,
            FloppyEnabled = model.FloppyEnabled,
            UpdateCdromMedia = model.UpdateCdromMedia,
            UpdateFloppyMedia = model.UpdateFloppyMedia,
        };

        if (model.UpdateCdromMedia)
        {
            request.CdromMedia = new StorageMediaSpecMsg
            {
                Source = model.CdromFromHost
                    ? DeviceMediaSource.MediaSourceHost
                    : DeviceMediaSource.MediaSourceDevice,
                Path = model.CdromPath?.Trim() ?? string.Empty,
            };
        }

        if (model.UpdateFloppyMedia)
        {
            request.FloppyMedia = new StorageMediaSpecMsg
            {
                Source = model.FloppyFromHost
                    ? DeviceMediaSource.MediaSourceHost
                    : DeviceMediaSource.MediaSourceDevice,
                Path = model.FloppyPath?.Trim() ?? string.Empty,
            };
        }

        return request;
    }

    /// <summary>
    /// Builds a Windows UNC path for the SMB share (TEST-MKP-043).
    /// </summary>
    /// <param name="host">Pi host name or IP.</param>
    /// <param name="shareName">Share name (default MouseKeyProxy).</param>
    public static string BuildSmbUnc(string host, string? shareName = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }

        var h = host.Trim().Trim('\\', '/');
        // Strip scheme/port if a gRPC URL was passed.
        if (h.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            h = h["https://".Length..];
        }
        else if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            h = h["http://".Length..];
        }

        var slash = h.IndexOf('/');
        if (slash >= 0)
        {
            h = h[..slash];
        }

        var colon = h.LastIndexOf(':');
        if (colon > 0 && h.IndexOf(']') < 0)
        {
            // host:port
            h = h[..colon];
        }

        var share = string.IsNullOrWhiteSpace(shareName) ? "MouseKeyProxy" : shareName.Trim();
        return $@"\\{h}\{share}";
    }

    /// <summary>Returns true when a typed pairing code looks plausible (non-empty, 4–12 chars).</summary>
    public static bool IsPlausiblePairingCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var t = code.Trim();
        return t.Length is >= 4 and <= 12;
    }
}
