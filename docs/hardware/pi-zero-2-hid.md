# Raspberry Pi Zero 2 HID Appliance

MouseKeyProxy supports a physical Raspberry Pi Zero 2 HID appliance path for lab validation and future backend work. The appliance is implemented in C# on .NET 10 and is published self-contained for `linux-arm64`.

## Topology

- Control host: `payton-legion2`
- Target host: `payton-desktop`
- Appliance hostname: `mkp-hid-pi`
- Control channel: Wi-Fi HTTP to the .NET service
- Input channel: Pi USB data port enumerating as standard HID keyboard and relative mouse

## Build

```powershell
dotnet publish src/MouseKeyProxy.PiHid/MouseKeyProxy.PiHid.csproj -c Release -r linux-arm64 --self-contained true
```

The publish helper wraps the same command:

```powershell
pwsh -ExecutionPolicy Bypass -File scripts/pi/publish-pi-hid.ps1
```

## Pi Setup

1. Install Raspberry Pi OS Lite 64-bit and set hostname `mkp-hid-pi`.
2. Copy the published `linux-arm64` output to `/opt/mousekeyproxy/pi-hid`.
3. Copy `scripts/pi/pi-hid.env.sample` to `/etc/mousekeyproxy/pi-hid.env` and set `MKP_HID_PI_TOKEN`.
4. Run `scripts/pi/setup-configfs-gadget.sh` as root to create `/dev/hidg0` and `/dev/hidg1`.
5. Install `scripts/pi/mousekeyproxy-pihid.service` into `/etc/systemd/system/`, then run `systemctl enable --now mousekeyproxy-pihid.service`.

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
