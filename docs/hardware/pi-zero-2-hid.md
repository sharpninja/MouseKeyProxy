# Raspberry Pi Zero 2 HID Appliance

MouseKeyProxy supports a physical Raspberry Pi Zero 2 HID appliance path for lab validation and future backend work. The appliance is implemented in C# on .NET 10 and is published self-contained for `linux-arm64`.

## Topology

- Control host: `payton-legion2`
- Target host: `payton-desktop`
- Appliance hostname: `mkp-hid-pi`
- Control channel: Wi-Fi HTTP to the .NET service
- Input channel: Pi USB data port enumerating as standard HID keyboard and relative mouse

## Operating System

The appliance runs Raspberry Pi OS Lite provisioned by `mkp pi provision`:

- Distribution: Raspberry Pi OS Lite, 64-bit (arm64), based on Debian 13 "trixie" (13.5).
- Kernel: `6.18.34+rpt-rpi-v8` (aarch64).
- Default image: `2026-06-18-raspios-trixie-arm64-lite.img.xz`.
- Board: Raspberry Pi Zero 2 W (Rev 1.0 verified).
- Networking: NetworkManager (Wi-Fi keyfile written at first boot).
- Access: SSH enabled, key-only; default hostname `mkp-hid-pi`, default user `mkp`.
- The image ships no .NET runtime; the HID service is published self-contained for `linux-arm64` and deployed separately (below).

First boot is unattended and self-healing: it configures hostname, user,
authorized SSH key, Wi-Fi, sudo policy, and the USB HID gadget, logs to
`mkp-firstboot.log` on the boot partition, and always reboots (even on a failed
step) so the board never bricks or loops.

## Build

```powershell
dotnet publish src/MouseKeyProxy.PiHid/MouseKeyProxy.PiHid.csproj -c Release -r linux-arm64 --self-contained true
```

The publish helper wraps the same command:

```powershell
pwsh -ExecutionPolicy Bypass -File scripts/pi/publish-pi-hid.ps1
```

## Pi Setup

The recommended path is `mkp pi provision` (see the User Guide), which writes the
image and applies the first-boot configuration in step 1 automatically. The manual
steps below remain valid if you provision the card yourself.

1. Install Raspberry Pi OS Lite 64-bit (Debian 13 trixie, arm64) and set hostname `mkp-hid-pi`. `mkp pi provision` does this and the SSH/Wi-Fi/user setup for you.
2. Copy the published `linux-arm64` output to `/opt/mousekeyproxy/pi-hid`.
3. Copy `scripts/pi/pi-hid.env.sample` to `/etc/mousekeyproxy/pi-hid.env` and set `MKP_HID_PI_TOKEN`.
4. Run `scripts/pi/setup-configfs-gadget.sh` as root to create `/dev/hidg0` and `/dev/hidg1`.
   The script writes HID report descriptors as **binary** (base64-decoded) and
   verifies lengths (keyboard 63, mouse 52). Do **not** use dash `printf '\x05...'`
   to populate `report_desc`: on Raspberry Pi OS `/bin/sh` is dash, which writes
   the literal ASCII `\x05` text and breaks host interrupt polling (writes to
   `/dev/hidg*` then fail with EAGAIN).
5. For gadget mode on Zero 2 W, `config.txt` must expose only
   `dtoverlay=dwc2,dr_mode=peripheral`. Comment out stock `otg_mode=1` and
   `dtoverlay=dwc2,dr_mode=host` (they conflict with the peripheral gadget).
6. Install `scripts/pi/mousekeyproxy-pihid.service` into `/etc/systemd/system/`, then run `systemctl enable --now mousekeyproxy-pihid.service`.

## Endpoints

- `GET /status`
- `POST /keyboard/report`
- `POST /mouse/report`
- `POST /clear-modifiers`
- `POST /reset`

All endpoints require `Authorization: Bearer <MKP_HID_PI_TOKEN>`.

## Validation

Use the CLI from the control host:

```powershell
$env:MKP_HID_PI_HOST = 'mkp-hid-pi'
$env:MKP_HID_PI_TOKEN = '<token>'
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- hid provision-check
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- hid test-key --chord alt+space
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- hid test-mouse --dx 40 --dy 0
```

Transition acceptance still requires WindowProbe JSON and timestamped screenshot metadata from the target host.
