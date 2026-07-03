# MouseKeyProxy

Free hotkey-only alternative to PowerToys Mouse Without Borders for exactly two Windows 11 systems.

See full plan: docs/PLAN-MKP-004.md and https://github.com/sharpninja/MouseKeyProxy

## Quick start (REPL)
dotnet tool install --global MouseKeyProxy.Repl

mkp --help
mkp service install   # (elevated, uses powershell.exe (5.1))
mkp pair ...
mkp toggle

Hotkeys: Ctrl-Alt-F1 (local), Ctrl-Alt-F2 (remote) - configurable.

## Features (v1)
- Hotkey toggle only (no edge mouse)
- LIFO clipboard sync (DPAPI encrypted, ~50 cap, privacy skip)
- gRPC (TLS) + advanced controls (Inject, SetMousePos, LocateProcess, SetFocus)
- Service + tray + REPL management
- Failsafes: emergency release, mod resync, clip release <2s

## Build
dotnet build
dotnet test

See Nuke in build/ (basic targets).

## License

 (workspace re-anchored edit for harness visibility)
MIT

(Extensive docs in repo + wiki.)
