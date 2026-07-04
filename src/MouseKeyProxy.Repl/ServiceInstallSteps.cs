namespace MouseKeyProxy.Repl;

/// <summary>
/// Pure step evaluation for service install/uninstall process results.
/// </summary>
public static class ServiceInstallSteps
{
    public static readonly string[] InstallRegistrationSteps =
    [
        "sc create",
        "sc description",
        "netsh firewall add",
        "sc start",
        "schtasks create",
        "schtasks run"
    ];

    public static (bool Success, int ExitCode, string? FailureDetail) EvaluateStepResults(
        IReadOnlyList<ProcessRunResult> results)
    {
        foreach (var result in results)
        {
            if (result.ExitCode == 0)
            {
                continue;
            }

            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = $"process exited with code {result.ExitCode}";
            }

            return (false, result.ExitCode, detail.Trim());
        }

        return (true, 0, null);
    }
}