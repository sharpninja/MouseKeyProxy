# MouseKeyProxy

Free hotkey-only alternative to PowerToys Mouse Without Borders for exactly two Windows 11 systems.

MouseKeyProxy provides a Windows service, a user-session tray/dashboard agent, and the `mkp` .NET tool for pairing two machines and transferring keyboard/mouse ownership with an explicit hotkey.

## Documentation

- [User Guide](docs/USER-GUIDE.md)
- [Security Administration Guide](docs/SECURITY-ADMIN-GUIDE.md)
- [Logo Branding Contract](assets/logo.branding.md)
- [Current implementation plan](docs/PLAN-MKP-006.md)

## Quick Start

```powershell
dotnet tool install --global MouseKeyProxy.Repl
mkp --version
mkp --help
mkp service install
mkp pair status
mkp toggle
```

Default hotkey: Ctrl+Alt+F1.

## Features

- Explicit hotkey toggle only; no mirror mode and no edge-of-screen switching.
- Exclusive input forwarding so one machine receives keyboard and mouse at a time.
- Pairing, status, service lifecycle, emergency release, logs, clipboard, and remote-control commands through the canonical `mkp` CLI/REPL surface.
- User-session dashboard for pairing state, active peer, service state, clipboard state, recent errors, and emergency release.
- Windows Event Log diagnostics.
- LIFO clipboard sync with bounded history and privacy skips.

## Known Compatible Hardware

Verified configurations:

- **Control and target hosts:** Windows 11 x64. MouseKeyProxy pairs exactly two Windows 11 machines.
- **Raspberry Pi HID appliance:** Raspberry Pi Zero 2 W (Rev 1.0). Provisioned by `mkp pi provision` with Raspberry Pi OS Lite 64-bit (Debian 13 "trixie", 13.5; kernel `6.18.34+rpt-rpi-v8`). First boot, Wi-Fi join, and key-only SSH are verified; the USB HID gadget (keyboard and mouse via configfs, dwc2 peripheral mode) is configured at provision time.
- **SD media:** microSD card (4 GB or larger) written with the bundled "RUFUS For MouseKeyProxy" writer.

Notes:

- The Pi HID appliance is headless by design; local HDMI is optional. The Pi Zero 2 W outputs at most ~1080p and cannot drive 1440p or ultrawide monitors at native resolution. If you need a local console, use a solid mini-HDMI adapter and cable (a marginal link can flap EDID and fail to sync).

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
