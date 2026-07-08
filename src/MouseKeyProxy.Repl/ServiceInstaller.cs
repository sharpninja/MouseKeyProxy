using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MouseKeyProxy.Repl;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

public interface IProcessRunner
{
    ProcessRunResult Run(string fileName, string arguments, bool redirectOutput = true);
}

public sealed class SystemProcessRunner : IProcessRunner
{
    public ProcessRunResult Run(string fileName, string arguments, bool redirectOutput = true)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdout = redirectOutput ? process.StandardOutput.ReadToEnd() : string.Empty;
        var stderr = redirectOutput ? process.StandardError.ReadToEnd() : string.Empty;
        process.WaitForExit();
        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }
}

public sealed class ServiceInstallContext
{
    public string PayloadsDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "payloads");
    public string InstallDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MouseKeyProxy");
    public string ServiceName { get; init; } = "MouseKeyProxy";
    public string FirewallRuleName { get; init; } = "MouseKeyProxy-gRPC";
    public string TrayTaskName { get; init; } = "MouseKeyProxyTray";
    public string? TrayUser { get; init; }
    public bool SkipTrayTask { get; init; }
    public Action<string>? Log { get; init; }
}

public sealed class ServiceInstallResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public IReadOnlyList<string> CopiedFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<(string FileName, string Arguments)> ProcessCalls { get; init; } = Array.Empty<(string, string)>();
    public IReadOnlyList<ProcessRunResult> StepResults { get; init; } = Array.Empty<ProcessRunResult>();
}

public static class ServiceInstaller
{
    public static string ServiceExePath(ServiceInstallContext ctx) =>
        Path.Combine(ctx.InstallDirectory, "MouseKeyProxy.Service.exe");

    public static string AgentExePath(ServiceInstallContext ctx) =>
        Path.Combine(ctx.InstallDirectory, "Agent", "MouseKeyProxy.Agent.exe");

    public static ServiceInstallResult Install(ServiceInstallContext ctx, IProcessRunner runner)
    {
        var log = ctx.Log ?? Console.WriteLine;
        var copied = new List<string>();
        var calls = new List<(string, string)>();
        var stepResults = new List<ProcessRunResult>();
        var rollback = new List<(string FileName, string Arguments)>();

        // FR-MKP-006: on partial failure, reverse the applied steps (in reverse order) and remove any
        // copied payloads so a failed install does not leave the machine half-configured.
        void RollbackAll()
        {
            foreach (var (fileName, arguments) in Enumerable.Reverse(rollback))
            {
                try
                {
                    var r = runner.Run(fileName, arguments);
                    calls.Add((fileName, arguments));
                    stepResults.Add(r);
                    log($"rollback: {fileName} {arguments}");
                }
                catch (Exception ex)
                {
                    log($"rollback step failed: {fileName} {arguments}: {ex.Message}");
                }
            }

            rollback.Clear();

            foreach (var file in copied)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    log($"rollback file delete failed: {file}: {ex.Message}");
                }
            }

