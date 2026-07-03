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

## FR-MKP-007 Full logging via ILogger to Windows Event Viewer

All service components (gRPC service, pairing, lifecycle, watchdog, connection management) and key agent operations must log exclusively through Microsoft.Extensions.Logging.ILogger<T> (or ILoggerFactory). The service host must be configured to write logs to the Windows Event Log (Event Viewer) using the EventLog provider under source "MouseKeyProxy" / log "Application".

Acceptance Criteria:
- Service startup, shutdown, gRPC session open/close, Pair success/failure, input batch processing (at Information or Debug), errors, and failsafe triggers are logged with appropriate LogLevel (Information, Warning, Error, Critical).
- Logs are visible in Event Viewer > Windows Logs > Application with Source = "MouseKeyProxy".
- Structured logging properties (e.g. PeerId, Seq, ErrorCode) are preserved in EventData.
- No Console.WriteLine or direct EventLog.WriteEntry in production service code paths (use ILogger).
- During `mkp service install` (elevated), the EventLog source is created if missing.
- Log level can be controlled via configuration (appsettings.json or command line).
- REPL and tray use ILogger for their operations where relevant (console provider + EventLog where appropriate for agent actions).
- Unit/component tests verify logging calls using NSubstitute for ILogger without side effects on the real Event Log.

Scope: layer-1+ (service primary, agent/tray secondary)

