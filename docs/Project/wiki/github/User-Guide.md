# MouseKeyProxy User Guide

MouseKeyProxy lets one Windows keyboard and mouse control a paired remote: either another Windows peer or a Linux **HID appliance** (Orange Pi / Raspberry Pi) that appears to a target PC as a standard USB keyboard and mouse. Pair endpoints, toggle control with the configured hotkey, and use emergency release whenever control must return immediately to the host.

## What MouseKeyProxy Does

MouseKeyProxy provides:

- A Windows service that exposes the local gRPC endpoint and management operations.
- A user-session agent with tray/dashboard controls for pairing, active peer state, forwarding state, emergency release, and logs.
- The `mkp` .NET tool for the canonical command-line control surface.
- Hotkey-based control transfer. Edge-of-screen or mirror-mode behavior is not part of the product.
- Optional clipboard synchronization with local privacy controls (Windows peers / advanced effects).
- Windows Event Log diagnostics under the MouseKeyProxy application log.
- Optional Linux USB HID gadget peer for keyboard and mouse inject into a physical PC (no software on the target).

MouseKeyProxy is intentionally exclusive: when forwarding is active, the physical keyboard and mouse drive the remote (or HID target) only. Local Windows applications should not receive normal keyboard or mouse events until forwarding is stopped, the toggle returns local, emergency release runs, or the device HID link is lost.

For Orange Pi Zero 2W appliances, a parametric OpenSCAD case (base + lid, M2.5 SHCS stack, multi-object 3MF for Orca) is documented under [cad/orange-pi-zero-2w/README.md](../cad/orange-pi-zero-2w/README.md) and linked from the Orange Pi HID guide.

## Main Concepts

Primary system (control host): the machine whose physical keyboard and mouse are currently being used.

Remote peer / device appliance: the paired endpoint that receives forwarded input when remote control is active. For HID, this is typically the Pi over gRPC; the **USB host PC** is the machine that receives HID reports from the Pi OTG port (it need not run MouseKeyProxy for keyboard/mouse).

Pairing: the trust setup (mTLS client cert after one-time code or ToFU discovery) that allows endpoints to recognize one another.

Forwarding active: keyboard and mouse input is being captured locally and sent to the device appliance / peer.

Emergency release: a forced stop that tears down capture **on the host first**, then best-effort peer cleanup, restoring full local keyboard and mouse.

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

Defaults (overridable in `%LOCALAPPDATA%\MouseKeyProxy\hotkey-config.json`):

- **Remote activation / toggle:** F1 with any two of Ctrl, Alt, and Win. Ctrl+Win+F1 remains the default configured binding; legacy Ctrl+Alt+F2 is still recognized by the capture hook.
- **Emergency release:** F3 with any two of Ctrl, Alt, and Win. Ctrl+Alt+F3 remains the default configured binding.

The toggle transfers active control between local and remote when the host is paired and connected to the device endpoint.

When remote forwarding is active:

- Normal keyboard and mouse input is forwarded to the device appliance (Pi HID path for inject).
- Local applications should not receive those forwarded events.
- The host mouse pointer shows the Windows busy cursor until control returns locally; toggle-off, emergency release, disconnect/HID-loss fallback, watchdog fallback, and application exit restore the normal pointer.
- Escape chords are handled **inside the capture hook first** so the host can always get control back even if RegisterHotKey never sees the keys.
- Host restore does **not** wait on peer RPC: unhook and local modifiers clear first; device clear-modifiers / emergency is best-effort in the background.
- If the appliance loses USB HID link to its host (`DEVICE_HID_DISCONNECTED` / UDC not attached), the agent falls back to local control automatically and shows a tray notice.

If a hotkey does not work, confirm the tray agent process path is the current build (scheduled task `MouseKeyProxyTray` should run `C:\ProgramData\MouseKeyProxy\Agent\MouseKeyProxy.Agent.exe`), then check agent status and the Windows Event Log before reinstalling.

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

