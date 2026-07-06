using Grpc.Core;
using Microsoft.Extensions.Logging;
using NSubstitute;
using MouseKeyProxy.Network.V1;
using Xunit;

namespace MouseKeyProxy.Service.Tests;

public class LoggingTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Pair_Success_LogsInformation_Via_ILogger()
    {
        var logger = Substitute.For<ILogger<MouseKeyProxyImpl>>();
        var impl = new MouseKeyProxyImpl(logger);

        var req = new PairRequest { PeerId = "peer42", PairingCode = "valid-test" };
        var resp = await impl.Pair(req, Substitute.For<ServerCallContext>());

        Assert.True(resp.Success);

        // This verifies that the code under test uses ILogger (which is configured to EventLog)
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Pair succeeded for PeerId=peer42")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Pair_Failure_LogsWarning()
    {
        var logger = Substitute.For<ILogger<MouseKeyProxyImpl>>();
        var impl = new MouseKeyProxyImpl(logger);

        var req = new PairRequest { PeerId = "bad", PairingCode = "wrong" };
        var resp = await impl.Pair(req, Substitute.For<ServerCallContext>());

        Assert.False(resp.Success);
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Pair failed")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    [Trait("Category", "Logging")]
    public void Service_Configures_Dedicated_MouseKeyProxy_EventLog()
    {
        var sourcePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Service", "Program.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("options.SourceName = \"MouseKeyProxy\"", source, StringComparison.Ordinal);
        Assert.Contains("options.LogName = \"MouseKeyProxy\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("options.LogName = \"Application\"", source, StringComparison.Ordinal);
    }
}
