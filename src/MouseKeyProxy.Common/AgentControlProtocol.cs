using System.Collections.Generic;

namespace MouseKeyProxy.Common;

public static class AgentControlPipe
{
    public const string PipeName = "MouseKeyProxy.Agent.Control";

    public const string SetMousePosition = "setMousePosition";
    public const string LocateProcess = "locateProcess";
    public const string SetFocusByHwnd = "setFocusByHwnd";
    public const string InjectInput = "injectInput";
    public const string NotifyPairingState = "notifyPairingState";
    public const string GetAgentStatus = "getAgentStatus";
    public const string EmergencyRelease = "emergencyRelease";
}

public sealed class AgentControlRequest
{
    public string Operation { get; set; } = string.Empty;
    public string DisplayId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public uint Pid { get; set; }
    public ulong Hwnd { get; set; }
    public bool BringToFront { get; set; }
    public string RemotePeer { get; set; } = string.Empty;
    public string RemoteGrpcUrl { get; set; } = string.Empty;
    public string PairingCode { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public bool NotifyPeer { get; set; }
    public List<InputEvent> Events { get; set; } = new();
}

public sealed class AgentControlResponse
{
    public bool Ok { get; set; }
    public string ErrorCode { get; set; } = "0";
    public string Message { get; set; } = string.Empty;
    public string RemotePeer { get; set; } = string.Empty;
    public string RemoteGrpcUrl { get; set; } = string.Empty;
    public string RemoteState { get; set; } = string.Empty;
    public bool ForwardingActive { get; set; }
    public List<RemoteWindowNode> Nodes { get; set; } = new();

    public static AgentControlResponse Success(string message = "ok") => new()
    {
        Ok = true,
        ErrorCode = "0",
        Message = message
    };

    public static AgentControlResponse Failure(string errorCode, string message) => new()
    {
        Ok = false,
        ErrorCode = errorCode,
        Message = message
    };
}
