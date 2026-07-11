using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-024 / FR-MKP-025: non-secret bootstrap metadata staged beside the client MSI
/// (<c>device-bootstrap.json</c>) on MKP-DEPLOY/install media.
/// </summary>
public sealed class DeviceBootstrapConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>Schema version (currently 1).</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Device peer id (e.g. mkp-hid-pi).</summary>
    public string DevicePeerId { get; set; } = string.Empty;

    /// <summary>Device gRPC base URL (https).</summary>
    public string DeviceGrpcUrl { get; set; } = string.Empty;

    /// <summary>UDP discovery port (default 50052).</summary>
    public int DiscoveryPort { get; set; } = 50052;

    /// <summary>When true, bootstrap prefers LAN discovery before fixed URL.</summary>
    public bool PreferDiscovery { get; set; } = true;

    /// <summary>Client role label (UsbConnectedPc).</summary>
    public string ClientRole { get; set; } = "UsbConnectedPc";

    /// <summary>When true, client advertises local Service for clipboard channel auth.</summary>
    public bool AdvertiseClientService { get; set; } = true;

    /// <summary>
    /// Optional install ticket (capability-bearing). Prefer side-channel if volume is world-readable.
    /// </summary>
    public string? InstallTicket { get; set; }

    /// <summary>Parses JSON text into a config; throws when invalid.</summary>
    /// <param name="json">JSON document.</param>
    /// <returns>Parsed config.</returns>
    public static DeviceBootstrapConfig Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Bootstrap JSON is required.", nameof(json));
        }

        var cfg = JsonSerializer.Deserialize<DeviceBootstrapConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException("Bootstrap JSON deserialized to null.");

        cfg.Validate();
        return cfg;
    }

    /// <summary>Loads and parses a file path.</summary>
    /// <param name="path">Path to device-bootstrap.json.</param>
    /// <returns>Parsed config.</returns>
    public static DeviceBootstrapConfig LoadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var text = File.ReadAllText(path);
        return Parse(text);
    }

    /// <summary>Serializes to JSON for staging on install media.</summary>
    /// <returns>Indented JSON.</returns>
    public string ToJson()
    {
        Validate();
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>Validates required fields.</summary>
    public void Validate()
    {
        if (SchemaVersion < 1)
        {
            throw new InvalidOperationException("schemaVersion must be >= 1.");
        }

        if (string.IsNullOrWhiteSpace(DeviceGrpcUrl) && !PreferDiscovery)
        {
            throw new InvalidOperationException("deviceGrpcUrl is required when preferDiscovery is false.");
        }

        if (!string.IsNullOrWhiteSpace(DeviceGrpcUrl))
        {
            if (!Uri.TryCreate(DeviceGrpcUrl.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException("deviceGrpcUrl must be an absolute http(s) URL.");
            }
        }

        if (DiscoveryPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("discoveryPort must be 1-65535.");
        }
    }
}
