using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MouseKeyProxy.Repl;
using Xunit;

namespace MouseKeyProxy.Repl.Tests;

/// <summary>
/// FR-MKP-006: verifies ServiceInstaller.Install reverses applied steps (in reverse order) and removes
/// copied payloads when a later step fails, so a partial install does not leave the machine
/// half-configured. Uses a scripted IProcessRunner - no real sc.exe/netsh/schtasks.
/// </summary>
public class ServiceInstallerRollbackTests
{
    /// <summary>A runner that returns a scripted exit code per (fileName, argsPrefix) match.</summary>
    private sealed class ScriptedRunner : IProcessRunner
    {
        private readonly Func<string, string, int> _exit;
        public List<(string FileName, string Arguments)> Calls { get; } = new();

        public ScriptedRunner(Func<string, string, int> exit) => _exit = exit;

        public ProcessRunResult Run(string fileName, string arguments, bool redirectOutput = true)
        {
            Calls.Add((fileName, arguments));
            return new ProcessRunResult(_exit(fileName, arguments), string.Empty, string.Empty);
        }
    }

    /// <summary>When the firewall step fails, the created service is deleted and copied payloads removed.</summary>
    [Fact]
    [Trait("Category", "ServiceInstall")]
    public void Install_FirewallStepFails_RollsBackServiceAndPayload()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"mkp-install-{Guid.NewGuid():N}");
        var payloads = Path.Combine(temp, "payloads");
        var serviceSrcDir = Path.Combine(payloads, "service");
        Directory.CreateDirectory(serviceSrcDir);
        var serviceSrc = Path.Combine(serviceSrcDir, "MouseKeyProxy.Service.exe");
        File.WriteAllText(serviceSrc, "stub");

        var installDir = Path.Combine(temp, "install");

        // Fail on the netsh firewall step; everything else succeeds.
        var runner = new ScriptedRunner((fileName, args) =>
            fileName == "netsh" && args.Contains("add rule") ? 1 : 0);

        var ctx = new ServiceInstallContext
        {
            PayloadsDirectory = payloads,
            InstallDirectory = installDir,
            SkipTrayTask = true,
            Log = _ => { },
        };

        try
        {
            var result = ServiceInstaller.Install(ctx, runner);

            Assert.False(result.Success);

            // The rollback ran `sc.exe delete` for the created service, after the failed netsh add.
            var deleteIndex = runner.Calls.FindIndex(c => c.FileName == "sc.exe" && c.Arguments.StartsWith("delete "));
            var createIndex = runner.Calls.FindIndex(c => c.FileName == "sc.exe" && c.Arguments.StartsWith("create "));
            Assert.True(createIndex >= 0, "service create should have been attempted");
            Assert.True(deleteIndex > createIndex, "service delete (rollback) should run after create");

            // Copied payloads were removed and the result reports no lingering copied files.
            Assert.Empty(result.CopiedFiles);
            Assert.False(File.Exists(ServiceInstaller.ServiceExePath(ctx)));
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    /// <summary>A fully-successful install performs no rollback (no delete calls).</summary>
    [Fact]
    [Trait("Category", "ServiceInstall")]
    public void Install_AllStepsSucceed_NoRollback()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"mkp-install-ok-{Guid.NewGuid():N}");
        var serviceSrcDir = Path.Combine(temp, "payloads", "service");
        Directory.CreateDirectory(serviceSrcDir);
        File.WriteAllText(Path.Combine(serviceSrcDir, "MouseKeyProxy.Service.exe"), "stub");

        var runner = new ScriptedRunner((_, _) => 0);
        var ctx = new ServiceInstallContext
        {
            PayloadsDirectory = Path.Combine(temp, "payloads"),
            InstallDirectory = Path.Combine(temp, "install"),
            SkipTrayTask = true,
            Log = _ => { },
        };

        try
        {
            var result = ServiceInstaller.Install(ctx, runner);

            Assert.True(result.Success);
            Assert.DoesNotContain(runner.Calls, c => c.FileName == "sc.exe" && c.Arguments.StartsWith("delete "));
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }
}
