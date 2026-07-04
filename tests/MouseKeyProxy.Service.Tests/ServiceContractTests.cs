using MouseKeyProxy.Repl;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// TEST-MKP-007: drives shipped ServiceInstaller code paths with real file copies and exit-code gates.
/// </summary>
public class ServiceContractTests
{
    [Fact]
    [Trait("Category", "ServiceContract")]
    public void TEST_MKP_007_Install_Copies_Payloads_And_Registers_Service_Fw_Tray()
    {
        using var root = new TempInstallRoot();
        root.SeedPayloads();
        var runner = new RecordingProcessRunner();
        var logs = new List<string>();
        var ctx = new ServiceInstallContext
        {
            PayloadsDirectory = root.PayloadsDir,
            InstallDirectory = root.InstallDir,
            TrayUser = $"{Environment.UserDomainName}\\{Environment.UserName}",
            Log = logs.Add
        };

        var result = ServiceInstaller.Install(ctx, runner);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(ServiceInstaller.ServiceExePath(ctx)));
        Assert.True(File.Exists(Path.Combine(root.InstallDir, "Agent", "logo.png")));
        Assert.Contains(runner.Calls, c => c.FileName == "sc.exe" && c.Arguments.Contains("create MouseKeyProxy"));
        Assert.Contains(runner.Calls, c => c.FileName == "netsh" && c.Arguments.Contains("MouseKeyProxy-gRPC"));
        Assert.Contains(runner.Calls, c => c.FileName == "schtasks.exe" && c.Arguments.Contains("MouseKeyProxyTray"));
        Assert.Contains(logs, l => l.Contains("payload copy:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, l => l.Contains("Tray scheduled task created", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, l => l.Contains("Service installed.", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "ServiceContract")]
    public void TEST_MKP_007_Install_Fails_When_Sc_Create_Denied()
    {
        using var root = new TempInstallRoot();
        root.SeedPayloads();
        var runner = new RecordingProcessRunner();
        runner.SetResponse("sc.exe", "create", new ProcessRunResult(5, string.Empty, "Access is denied."));
        var logs = new List<string>();
        var ctx = new ServiceInstallContext
        {
            PayloadsDirectory = root.PayloadsDir,
            InstallDirectory = root.InstallDir,
            SkipTrayTask = true,
            Log = logs.Add
        };

        var result = ServiceInstaller.Install(ctx, runner);

        Assert.False(result.Success);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(logs, l => l.Contains("Access is denied", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs, l => l.Contains("Service installed.", StringComparison.Ordinal));
        Assert.Contains(runner.Calls, c => c.FileName == "sc.exe" && c.Arguments.Contains("create MouseKeyProxy"));
        Assert.DoesNotContain(runner.Calls, c => c.FileName == "netsh");
    }

    [Fact]
    [Trait("Category", "ServiceContract")]
    public void TEST_MKP_007_EvaluateStepResults_Fails_On_First_NonZero()
    {
        var results = new[]
        {
            new ProcessRunResult(0, "ok", string.Empty),
            new ProcessRunResult(5, string.Empty, "Access is denied.")
        };

        var (success, exitCode, detail) = ServiceInstallSteps.EvaluateStepResults(results);

        Assert.False(success);
        Assert.Equal(5, exitCode);
        Assert.Contains("Access is denied", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ServiceContract")]
    public void TEST_MKP_007_Uninstall_Reverses_Service_Firewall_And_Install_Dir()
    {
        using var root = new TempInstallRoot();
        root.SeedPayloads();
        var runner = new RecordingProcessRunner();
        var ctx = new ServiceInstallContext
        {
            PayloadsDirectory = root.PayloadsDir,
            InstallDirectory = root.InstallDir,
            SkipTrayTask = true
        };
        ServiceInstaller.Install(ctx, runner);

        var result = ServiceInstaller.Uninstall(ctx, runner);

        Assert.True(result.Success);
        Assert.False(Directory.Exists(root.InstallDir));
        Assert.Contains(runner.Calls, c => c.FileName == "sc.exe" && c.Arguments.Contains("delete MouseKeyProxy"));
        Assert.Contains(runner.Calls, c => c.FileName == "netsh" && c.Arguments.Contains("delete rule"));
    }

    [Fact]
    [Trait("Category", "ServiceContract")]
    public void TEST_MKP_007_Install_Fails_When_Service_Payload_Missing()
    {
        using var root = new TempInstallRoot();
        var runner = new RecordingProcessRunner();
        var logs = new List<string>();
        var ctx = new ServiceInstallContext
        {
            PayloadsDirectory = root.PayloadsDir,
            InstallDirectory = root.InstallDir,
            SkipTrayTask = true,
            Log = logs.Add
        };

        var result = ServiceInstaller.Install(ctx, runner);

        Assert.False(result.Success);
        Assert.Equal(3, result.ExitCode);
        Assert.Empty(result.CopiedFiles);
        Assert.DoesNotContain(runner.Calls, c => c.FileName == "sc.exe");
        Assert.DoesNotContain(logs, l => l.Contains("Service installed.", StringComparison.Ordinal));
    }

    private sealed class TempInstallRoot : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mkp-svc-test-" + Guid.NewGuid().ToString("N"));
        public string PayloadsDir => Path.Combine(Root, "payloads");
        public string InstallDir => Path.Combine(Root, "install");

        public void SeedPayloads()
        {
            var serviceDir = Path.Combine(PayloadsDir, "service");
            var agentDir = Path.Combine(PayloadsDir, "agent");
            Directory.CreateDirectory(serviceDir);
            Directory.CreateDirectory(agentDir);
            File.WriteAllText(Path.Combine(serviceDir, "MouseKeyProxy.Service.exe"), "service");
            File.WriteAllText(Path.Combine(agentDir, "MouseKeyProxy.Agent.exe"), "agent");
            File.WriteAllText(Path.Combine(agentDir, "logo.png"), "logo");
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}