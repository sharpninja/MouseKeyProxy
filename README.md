# MouseKeyProxy

Free hotkey-only alternative to PowerToys Mouse Without Borders. Forward keyboard and mouse from a Windows control host to a paired peer or a USB HID appliance over an authenticated gRPC channel.

MouseKeyProxy provides a Windows service, a user-session tray/dashboard agent, and the `mkp` .NET tool for pairing, toggle, emergency release, and Pi provisioning.

## Documentation

- [User Guide](docs/USER-GUIDE.md)
- [Security Administration Guide](docs/SECURITY-ADMIN-GUIDE.md)
- [Pi service deployment](docs/deployment/Pi-Service-Deployment.md)
- [Orange Pi Zero 2 / 2W HID](docs/hardware/orange-pi-zero-2-hid.md)
- [Raspberry Pi Zero 2 W HID](docs/hardware/pi-zero-2-hid.md)
- [Logo Branding Contract](assets/logo.branding.md)
- [Requirements matrix](docs/Project/Requirements-Matrix.md)
- [Historical plans, audits, handoffs, and receipts](docs/historical/README.md)

## Quick Start

```powershell
dotnet tool install --global MouseKeyProxy.Repl
mkp --version
mkp --help
mkp service install
mkp pair status
mkp toggle
```

Default configured toggle: **Ctrl+Win+F1**. Default configured emergency release: **Ctrl+Alt+F3**. Both are configurable via the agent hotkey config under `%LOCALAPPDATA%\MouseKeyProxy\hotkey-config.json`. As fixed safety fallbacks, **F1 with any two of Ctrl, Alt, and Win** activates the remote toggle and **F3 with any two** triggers emergency release.

## Features

- Explicit hotkey toggle only; no mirror mode and no edge-of-screen switching.
- Exclusive input forwarding so one machine (or HID target) receives keyboard and mouse at a time.
- **Host restore first:** toggle, emergency release, and HID link-loss always unhook local capture before any peer RPC; a hung appliance cannot trap the control host.
- Pairing (mTLS + one-time code or ToFU discovery), status, service lifecycle, emergency release, logs, clipboard, and remote-control commands through the canonical `mkp` CLI/REPL surface.
- Optional **DeviceAppliance** path: Linux USB HID gadget (keyboard + relative mouse) as a full paired peer; letters, digits, and US punctuation map to boot-protocol HID usages.
- User-session dashboard for pairing state, active peer, service state, clipboard state, recent errors, and emergency release.
- Windows Event Log diagnostics.
- LIFO clipboard sync with bounded history and privacy skips (advanced peer effects; not required for HID inject).

## Known Compatible Hardware

Verified configurations:

- **Control host:** Windows 11 x64 with the tray Agent (logon task typically `C:\ProgramData\MouseKeyProxy\Agent\MouseKeyProxy.Agent.exe`).
- **Windows peer (optional):** second Windows 11 machine for clipboard / advanced effects when not using a pure HID appliance.
- **Linux HID appliance (recommended lab path):** Orange Pi Zero 2W (aarch64 / `linux-arm64`). See [orange-pi-zero-2-hid.md](docs/hardware/orange-pi-zero-2-hid.md). Control over Wi-Fi gRPC; keyboard/mouse over USB OTG to the target PC.
- **Raspberry Pi HID appliance:** Raspberry Pi Zero 2 W. Provisioned by `mkp pi provision` / Rufus profiles with Raspberry Pi OS Lite; USB HID gadget via configfs + dwc2. See [pi-zero-2-hid.md](docs/hardware/pi-zero-2-hid.md).
- **SD media:** microSD card (4 GB or larger) written with the bundled "RUFUS For MouseKeyProxy" writer or board-specific prepare scripts.

Notes:

- The HID appliance is headless by design; local HDMI is optional. Prefer a solid mini-HDMI (or board HDMI) cable if you need a local console.

## Build

```powershell
dotnet tool restore
dotnet build MouseKeyProxy.slnx -c Release
dotnet test MouseKeyProxy.slnx -c Release
```

Nuke lives in `build/MouseKeyProxy.Build.csproj`. From the **repo root**, use the bootstrap scripts
(`build.ps1` / `build.cmd` / `build.sh`) so target names work like a normal Nuke workspace:

```powershell
.\build.ps1 PackRepl --configuration Release
.\build.ps1 PublishToolToNuGet --configuration Release

# Rufus (requires local rufus-mkp; default sibling ../rufus-mkp or RUFUS_MKP_ROOT)
.\build.ps1 BuildRufus
.\build.ps1 LaunchRufus --RufusProfile default
.\build.ps1 CreatePiImage --RufusProfile default
# alias: CreateImageFromRufusConfig

# Full SD card build: PublishPi + PackClientMsi/StagePiInstallMedia + unattended Rufus write/eject
.\build.ps1 BuildSdCard --RufusProfile default
# Interactive Rufus GUI: --AutoWrite false; pin reader: --RufusDevice N
# optional: --ForcePiImage
# optional env: MKP_INSTALL_TICKET, MKP_DEVICE_GRPC, MKP_DEVICE_PEER_ID
```

Equivalent without bootstrap: `dotnet run --project build/MouseKeyProxy.Build.csproj -- --target <Name>`.

Versions are produced by GitVersion. NuGet publishing requires the current commit to be the latest tagged commit and reads the API key from `NUGET_API_KEY`.

## License

MouseKeyProxy's own code is Apache-2.0. See [LICENSE](LICENSE).

The `mkp` tool bundles a modified build of **Rufus** ("RUFUS For MouseKeyProxy"),
used by `mkp pi provision` to write the Pi HID image to SD media. Rufus is
Copyright (C) 2011-2026 Pete Batard / Akeo Consulting and is licensed under
**GPLv3** (mere aggregation - it does not relicense MouseKeyProxy). Corresponding
source: https://github.com/sharpninja/rufus-mkp. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
