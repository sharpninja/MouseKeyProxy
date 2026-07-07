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

## Build

```powershell
dotnet tool restore
dotnet build MouseKeyProxy.slnx -c Release
dotnet test MouseKeyProxy.slnx -c Release
```

Nuke targets live in `build/`:

```powershell
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target PackRepl --configuration Release
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target PublishToolToNuGet --configuration Release
```

Versions are produced by GitVersion. NuGet publishing requires the current commit to be the latest tagged commit and reads the API key from `NUGET_API_KEY`.

## License

MouseKeyProxy's own code is Apache-2.0. See [LICENSE](LICENSE).

The `mkp` tool bundles a modified build of **Rufus** ("RUFUS For MouseKeyProxy"),
used by `mkp pi provision` to write the Pi HID image to SD media. Rufus is
Copyright (C) 2011-2026 Pete Batard / Akeo Consulting and is licensed under
**GPLv3** (mere aggregation - it does not relicense MouseKeyProxy). Corresponding
source: https://github.com/sharpninja/rufus-mkp. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
