using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Agent;

internal sealed class AgentControlPipeServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRemoteDesktopController _desktopController;
    private readonly IInputInjector _inputInjector;
    private readonly Func<AgentControlRequest, AgentControlResponse>? _pairingStateNotifier;
    private readonly Func<AgentControlResponse>? _statusProvider;
    private readonly Func<AgentControlRequest, AgentControlResponse>? _emergencyReleaseHandler;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private AgentControlPipeServer(
        IRemoteDesktopController desktopController,
        IInputInjector inputInjector,
        Func<AgentControlRequest, AgentControlResponse>? pairingStateNotifier,
        Func<AgentControlResponse>? statusProvider,
        Func<AgentControlRequest, AgentControlResponse>? emergencyReleaseHandler)
    {
        _desktopController = desktopController;
        _inputInjector = inputInjector;
        _pairingStateNotifier = pairingStateNotifier;
        _statusProvider = statusProvider;
        _emergencyReleaseHandler = emergencyReleaseHandler;
        _loop = Task.Run(RunAsync);
    }

    public static AgentControlPipeServer Start(
        IRemoteDesktopController desktopController,
        IInputInjector inputInjector,
        Func<AgentControlRequest, AgentControlResponse>? pairingStateNotifier = null,
        Func<AgentControlResponse>? statusProvider = null,
        Func<AgentControlRequest, AgentControlResponse>? emergencyReleaseHandler = null)
    {
        return new AgentControlPipeServer(desktopController, inputInjector, pairingStateNotifier, statusProvider, emergencyReleaseHandler);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown should not block tray exit.
        }

        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    AgentControlPipe.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(_stop.Token);
                var response = Handle(line);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(250, _stop.Token).ConfigureAwait(false);
            }
        }
    }

    private AgentControlResponse Handle(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return AgentControlResponse.Failure("EMPTY_REQUEST", "Agent control request was empty.");
        }

        try
        {
            var request = JsonSerializer.Deserialize<AgentControlRequest>(line, JsonOptions);
            if (request is null)
            {
                return AgentControlResponse.Failure("BAD_REQUEST", "Agent control request could not be parsed.");
            }

            return request.Operation switch
            {
                AgentControlPipe.SetMousePosition => ToResponse(_desktopController.SetMousePosition(request.DisplayId, request.X, request.Y)),
                AgentControlPipe.SetFocusByHwnd => ToResponse(_desktopController.SetFocusByHwnd(request.Hwnd, request.BringToFront)),
                AgentControlPipe.LocateProcess => LocateProcess(request),
                AgentControlPipe.InjectInput => InjectInput(request),
                AgentControlPipe.NotifyPairingState => NotifyPairingState(request),
                AgentControlPipe.GetAgentStatus => GetAgentStatus(),
                AgentControlPipe.EmergencyRelease => EmergencyRelease(request),
                _ => AgentControlResponse.Failure("UNKNOWN_OPERATION", $"Unknown agent control operation: {request.Operation}")
            };
        }
        catch (Exception ex)
        {
            return AgentControlResponse.Failure("AGENT_CONTROL_ERROR", ex.Message);
        }
    }

    private AgentControlResponse LocateProcess(AgentControlRequest request)
    {
        var nodes = _desktopController.LocateProcess(request.ProcessName, request.Pid);
        return new AgentControlResponse
        {
            Ok = true,
            ErrorCode = "0",
            Message = $"located {nodes.Count} windows",
            Nodes = new System.Collections.Generic.List<RemoteWindowNode>(nodes)
        };
    }

    private AgentControlResponse InjectInput(AgentControlRequest request)
    {
        return _inputInjector.TryInjectBatch(request.Events, out var error)
            ? AgentControlResponse.Success($"injected {request.Events.Count} events")
            : AgentControlResponse.Failure("INJECT_INPUT_FAILED", error ?? "Input injection failed.");
    }

    private AgentControlResponse NotifyPairingState(AgentControlRequest request)
    {
        return _pairingStateNotifier?.Invoke(request)
            ?? AgentControlResponse.Failure("PAIRING_STATE_UNAVAILABLE", "Agent pairing state handler is not configured.");
    }

    private AgentControlResponse GetAgentStatus()
    {
        return _statusProvider?.Invoke()
            ?? AgentControlResponse.Failure("AGENT_STATUS_UNAVAILABLE", "Agent status handler is not configured.");
    }

    private AgentControlResponse EmergencyRelease(AgentControlRequest request)
    {
        return _emergencyReleaseHandler?.Invoke(request)
            ?? AgentControlResponse.Failure("EMERGENCY_RELEASE_UNAVAILABLE", "Agent emergency release handler is not configured.");
    }

    private static AgentControlResponse ToResponse(RemoteControlResult result)
    {
        return new AgentControlResponse
        {
            Ok = result.Ok,
            ErrorCode = result.ErrorCode,
            Message = result.Message
        };
    }
}
