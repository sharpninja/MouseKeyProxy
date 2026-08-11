# MouseKeyProxy

Hotkey-only keyboard and mouse control from a Windows host through a small **USB HID appliance** (Orange Pi / Raspberry Pi). The control host captures input over an authenticated gRPC channel; the Pi injects standard USB keyboard and relative mouse into whatever PC it is plugged into. The target does not run MouseKeyProxy for keyboard and mouse.

This is **not** a Windows-to-Windows Mouse Without Borders clone. Mouse and key proxying is **only** supported through the HID device path.

MouseKeyProxy provides a Windows service and tray/dashboard agent on the control host, the `mkp` .NET tool for pairing, toggle, emergency release, and Pi provisioning, and a cross-platform service on the appliance.

**Project status: feature complete / maintenance mode.** Prefer bug fixes, security updates, and dependency hygiene over new product features. Historical plans, audits, and receipts live under [docs/historical/](docs/historical/).

## Documentation

- [User Guide](docs/USER-GUIDE.md)
- [Security Administration Guide](docs/SECURITY-ADMIN-GUIDE.md)
- [Pi service deployment](docs/deployment/Pi-Service-Deployment.md)
- [Orange Pi Zero 2 / 2W HID](docs/hardware/orange-pi-zero-2-hid.md)
- [Orange Pi Zero 2W printable case (OpenSCAD)](cad/orange-pi-zero-2w/README.md)
- [Raspberry Pi Zero 2 W HID](docs/hardware/pi-zero-2-hid.md)
- [Logo Branding Contract](assets/logo.branding.md)
- [Requirements matrix](docs/Project/Requirements-Matrix.md)
- [Historical plans, audits, and receipts](docs/historical/README.md)

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

- **HID-only product path:** keyboard and mouse leave the Windows host only toward a paired Linux USB HID appliance (Pi), not as a software agent on the target PC.
- Explicit hotkey toggle only; no mirror mode and no edge-of-screen switching.
- Exclusive input capture on the control host so the HID target receives keyboard and mouse while forwarding is active.
- A Windows busy pointer marks remote-control ownership on the host and returns to the normal pointer whenever local control is restored.
- **Host restore first:** toggle, emergency release, and HID link-loss always unhook local capture before any peer RPC; a hung appliance cannot trap the control host.
- Pairing (mTLS + one-time code or ToFU discovery), status, service lifecycle, emergency release, logs, and HID diagnostics through the canonical `mkp` CLI/REPL surface. `mkp pair discover` lists live gRPC hosts and a mint/pair hint when ToFU has no unpaired advertisers. `mkp toggle` drives the local Agent capture (same as Ctrl+Win+F1).
- **Agent startup self-heal:** reloads peer credentials, prefers settings remote URL, probes mTLS, may recover via an alternate live gRPC host; logs under `%LOCALAPPDATA%\MouseKeyProxy\logs\self-heal.log`.
- Linux USB HID gadget: keyboard + relative mouse; letters, digits, and US punctuation map to boot-protocol HID usages (works at login / firmware screens that ignore high-level remotes).
- User-session dashboard for pairing state, active appliance, service state, recent errors, and emergency release.
- **Appliance folder share (gRPC):** when `MKP_FOLDER_SHARE=1` on the Pi, the paired control host can list, download, upload, mkdir, rename, and delete under a sandboxed share root (same tree that seeds the USB mass-storage LUN). Access is **paired mTLS identity only** (no operator IP list for gRPC).
- **WinFsp virtual drive (Windows control host):** optional drive letter mount of that share in the tray Agent user session (`mkp share mount` / Agent **Mount appliance share…**). Requires the WinFsp runtime; the Windows service does not mount (Session 0).
- **Client MSI / install kit:** Nuke `PackClientMsi` stages `output/payloads/client-install/` (Service + Agent + bootstrap + `MouseKeyProxy-Client.msi`) for MKP-DEPLOY and USB LUN install media.
- Windows Event Log diagnostics on the control host.

## Known Compatible Hardware

Verified configurations:

- **Control host:** Windows 11 x64 with the tray Agent (logon task typically `C:\ProgramData\MouseKeyProxy\Agent\MouseKeyProxy.Agent.exe`) and local Service.
- **HID appliance (required for mouse/key proxy):** Orange Pi Zero 2W (aarch64 / `linux-arm64`). Control over Wi-Fi or Ethernet gRPC; keyboard/mouse over USB OTG to the target PC. See [orange-pi-zero-2-hid.md](docs/hardware/orange-pi-zero-2-hid.md).
- **HID appliance (alternate):** Raspberry Pi Zero 2 W. Provisioned by `mkp pi provision` / Rufus profiles with Raspberry Pi OS Lite; USB HID gadget via configfs + dwc2. See [pi-zero-2-hid.md](docs/hardware/pi-zero-2-hid.md).
- **Target PC:** any machine that accepts a USB keyboard and mouse. It does not run MouseKeyProxy for input inject.
- **SD media:** microSD card (4 GB or larger) written with the bundled "RUFUS For MouseKeyProxy" writer or board-specific prepare scripts.

Notes:

- The HID appliance is headless by design; local HDMI is optional. Prefer a solid mini-HDMI (or board HDMI) cable if you need a local console.
- Direct Windows-to-Windows mouse/key proxy is not a supported product mode.

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
.\build.ps1 PackClientMsi --configuration Release
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

MouseKeyProxy is **source-available** under the project [LICENSE](LICENSE):

- **Non-commercial** personal, educational, evaluation, and research use is permitted under that license.
- **Commercial use** is available **only under a separate royalty agreement**. Contact **ninja@thesharp.ninja** for commercial and royalty terms.

The `mkp` tool bundles a modified build of **Rufus** ("RUFUS For MouseKeyProxy"),
used by `mkp pi provision` to write the Pi HID image to SD media. Rufus is
Copyright (C) 2011-2026 Pete Batard / Akeo Consulting and is licensed under
**GPLv3** (mere aggregation - it does not relicense MouseKeyProxy). Corresponding
source: https://github.com/sharpninja/rufus-mkp. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
