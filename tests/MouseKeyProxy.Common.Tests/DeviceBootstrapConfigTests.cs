using System;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-024 / FR-MKP-025 / TEST: parse and validate device-bootstrap.json.
/// </summary>
public class DeviceBootstrapConfigTests
{
    /// <summary>Valid JSON round-trips.</summary>
    [Fact]
    public void Parse_Valid_Succeeds()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "devicePeerId": "mkp-hid-pi",
              "deviceGrpcUrl": "https://192.168.1.200:50051",
              "discoveryPort": 50052,
              "preferDiscovery": true,
              "clientRole": "UsbConnectedPc",
              "advertiseClientService": true,
              "installTicket": "abc123"
            }
            """;

        var cfg = DeviceBootstrapConfig.Parse(json);
        Assert.Equal(1, cfg.SchemaVersion);
        Assert.Equal("mkp-hid-pi", cfg.DevicePeerId);
        Assert.Equal("https://192.168.1.200:50051", cfg.DeviceGrpcUrl);
        Assert.Equal(50052, cfg.DiscoveryPort);
        Assert.True(cfg.PreferDiscovery);
        Assert.Equal("abc123", cfg.InstallTicket);

        var again = DeviceBootstrapConfig.Parse(cfg.ToJson());
        Assert.Equal(cfg.DeviceGrpcUrl, again.DeviceGrpcUrl);
    }

    /// <summary>Missing URL when discovery disabled fails validation.</summary>
    [Fact]
    public void Parse_NoUrl_NoDiscovery_Fails()
    {
        const string json = """
            { "schemaVersion": 1, "preferDiscovery": false }
            """;
        Assert.ThrowsAny<Exception>(() => DeviceBootstrapConfig.Parse(json));
    }

    /// <summary>Invalid URL fails validation.</summary>
    [Fact]
    public void Parse_BadUrl_Fails()
    {
        const string json = """
            { "schemaVersion": 1, "deviceGrpcUrl": "not-a-url", "preferDiscovery": false }
            """;
        Assert.ThrowsAny<Exception>(() => DeviceBootstrapConfig.Parse(json));
    }
}
