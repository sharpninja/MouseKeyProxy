using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Testing;
using MouseKeyProxy.Commands;
using NSubstitute;
using Wire = MouseKeyProxy.Network.V1;
using Client = MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient;
using Xunit;

namespace MouseKeyProxy.Commands.Tests;

/// <summary>
/// FR-MKP-006 / TR-MKP-AGENTIPC-001: verifies the shared RemoteServiceCommands surface that the REPL and
/// tray agent both invoke - it maps the gRPC responses to RemoteControlResult and returns NOT_PAIRED
/// (without dialing) when the client factory yields no client.
/// </summary>
public class RemoteServiceCommandsTests
{
    private static AsyncUnaryCall<Wire.CommandResult> Call(Wire.CommandResult result) =>
        TestCalls.AsyncUnaryCall(
            Task.FromResult(result),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    /// <summary>ClearModifiers dispatches to the client and maps the response fields.</summary>
    [Fact]
    [Trait("Category", "SharedCommands")]
    public async Task ClearModifiers_DispatchesToClient_AndMapsResponse()
    {
        var client = Substitute.For<Client>();
        client.ClearModifiersAsync(Arg.Any<Wire.ClearModifiersRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Call(new Wire.CommandResult { Ok = true, Err = "0", Msg = "modifiers cleared" }));

        var commands = new RemoteServiceCommands(() => client);
        var result = await commands.ClearModifiersAsync("peer-a", "corr-1");

        Assert.True(result.Ok);
        Assert.Equal("modifiers cleared", result.Message);
    }

    /// <summary>EmergencyRelease dispatches to the client and maps a failure response.</summary>
    [Fact]
    [Trait("Category", "SharedCommands")]
    public async Task EmergencyRelease_MapsFailureResponse()
    {
        var client = Substitute.For<Client>();
        client.EmergencyReleaseAsync(Arg.Any<Wire.EmergencyReleaseRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Call(new Wire.CommandResult { Ok = false, Err = "AGENT_IPC_UNAVAILABLE", Msg = "no agent" }));

        var commands = new RemoteServiceCommands(() => client);
        var result = await commands.EmergencyReleaseAsync("peer-a", "corr-2");

        Assert.False(result.Ok);
        Assert.Equal("AGENT_IPC_UNAVAILABLE", result.ErrorCode);
    }

    /// <summary>A null client (unpaired) short-circuits to NOT_PAIRED without dialing.</summary>
    [Theory]
    [Trait("Category", "SharedCommands")]
    [InlineData("clear")]
    [InlineData("emergency")]
    [InlineData("inject")]
    public async Task NullClient_Returns_NotPaired(string op)
    {
        var commands = new RemoteServiceCommands(() => null);

        var result = op switch
        {
            "clear" => await commands.ClearModifiersAsync("p", "c"),
            "emergency" => await commands.EmergencyReleaseAsync("p", "c"),
            _ => await commands.InjectTextAsync("hello"),
        };

        Assert.False(result.Ok);
        Assert.Equal("NOT_PAIRED", result.ErrorCode);
    }
}
