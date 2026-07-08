using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MouseKeyProxy.Common;
using MouseKeyProxy.Service;
using Xunit;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// TEST-MKP-002 / TR-MKP-ARCH-001: verifies the ownership boundary - the Service never performs Win32
/// input P/Invoke (SendInput / RegisterHotKey / ClipCursor). Those belong to the user-session Agent;
/// the Service reaches input only through the injected <see cref="IInputInjector"/> seam.
/// </summary>
public class OwnershipArchitectureTests
{
    /// <summary>The Service assembly declares no forbidden Win32 input P/Invoke entry points.</summary>
    [Fact]
    [Trait("Category", "Architecture")]
    public void Service_HasNo_Win32_Input_PInvoke()
    {
        var forbidden = new[] { "SendInput", "RegisterHotKey", "ClipCursor", "SetCursorPos", "keybd_event", "mouse_event" };
        var assembly = typeof(MouseKeyProxyImpl).Assembly;

        var offenders = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(m => m.GetCustomAttribute<DllImportAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.EntryPoint ?? string.Empty)
            .Where(entry => forbidden.Any(f => string.Equals(f, entry, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>The Service reaches input only through the IInputInjector seam (constructor dependency).</summary>
    [Fact]
    [Trait("Category", "Architecture")]
    public void Dispatcher_DependsOn_InputInjector_Seam()
    {
        var ctor = typeof(SessionFrameDispatcher).GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(IInputInjector), paramTypes);
    }
}
