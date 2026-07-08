using System;
using System.Diagnostics;

namespace MouseKeyProxy.Common;

/// <summary>FR-MKP-012: Result of a device power action (poweroff/reboot).</summary>
/// <param name="Ok">True when the action was initiated successfully.</param>
/// <param name="Error">Error code/message when <paramref name="Ok"/> is false; empty otherwise.</param>
public sealed record PowerActionResult(bool Ok, string Error);

/// <summary>
/// FR-MKP-012: Abstraction over safely powering off or rebooting the device. Implemented on the
/// Linux/Pi appliance; a no-op unsupported variant is used on platforms (Windows) that must not
/// expose device power control.
/// </summary>
public interface ISystemPowerController
{
    /// <summary>Initiates a graceful device power action.</summary>
    /// <param name="reboot">True to reboot the device; false to power it off.</param>
    /// <returns>The result of initiating the action.</returns>
    PowerActionResult Trigger(bool reboot);
}

/// <summary>
/// FR-MKP-012: Power controller for platforms where device power control is not supported
/// (for example the Windows service host). Always reports PLATFORM_NOT_SUPPORTED.
/// </summary>
public sealed class UnsupportedPowerController : ISystemPowerController
{
    /// <inheritdoc />
    public PowerActionResult Trigger(bool reboot) => new(false, "PLATFORM_NOT_SUPPORTED");
}

/// <summary>
/// FR-MKP-012: Linux power controller that issues a graceful <c>systemctl reboot</c> or
/// <c>systemctl poweroff</c> so systemd stops services, syncs, and unmounts cleanly. Intended to
/// run as root under the appliance's systemd unit.
/// </summary>
public sealed class SystemctlPowerController : ISystemPowerController
{
    /// <inheritdoc />
    public PowerActionResult Trigger(bool reboot)
    {
        var verb = reboot ? "reboot" : "poweroff";
        try
        {
            var psi = new ProcessStartInfo("systemctl", verb)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return new PowerActionResult(false, "SYSTEMCTL_START_FAILED");
            }

            // systemctl returns once the transition is enqueued; bound the wait so we never hang.
            process.WaitForExit(5000);
            return new PowerActionResult(true, string.Empty);
        }
        catch (Exception ex)
        {
            return new PowerActionResult(false, ex.Message);
        }
    }
}
