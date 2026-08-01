# MouseKeyProxy handoff (maintenance)

**Status: feature complete; maintenance mode.**

Do not resume large feature work from this file. Current product guidance lives in:

- [README.md](README.md)
- [docs/USER-GUIDE.md](docs/USER-GUIDE.md)
- [docs/SECURITY-ADMIN-GUIDE.md](docs/SECURITY-ADMIN-GUIDE.md)
- [docs/Project/Requirements-Matrix.md](docs/Project/Requirements-Matrix.md)

Historical pickup notes (Pi Zero W ARMv6 stop, Orange Pi Rufus handoffs, etc.) are under [docs/historical/handoffs/](docs/historical/handoffs/).

## Maintenance posture

- Prefer small, reviewable fixes and dependency hygiene over new product surface.
- Keep HID-only keyboard/mouse path (control host → appliance → USB target).
- Keep paired mTLS for effect RPCs and folder share; do not reintroduce IP-list gRPC share gates.
- Rebuild artifacts with Nuke (`.\build.ps1 PackClientMsi`, `PublishPi`, etc.); do not commit `output/`.

## Lab operators

Agent install path: `C:\ProgramData\MouseKeyProxy\Agent\MouseKeyProxy.Agent.exe`  
Appliance share / WinFsp: see User Guide.  
SSH key for lab Pi (not in git): documented in historical handoffs only when still accurate.
