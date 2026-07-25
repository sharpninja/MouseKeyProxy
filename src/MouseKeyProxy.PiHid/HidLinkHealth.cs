using System;
using System.IO;
using System.Linq;

namespace MouseKeyProxy.PiHid;

/// <summary>
/// TR-MKP-RELI-001: helpers for detecting USB gadget / HID host-link loss on Linux.
/// When the UDC is not attached (or hidg writes fail with transport errors), the host Agent
/// should fall back to local control.
/// </summary>
public static class HidLinkHealth
{
    /// <summary>Stable error code returned to gRPC clients when the gadget is not linked to a USB host.</summary>
    public const string DisconnectedErrorCode = "DEVICE_HID_DISCONNECTED";

    /// <summary>Prefix embedded in injector error strings for host-side matching.</summary>
    public const string DisconnectedErrorPrefix = "DEVICE_HID_DISCONNECTED:";

    /// <summary>
    /// Best-effort read of the first UDC state under /sys/class/udc.
    /// </summary>
    /// <returns>State string (e.g. configured, not attached), or null when unavailable.</returns>
    public static string? TryReadUdcState()
    {
        try
        {
            if (!Directory.Exists("/sys/class/udc"))
            {
                return null;
            }

            foreach (var udc in Directory.EnumerateDirectories("/sys/class/udc"))
            {
                var statePath = Path.Combine(udc, "state");
                if (File.Exists(statePath))
                {
                    return File.ReadAllText(statePath).Trim();
                }
            }
        }
        catch
        {
            // Non-Linux or restricted environment.
        }

        return null;
    }

    /// <summary>True when the gadget is bound and a USB host has configured the link.</summary>
    public static bool IsHostLinkUp()
    {
        var state = TryReadUdcState();
        if (state is null)
        {
            // Unknown (Windows/tests): do not block inject.
            return true;
        }

        // Linux UDC states: not attached, attached, powered, default, addressed, configured, suspended.
        return state.Equals("configured", StringComparison.OrdinalIgnoreCase)
            || state.Equals("addressed", StringComparison.OrdinalIgnoreCase)
            || state.Equals("suspended", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when an error message indicates HID/USB host link loss.</summary>
    public static bool IsDisconnectError(string? errorOrMessage)
    {
        if (string.IsNullOrWhiteSpace(errorOrMessage))
        {
            return false;
        }

        if (errorOrMessage.Contains(DisconnectedErrorCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Common Linux failures when the host unplugs the OTG cable mid-session.
        var markers = new[]
        {
            "ESHUTDOWN",
            "EPIPE",
            "ENODEV",
            "ENXIO",
            "Broken pipe",
            "No such device",
            "Input/output error",
            "not attached",
            "Device or resource busy",
        };

        return markers.Any(m => errorOrMessage.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Formats a disconnect error for injector/gRPC surfaces.</summary>
    public static string FormatDisconnectError(string detail)
    {
        return $"{DisconnectedErrorPrefix} {detail}";
    }
}
