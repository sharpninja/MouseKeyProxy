# MouseKeyProxy User Guide

MouseKeyProxy lets one Windows keyboard and mouse drive a **Linux HID appliance** (Orange Pi or Raspberry Pi) that appears to a target PC as a standard USB keyboard and mouse. Pair the Windows control host to the appliance, toggle control with the configured hotkey, and use emergency release whenever control must return immediately to the host.

**Product scope:** mouse and keyboard proxying is **only** available through the Pi HID device path. Direct Windows-to-Windows mouse/key proxy is not a supported product mode. The target PC does not install MouseKeyProxy for input inject.

## What MouseKeyProxy Does

MouseKeyProxy provides:

- A Windows service and user-session agent on the **control host** (pairing, hotkeys, exclusive capture, dashboard, logs).
- The `mkp` .NET tool for install, pairing, toggle, emergency release, HID checks, and Pi provisioning.
- Hotkey-based control transfer. Edge-of-screen or mirror-mode behavior is not part of the product.
- A Linux **DeviceAppliance** service on the Pi that injects HID keyboard and relative mouse over USB OTG.
- Windows Event Log diagnostics on the control host.

MouseKeyProxy is intentionally exclusive on the control host: when forwarding is active, physical keyboard and mouse drive the HID target only. Local Windows applications should not receive normal keyboard or mouse events until forwarding is stopped, the toggle returns local, emergency release runs, or the device HID link is lost.

For Orange Pi Zero 2W appliances, a parametric OpenSCAD case (base + lid, M2.5 SHCS stack, multi-object 3MF for Orca) is documented under [cad/orange-pi-zero-2w/README.md](../cad/orange-pi-zero-2w/README.md) and linked from the Orange Pi HID guide.

## Main Concepts

**Control host:** the Windows machine whose physical keyboard and mouse are used. Runs Service + Agent + `mkp`.

**HID appliance (Pi):** the paired Linux device that receives gRPC input from the control host and emits USB HID reports. Orange Pi Zero 2W or Raspberry Pi Zero 2 W in the lab.

**Target PC (USB host):** the machine that has the Pi OTG/data USB cable plugged in. It only sees a keyboard and mouse. It does not need MouseKeyProxy installed for input.

**Pairing:** mTLS client cert after one-time code or ToFU discovery between control host and appliance.

**Forwarding active:** keyboard and mouse are captured on the control host and sent to the appliance for HID inject.

**Emergency release:** forced stop that tears down capture **on the host first**, then best-effort appliance cleanup, restoring full local keyboard and mouse.

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

1. Install or update `MouseKeyProxy.Repl` on the **Windows control host**.
2. Run `mkp service install` from an elevated shell on the control host; start the tray agent.
3. Provision and boot the **Pi HID appliance** (see Orange Pi / Raspberry Pi HID docs); confirm HID gadget and service are up.
4. Pair the control host to the appliance (`mkp pair discover` / code flow from the dashboard or CLI).
5. Plug the appliance USB data/OTG port into the **target PC**.
6. Confirm pair and HID state:

```powershell
mkp pair status
mkp agent status --json
mkp hid status
```

Until the control host is paired to a reachable appliance, remote-dependent actions should stay disabled in the UI.

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

## Device share (appliance thumb contents)

When the appliance has folder share enabled (`MKP_FOLDER_SHARE=1`), the control host can fully manage the sandboxed share root over paired gRPC (the same tree that seeds the USB mass-storage LUN):

```powershell
mkp share discover
mkp share info
mkp share list [dir]
mkp share get <remoteRelativePath> <localPath>
mkp share put <localPath> <remoteRelativePath>
mkp share mkdir <remoteRelativeDir>
mkp share rm <remoteRelativePath> [--recursive]
mkp share mv <fromRelative> <toRelative>
```

The Agent **Device management** form Share tab provides the same operations (list, download, upload, new folder, rename, delete) plus optional SMB UNC open when allowed by the device IP allowlist.

A Windows **virtual drive** mount that backs this share (so Explorer uses a drive letter) is a separate host-side feature. The recommended stack is **WinFsp** (Windows File System Proxy, with a .NET API): it presents a user-mode file system as a normal drive. That is not required for gRPC full control; it is the natural next layer for “looks like a USB disk on the control host.”

## Clipboard (control host)

Clipboard tools on the control host remain available for local convenience. They are not the HID keyboard/mouse product path.

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
