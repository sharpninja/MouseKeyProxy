# MouseKeyProxy User Guide

MouseKeyProxy lets one Windows 11 keyboard and mouse control one paired Windows 11 peer at a time. It is designed for an explicit two-machine workflow: pair the machines, choose which side is active, toggle control with the configured hotkey, and use emergency release whenever control must return immediately to the local machine.

## What MouseKeyProxy Does

MouseKeyProxy provides:

- A Windows service that exposes the local gRPC endpoint and management operations.
- A user-session agent with tray/dashboard controls for pairing, active peer state, forwarding state, emergency release, and logs.
- The `mkp` .NET tool for the canonical command-line control surface.
- Hotkey-based control transfer. Edge-of-screen or mirror-mode behavior is not part of the product.
- Optional clipboard synchronization with local privacy controls.
- Windows Event Log diagnostics under the MouseKeyProxy application log.

MouseKeyProxy is intentionally exclusive: when forwarding is active, the physical keyboard and mouse drive the remote peer only. Local Windows applications should not receive normal keyboard or mouse events until forwarding is stopped or emergency release is triggered.

## Main Concepts

Primary system: the machine whose physical keyboard and mouse are currently being used.

Remote peer: the paired machine that receives forwarded input when remote control is active.

Pairing: the trust setup that allows exactly two endpoints to recognize one another.

Forwarding active: keyboard and mouse input is being captured locally and sent to the remote peer.

Emergency release: a forced stop that tears down the active control path and restores full local keyboard and mouse input.

## Installation

Install the command-line tool:

```powershell
dotnet tool install --global MouseKeyProxy.Repl
```

Update the command-line tool:

```powershell
dotnet tool update --global MouseKeyProxy.Repl
```

Install the local service from an elevated shell:

```powershell
mkp service install
```

Check service state:

```powershell
mkp service status
```

The service payload is installed under the product-managed service location. Do not manually copy service binaries out of the .NET tool cache.

## First-Time Setup

1. Install or update `MouseKeyProxy.Repl` on both machines.
2. Run `mkp service install` from an elevated shell on both machines.
3. Start the agent on each user desktop session.
4. Pair the two machines using the pairing code flow shown by the agent dashboard or command line.
5. Confirm pair state with:

```powershell
mkp pair status
mkp agent status --json
```

A machine that is not paired has no remote endpoint. Remote-dependent actions should remain disabled in the UI until a paired and reachable peer is available.

## Daily Operation

Use the agent dashboard for normal status and emergency controls. Use `mkp` for repeatable operation, scripting, and diagnostics.

Common commands:

```powershell
mkp status
mkp status --json
mkp pair status
mkp agent status
mkp agent status --json
mkp toggle
mkp emergency-release
mkp open-logs
```

The CLI/REPL is the canonical implementation of the control surface. UI actions should call shared command implementations and should not expose controls that cannot also be operated through `mkp`.

## Hotkeys

The default local toggle hotkey is Ctrl+Alt+F1. The configured toggle hotkey transfers active control between local and remote operation when the machines are paired and connected.

When remote forwarding is active:

- Normal keyboard and mouse input is forwarded to the remote peer.
- Local applications should not receive those forwarded events.
- The explicit control chord remains local so control can be toggled back.
- Emergency release remains available from the dashboard or CLI.

If a hotkey does not work, check the agent status and Windows Event Log before reinstalling anything.

## Dashboard

The agent dashboard should show:

- Pairing state.
- Active peer.
- Service endpoint and service state.
- Clipboard state.
- Recent errors.
- Controls that are valid for the current state.

When not paired or not connected, remote-dependent controls should be disabled. The dashboard should still allow pairing, log access, and local status operations.

## Clipboard

Clipboard synchronization is intended for small, recent text payloads. The product keeps a bounded local clipboard history and skips private or unsupported content. Treat clipboard sync as convenience data movement, not as a secure file-transfer channel.

Use:

```powershell
mkp clipboard
mkp clipboard clear
```

## Event Logs

MouseKeyProxy writes operational logs to Windows Event Logs, not arbitrary per-user log folders. Open the product log with:

```powershell
mkp open-logs
```

Use the Event Viewer path for the MouseKeyProxy application log when collecting evidence for support or administration.

## Emergency Release

Use emergency release whenever input appears stuck, a peer becomes unreachable, or local control must be restored immediately:

```powershell
mkp emergency-release
```

Emergency release should:

- Stop active forwarding.
- Terminate the active gRPC control path.
- Release cursor clipping.
- Release pressed modifier state.
- Restore normal local keyboard and mouse behavior on the primary system.

Emergency release may be called from either side. If a remote-side release cannot reach the peer, the local side still performs defensive cleanup.

## Troubleshooting

Check basic state:

```powershell
mkp status --json
mkp agent status --json
mkp service status
```

If pairing fails:

- Verify both machines are on the expected network.
- Verify both services are installed and running.
- Verify the displayed pairing code has not expired.
- Check the MouseKeyProxy Event Log on both machines.

If the toggle hotkey fails:

- Confirm the agent is running in the interactive user session.
- Confirm the machines are paired and connected.
- Confirm no other application has captured the same hotkey.
- Use `mkp agent status --json` to verify the forwarding state.

If input is forwarded but local applications still receive it, stop using the session and trigger emergency release. That behavior violates the exclusive-control model and should be treated as a bug.

## Uninstall

Stop forwarding first, then remove the service from an elevated shell:

```powershell
mkp emergency-release
mkp service stop
mkp service uninstall
```

Then remove the .NET tool if desired:

```powershell
dotnet tool uninstall --global MouseKeyProxy.Repl
```
