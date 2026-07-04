# Technical Requirements (MCP Server)

## TR-HOTKEY-CONTRACT-001

**Hotkey seam compile contracts** — The .NET 10 solution must compile with interface-only seams and behaviorless stubs for hotkey toggle phase 1.
Scope: layer-1+

## TR-MKP-REPL-001

**REPL install and payload publish** — Self-contained payloads bundled in nupkg; Kestrel ListenAnyIP 50051 HTTP/2; fresh publish before pack.
Scope: layer-1+

## TR-MKP-SEC-001

**Security negative and pairing auth** — Unpaired and bad-secret requests rejected before effect.
Scope: layer-1+

## TR-OWNERSHIP-CONTRACT-001

**Ownership seam compile contracts** — The .NET 10 solution must compile ownership and input boundary contracts without implementing authorization behavior.
Scope: layer-1+

