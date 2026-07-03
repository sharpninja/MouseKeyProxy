# Functional Requirements (MCP Server)

## FR-MKP-001 Hotkey toggle only

Support configurable hotkey (default local Ctrl-Alt-F1, remote Ctrl-Alt-F2) to switch active without edge mouse move. AC: Hotkey switches focus + proxy direction on both. No auto edge crossing (ClipCursor + ownership rules). Assert no pointer transition occurs at screen edges while inactive/active (no edge hooks in this product); configurable + persisted.
Scope: layer-1+

## FR-MKP-002 Keyboard focus follows

When active machine changes via hotkey, keyboard input (and mouse when applicable) is proxied to the current focused/active machine. Focus follows the toggle.
Scope: layer-1+

## FR-MKP-003 Full proxy

Proxy input per support matrix (ordinary keys, modifiers, media, Win combos where permitted; explicitly excludes SAS/Ctrl+Alt+Del, secure desktop, lock/login screens, UIPI-blocked scenarios) with observable-failure semantics (fail observably, never hang or claim success). AC: Supported events work; unsupported fail observably, no hang or false success.
Scope: layer-1+

## FR-MKP-004 Real-time clipboard LIFO sync

Real-time clipboard sync between paired systems. Merge history as LIFO stack (newest on top). Max ~50 entries. Persist locally in user LocalAppData encrypted with DPAPI. Any copy on one immediately available on other.
Scope: layer-1+

## FR-MKP-005 gRPC advanced controls

Support gRPC calls from host: InjectInput (to specific remotes), SetMousePosition (display + x/y without focus change), LocateProcess (by name or PID return hwnd tree), SetFocusByHwnd (focus + optional bring front).
Scope: layer-1+

## FR-MKP-006 Setup/REPL/service

REPL manages pairing (UDP broadcast and/or mDNS LAN discovery (no UPnP IGD/NAT port mapping) + key negotiation/persist), settings, explicit service lifecycle (install/uninstall reverse fw using Windows PowerShell 5.1 `powershell.exe`), clipboard ops, toggle. REPL is the primary management UX. Explicit `mkp service install` (not automatic on tool install). Tray (WinForms) actions use shared command implementation library (no per-click spawn). .NET 10 + director workspace. ACs as in plan.
Scope: layer-1+

