using System.Net.Sockets;

namespace MouseKeyProxy.Compliance.Tests;

public class HardwareHidComplianceTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    [Trait("Category", "ByrdCompliance")]
    [Trait("Category", "HardwareHID")]
    public void Byrd_Rup_Process_Document_Uses_Zero_Fail_Zero_Skip_Gate()
    {
        var process = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Development-Process-draft-v4.md"));

        foreach (var term in new[] { "Inception", "Elaboration", "Construction", "Transition" })
        {
            Assert.Contains(term, process);
        }

        Assert.Contains("zero failed tests", process);
        Assert.Contains("zero skipped tests", process);
        Assert.Contains("MCP requirements", process);
        Assert.Contains("acceptance criteria", process);
    }

    [Fact]
    [Trait("Category", "MCPCompliance")]
    [Trait("Category", "HardwareHID")]
    public void Exported_Requirements_Contain_Hid_Traceability_And_Structured_Criteria()
    {
        var functional = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Project", "Functional-Requirements.md"));
        var technical = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Project", "Technical-Requirements.md"));
        var testing = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Project", "Testing-Requirements.md"));
        var matrix = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Project", "Requirements-Matrix.md"));

        Assert.Contains("FR-MKP-012", functional);
        Assert.Contains("C#/.NET 10", functional);
        Assert.Contains("no Python", functional, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TR-MKP-HID-001", technical);
        Assert.Contains("linux-arm64", technical);
        Assert.Contains("TEST-MKP-027", testing);
        Assert.Contains("TEST-MKP-028", testing);
        Assert.Contains("TEST-MKP-029", testing);
        Assert.Contains("FR-MKP-012", matrix);
        Assert.Contains("TR-MKP-HID-001", matrix);
        Assert.Contains("TEST-MKP-027", matrix);
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public void Hid_Appliance_Is_Dotnet_Only_With_No_Python_Dependency()
    {
        var piProject = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.PiHid", "MouseKeyProxy.PiHid.csproj"));
        var solution = File.ReadAllText(Path.Combine(RepoRoot, "MouseKeyProxy.slnx"));
        var doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "hardware", "pi-zero-2-hid.md"));
        var publish = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "pi", "publish-pi-hid.ps1"));

        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", piProject);
        Assert.Contains("linux-arm64", piProject);
        Assert.Contains("MouseKeyProxy.PiHid.csproj", solution);
        Assert.Contains("dotnet publish src/MouseKeyProxy.PiHid/MouseKeyProxy.PiHid.csproj -c Release -r linux-arm64 --self-contained true", doc);
        Assert.Contains("dotnet publish src/MouseKeyProxy.PiHid/MouseKeyProxy.PiHid.csproj -c $Configuration -r linux-arm64 --self-contained true", publish);

        var appliancePaths = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "MouseKeyProxy.PiHid"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "scripts", "pi"), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "docs", "hardware"), "*", SearchOption.AllDirectories));
        Assert.DoesNotContain(appliancePaths, path => string.Equals(Path.GetExtension(path), ".py", StringComparison.OrdinalIgnoreCase));

        foreach (var path in appliancePaths)
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("python3", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pip install", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("requirements.txt", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("virtualenv", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public void Repl_Exposes_Hid_Command_Surface()
    {
        var repl = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.Repl", "Program.cs"));

        Assert.Contains("mkp hid status", repl);
        Assert.Contains("case \"hid\"", repl);
        Assert.Contains("DoHidProvisionCheck", repl);
        Assert.Contains("DoHidTestKey", repl);
        Assert.Contains("DoHidTestMouse", repl);
        Assert.Contains("DoHidCaptureProof", repl);
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public async Task Hardware_E2E_Gate_Fails_Actionably_When_Enabled_Without_Prerequisites()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MKP_HARDWARE_E2E"), "1", StringComparison.Ordinal))
        {
            Assert.True(true);
            return;
        }

        var host = RequiredEnv("MKP_HID_PI_HOST");
        _ = RequiredEnv("MKP_HID_PI_TOKEN");
        _ = RequiredEnv("MKP_TARGET_HOST");

        var port = int.TryParse(Environment.GetEnvironmentVariable("MKP_HID_PI_PORT"), out var parsed)
            ? parsed
            : 8765;

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"MKP_HARDWARE_E2E=1 but {host}:{port} is not reachable within 3 seconds.");
        }
        catch (SocketException ex)
        {
            Assert.Fail($"MKP_HARDWARE_E2E=1 but {host}:{port} is not reachable: {ex.Message}");
        }
    }

    private static string RequiredEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"MKP_HARDWARE_E2E=1 but {name} is not set.");
        return value!;
    }
}