            copied.Clear();
        }

        ServiceInstallResult Fail(int exitCode, string? detail = null)
        {
            if (!string.IsNullOrWhiteSpace(detail))
            {
                log(detail);
            }

            RollbackAll();

            return new ServiceInstallResult
            {
                Success = false,
                ExitCode = exitCode,
                CopiedFiles = copied,
                ProcessCalls = calls,
                StepResults = stepResults
            };
        }

        bool TryRun(string fileName, string arguments, bool required = true)
        {
            var result = runner.Run(fileName, arguments);
            calls.Add((fileName, arguments));
            stepResults.Add(result);
            var combined = (result.StandardOutput + result.StandardError).Trim();
            if (!string.IsNullOrWhiteSpace(combined))
            {
                log(combined);
            }

            if (required && result.ExitCode != 0)
            {
                return false;
            }

            return true;
        }

        log("Installing MouseKeyProxy service and agent...");
        Directory.CreateDirectory(ctx.InstallDirectory);
        ApplyInstallAcl(ctx.InstallDirectory);

        var serviceSrc = Path.Combine(ctx.PayloadsDirectory, "service", "MouseKeyProxy.Service.exe");
        if (!File.Exists(serviceSrc))
        {
            log($"payload copy skipped: missing {serviceSrc}");
            return Fail(3, "Service payload missing.");
        }

        var dest = ServiceExePath(ctx);
        File.Copy(serviceSrc, dest, overwrite: true);
        copied.Add(dest);
        log($"payload copy: {serviceSrc} -> {dest}");

        var agentSrcDir = Path.Combine(ctx.PayloadsDirectory, "agent");
        if (Directory.Exists(agentSrcDir))
        {
            var agentDestDir = Path.Combine(ctx.InstallDirectory, "Agent");
            Directory.CreateDirectory(agentDestDir);
            foreach (var file in Directory.GetFiles(agentSrcDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(agentSrcDir, file);
                var agentDest = Path.Combine(agentDestDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(agentDest)!);
                try
                {
                    File.Copy(file, agentDest, overwrite: true);
                    copied.Add(agentDest);
                }
                catch (IOException ex)
                {
                    log($"payload copy warning: {file} -> {agentDest}: {ex.Message}");
                }
            }

            log($"payload copy: agent directory {agentSrcDir} -> {agentDestDir} ({copied.Count} files)");
        }

        if (!TryRun("sc.exe", $"create {ctx.ServiceName} binPath= \"{ServiceExePath(ctx)}\" start= auto"))
        {
            var eval = ServiceInstallSteps.EvaluateStepResults(stepResults);
            return Fail(eval.ExitCode, eval.FailureDetail);
        }

        rollback.Add(("sc.exe", $"delete {ctx.ServiceName}"));

        if (!TryRun("sc.exe", $"description {ctx.ServiceName} \"MouseKeyProxy - Free hotkey-only alternative to PowerToys Mouse Without Borders\""))
        {
            var eval = ServiceInstallSteps.EvaluateStepResults(stepResults);
            return Fail(eval.ExitCode, eval.FailureDetail);
        }

        if (!TryRun("netsh", $"advfirewall firewall add rule name=\"{ctx.FirewallRuleName}\" dir=in action=allow protocol=TCP localport=50051 profile=any"))
        {
            var eval = ServiceInstallSteps.EvaluateStepResults(stepResults);
            return Fail(eval.ExitCode, eval.FailureDetail);
        }

        rollback.Add(("netsh", $"advfirewall firewall delete rule name=\"{ctx.FirewallRuleName}\""));

        if (!TryRun("sc.exe", $"start {ctx.ServiceName}"))
        {
            var eval = ServiceInstallSteps.EvaluateStepResults(stepResults);
            return Fail(eval.ExitCode, eval.FailureDetail);
        }

        if (!ctx.SkipTrayTask && File.Exists(AgentExePath(ctx)))
        {
            var userName = Environment.UserName;
            var domain = Environment.UserDomainName;
            var fullUser = ctx.TrayUser ?? (string.IsNullOrEmpty(domain) ? userName : $"{domain}\\{userName}");
            var agentExe = AgentExePath(ctx);
            var createArgs = $"/Create /TN \"{ctx.TrayTaskName}\" /TR \"\\\"{agentExe}\\\"\" /SC ONLOGON /RU \"{fullUser}\" /RL LIMITED /F";
            if (!TryRun("schtasks.exe", createArgs))
            {
                var eval = ServiceInstallSteps.EvaluateStepResults(stepResults);
                return Fail(eval.ExitCode, eval.FailureDetail);
            }

            rollback.Add(("schtasks.exe", $"/Delete /TN \"{ctx.TrayTaskName}\" /F"));

            log("Tray scheduled task created for logon.");
            if (!TryRun("schtasks.exe", $"/Run /TN \"{ctx.TrayTaskName}\""))
            {
                var eval = ServiceInstallSteps.EvaluateStepResults(stepResults);
                return Fail(eval.ExitCode, eval.FailureDetail);
            }

            log("Tray agent set to start at logon (via scheduled task) and launched.");
        }

        log("Service installed.");
        return new ServiceInstallResult
        {
            Success = true,
            ExitCode = 0,
            CopiedFiles = copied,
            ProcessCalls = calls,
            StepResults = stepResults
        };
    }

    public static ServiceInstallResult Uninstall(ServiceInstallContext ctx, IProcessRunner runner)
    {
        var log = ctx.Log ?? Console.WriteLine;
        var calls = new List<(string, string)>();
        var stepResults = new List<ProcessRunResult>();

        void Run(string fileName, string arguments, bool required = false)
        {
            var result = runner.Run(fileName, arguments);
            calls.Add((fileName, arguments));
            stepResults.Add(result);
            var combined = (result.StandardOutput + result.StandardError).Trim();
            if (!string.IsNullOrWhiteSpace(combined))
            {
                log(combined);
            }

            if (required && result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Uninstall step failed: {fileName} {arguments} exit {result.ExitCode}");
            }
        }

        log("Uninstalling MouseKeyProxy service...");
        Run("sc.exe", $"stop {ctx.ServiceName}");
        Run("sc.exe", $"delete {ctx.ServiceName}");
        Run("netsh", $"advfirewall firewall delete rule name=\"{ctx.FirewallRuleName}\"");

        if (Directory.Exists(ctx.InstallDirectory))
        {
            try
            {
                Directory.Delete(ctx.InstallDirectory, recursive: true);
                log($"Removed install directory {ctx.InstallDirectory}");
            }
            catch (Exception ex)
            {
                log($"Warning: could not remove install directory: {ex.Message}");
            }
        }

        log("Uninstall complete.");
        return new ServiceInstallResult
        {
            Success = true,
            ExitCode = 0,
            ProcessCalls = calls,
            StepResults = stepResults
        };
    }

    private static void ApplyInstallAcl(string installDir)
    {
        var dirInfo = new DirectoryInfo(installDir);
        var dirSecurity = dirInfo.GetAccessControl();
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        dirSecurity.AddAccessRule(new FileSystemAccessRule(
            admins,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        dirSecurity.AddAccessRule(new FileSystemAccessRule(
            users,
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        dirInfo.SetAccessControl(dirSecurity);
    }
}