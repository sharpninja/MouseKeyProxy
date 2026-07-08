using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Service;

internal sealed class AgentControlPipeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan _connectTimeout;

    public AgentControlPipeClient(TimeSpan? connectTimeout = null)
    {
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(2);
    }

    public AgentControlResponse Send(AgentControlRequest request)
    {
        try
        {
            // TR-MKP-SEC-001: present the per-session token the agent minted (read fresh so a token
            // rotation on agent restart is picked up without restarting the service).
            request.AuthToken = AgentControlTokenStore.Read(AgentControlTokenStore.DefaultPath()) ?? string.Empty;

            using var pipe = new NamedPipeClientStream(
                ".",
                AgentControlPipe.PipeName,
                PipeDirection.InOut,
                PipeOptions.None);

            pipe.Connect((int)_connectTimeout.TotalMilliseconds);

            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);

            writer.WriteLine(JsonSerializer.Serialize(request, JsonOptions));
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return AgentControlResponse.Failure("AGENT_IPC_EMPTY_RESPONSE", "Agent control pipe returned no response.");
            }

            return JsonSerializer.Deserialize<AgentControlResponse>(line, JsonOptions)
                ?? AgentControlResponse.Failure("AGENT_IPC_BAD_RESPONSE", "Agent control pipe returned an unreadable response.");
        }
        catch (TimeoutException ex)
        {
            return AgentControlResponse.Failure("AGENT_IPC_UNAVAILABLE", ex.Message);
        }
        catch (IOException ex)
        {
            return AgentControlResponse.Failure("AGENT_IPC_UNAVAILABLE", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return AgentControlResponse.Failure("AGENT_IPC_DENIED", ex.Message);
        }
        catch (Exception ex)
        {
            return AgentControlResponse.Failure("AGENT_IPC_ERROR", ex.Message);
        }
    }
}

internal sealed class AgentPipeRemoteDesktopController : IRemoteDesktopController
{
    private readonly AgentControlPipeClient _client;

    public AgentPipeRemoteDesktopController(AgentControlPipeClient client)
    {
        _client = client;
    }

    public RemoteControlResult SetMousePosition(string displayId, int x, int y)
    {
        var response = _client.Send(new AgentControlRequest
        {
            Operation = AgentControlPipe.SetMousePosition,
            DisplayId = displayId,
            X = x,
            Y = y
        });

        return ToResult(response);
    }

    public IReadOnlyList<RemoteWindowNode> LocateProcess(string processName, uint pid)
    {
        var response = _client.Send(new AgentControlRequest
        {
            Operation = AgentControlPipe.LocateProcess,
            ProcessName = processName,
            Pid = pid
        });

        if (!response.Ok)
        {
            return Array.Empty<RemoteWindowNode>();
        }

        return response.Nodes;
    }

    public RemoteControlResult SetFocusByHwnd(ulong hwnd, bool bringToFront)
    {
        var response = _client.Send(new AgentControlRequest
        {
            Operation = AgentControlPipe.SetFocusByHwnd,
            Hwnd = hwnd,
            BringToFront = bringToFront
        });

        return ToResult(response);
    }

    private static RemoteControlResult ToResult(AgentControlResponse response)
    {
        return new RemoteControlResult(response.Ok, response.ErrorCode, response.Message);
    }
}

internal sealed class AgentPipeInputInjector : IInputInjector
{
    private readonly AgentControlPipeClient _client;

    public AgentPipeInputInjector(AgentControlPipeClient client)
    {
        _client = client;
    }

    public void Send(InputEvent evt)
    {
        if (!TryInjectBatch(new[] { evt }, out var error))
        {
            throw new InvalidOperationException(error ?? "Agent input injection failed.");
        }
    }

    public bool TryInjectBatch(IEnumerable<InputEvent> events, out string? error)
    {
        var response = _client.Send(new AgentControlRequest
        {
            Operation = AgentControlPipe.InjectInput,
            Events = new List<InputEvent>(events)
        });

        error = response.Ok ? null : $"{response.ErrorCode}: {response.Message}";
        return response.Ok;
    }
}

internal sealed class AgentPipeEmergencyReleaseController : IEmergencyReleaseController
{
    private readonly AgentControlPipeClient _client;

    public AgentPipeEmergencyReleaseController(AgentControlPipeClient client)
    {
        _client = client;
    }

    public RemoteControlResult EmergencyRelease(string peerId, string correlationId)
    {
        var response = _client.Send(new AgentControlRequest
        {
            Operation = AgentControlPipe.EmergencyRelease,
            RemotePeer = peerId,
            CorrelationId = correlationId
        });

        return new RemoteControlResult(response.Ok, response.ErrorCode, response.Message);
    }
}

internal sealed class AgentPipeModifierReleaseController : IModifierReleaseController
{
    private readonly AgentControlPipeClient _client;

    public AgentPipeModifierReleaseController(AgentControlPipeClient client)
    {
        _client = client;
    }

    public RemoteControlResult ClearModifiers(string peerId, string correlationId)
    {
        var response = _client.Send(new AgentControlRequest
        {
            Operation = AgentControlPipe.ClearModifiers,
            RemotePeer = peerId,
            CorrelationId = correlationId
        });

        return new RemoteControlResult(response.Ok, response.ErrorCode, response.Message);
    }
}

internal sealed class AgentPipeScreenshotCapture : IScreenshotCapture
{
    private readonly AgentControlPipeClient _client;

    public AgentPipeScreenshotCapture(AgentControlPipeClient client)
    {
        _client = client;
    }

    public ScreenshotCaptureResult Capture(ScreenshotCaptureRequest request)
    {
        var response = _client.Send(new AgentControlRequest
        {
            Operation = AgentControlPipe.CaptureScreenshot,
            ScreenshotTarget = request.Target.ToString(),
            Hwnd = request.Hwnd,
            CorrelationId = request.CorrelationId,
            IncludeCursor = request.IncludeCursor
        });

        if (!response.Ok)
        {
            throw new InvalidOperationException($"{response.ErrorCode}: {response.Message}");
        }

        var target = Enum.TryParse<ScreenshotTarget>(response.ScreenshotTarget, ignoreCase: true, out var parsed)
            ? parsed
            : request.Target;

        return new ScreenshotCaptureResult(
            new ScreenshotMetadata(
                response.CapturedAtUtc,
                response.SourceHost,
                response.CorrelationId,
                target,
                response.Hwnd,
                response.Width,
                response.Height,
                response.Sha256),
            response.ScreenshotPng);
    }
}