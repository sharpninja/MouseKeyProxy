using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Service;
using Xunit;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// FR-MKP-012: Verifies platform-aware logging configuration so the MKP service uses journald
/// (systemd console) on Linux/Pi and the Windows Event Log on Windows, never leaking the
/// EventLog provider onto the Linux path. Uses a real ServiceCollection and inspects the
/// registered <see cref="ILoggerProvider"/> instances.
/// </summary>
public class ServiceHostConfigurationTests
{
    private static ILoggerProvider[] ProvidersFor(bool isWindows)
    {
        var services = new ServiceCollection();
        services.AddLogging(lb => ServiceHostConfiguration.ConfigureLogging(lb, isWindows));
        var sp = services.BuildServiceProvider();
        return sp.GetServices<ILoggerProvider>().ToArray();
    }

    /// <summary>The non-Windows path registers no Event Log provider and does register a console (systemd) provider.</summary>
    [Fact]
    [Trait("Category", "Logging")]
    public void ConfigureLogging_NonWindows_UsesJournaldConsole_NoEventLog()
    {
        var providers = ProvidersFor(isWindows: false);

        Assert.DoesNotContain(providers, p => p.GetType().Name.Contains("EventLog", StringComparison.Ordinal));
        Assert.Contains(providers, p => p.GetType().Name.Contains("Console", StringComparison.Ordinal));
    }

    /// <summary>The Windows path registers the Event Log provider (skipped off Windows, where the provider is unavailable).</summary>
    [Fact]
    [Trait("Category", "Logging")]
    public void ConfigureLogging_Windows_RegistersEventLogProvider()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // EventLogLoggerProvider is Windows-only; assertion is meaningful only on Windows
        }

        var providers = ProvidersFor(isWindows: true);

        Assert.Contains(providers, p => p.GetType().Name.Contains("EventLog", StringComparison.Ordinal));
    }

    /// <summary>Passing a null logging builder throws, guarding the public contract.</summary>
    [Fact]
    [Trait("Category", "Logging")]
    public void ConfigureLogging_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ServiceHostConfiguration.ConfigureLogging(null!, false));
    }
}