- Stop active forwarding and uninstall low-level hooks **before** any device RPC.
- Release cursor clipping.
- Release pressed modifier state on the host.
- Restore normal local keyboard and mouse behavior on the primary system.
- Optionally notify the device appliance (clear-modifiers / emergency) with a short timeout in the background.

Emergency release may be called from the tray, dashboard, hotkey, or CLI. If a remote-side release cannot reach the peer, the local side still restores control.

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

## Linux HID Appliance (Optional)

MouseKeyProxy can drive a physical board that presents itself to a **target PC** as a standard USB HID keyboard and mouse. The control host talks to the appliance over gRPC (mTLS). Keyboard and mouse inject use boot-protocol HID reports, including US letters, digits, and punctuation (comma, period, brackets, quotes, and related OEM keys).

Topology:

1. Control host (Windows Agent + hotkeys).
2. Appliance (Orange Pi Zero 2W or Raspberry Pi Zero 2 W) on the network.
3. Target PC: USB host of the appliance OTG/data port; receives HID only (no MKP required for inject).

### Orange Pi Zero 2 / Zero 2W (recommended lab path)

See [docs/hardware/orange-pi-zero-2-hid.md](hardware/orange-pi-zero-2-hid.md) and
[docs/deployment/Pi-Service-Deployment.md](deployment/Pi-Service-Deployment.md).

```powershell
pwsh -ExecutionPolicy Bypass -File scripts/pi/publish-pi-hid.ps1 -Rid linux-arm64 -Service
# Board-specific SD prepare (example):
pwsh -ExecutionPolicy Bypass -File scripts/pi/prepare-orange-pi-zero-2-sd.ps1 -DiskNumber <N> -PublishService
```

Publish RID is **`linux-arm64`**. Pair with `mkp pair discover` (ToFU) or code-based pair. Service install path on appliance: `/opt/mousekeyproxy/MouseKeyProxy.Service` under `mousekeyproxy.service`.

### Raspberry Pi Zero 2 W

The `mkp` tool can provision SD media via the bundled "RUFUS For MouseKeyProxy" writer:

```powershell
mkp pi provision
```

Options:

```powershell
mkp pi provision --url <IMAGE_URL> --sha256 <HASH|skip> --stage-root <DIR> --profile <NAME> --force --no-launch
```

- `--url` overrides the image URL; pass `--sha256 <hash>` for the new image, or `--sha256 skip` to disable verification.
- `--stage-root` sets the staging directory (default `%LOCALAPPDATA%\MouseKeyProxy\pi-stage`).
- `--profile` selects the named Pi HID profile passed to the writer.
- `--force` re-downloads even if a staged copy exists.
- `--no-launch` stages the image without launching the writer.

Details: [docs/hardware/pi-zero-2-hid.md](hardware/pi-zero-2-hid.md). Prefer current Raspberry Pi OS Lite 64-bit (trixie) images when provisioning; override URL/hash as needed.

### HID link loss

If the appliance USB gadget is not attached to a host (UDC state not configured / transport errors such as broken pipe), inject returns `DEVICE_HID_DISCONNECTED`. The control-host agent restores local keyboard and mouse immediately and shows a tray notification.

First boot is unattended and self-healing. It sets the hostname (default
`mkp-hid-pi`), creates the user (default `mkp`) with key-only SSH, joins Wi-Fi via
a NetworkManager connection, enables SSH, and stages the USB HID gadget. It writes
`mkp-firstboot.log` to the boot partition and always reboots even if a step fails,
so the board never bricks or loops. After first boot the Pi is reachable by
hostname over the lab network, for example:

```powershell
ssh -i <path-to-private-key> mkp@mkp-hid-pi
```

The Raspberry Pi OS image does not include a .NET runtime; the HID appliance
service is published self-contained for `linux-arm` and deployed separately.
See `docs/hardware/pi-zero-2-hid.md` for the appliance build and setup details.

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
