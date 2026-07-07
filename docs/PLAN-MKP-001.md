# PLAN-MKP-001: MouseKeyProxy - Free Hotkey-Only Alternative to PowerToys Mouse Without Borders

**Status**: Superseded. This is the historical version. The current working plan has been forked to `docs/PLAN-MKP-002.md`. All prior reviews incorporated there.

**Workspace root**: `f:\github\MouseKeyProxy`

**Date**: 2026-07-02 / 2026-07-03 (revised 2026-07-03 after Codex review)

## Context
The user wants a custom, free, open-source alternative to Microsoft's PowerToys Mouse Without Borders (MWB) for **exactly two Windows 11 systems** (one host + one remote in v1). The architecture is designed to be extensible but v1 gates are strictly two-node.

Core requirements from user:
- Mouse toggles **only** via hotkey (Ctrl-Alt-F1 local / F2 remote, configurable). No auto edge crossing. Use ClipCursor to maintain boundaries.
- Keyboard focus follows the mouse toggle.
- Proxy keyboard and mouse input per the explicit support matrix (ordinary keys, modifiers, media, Win combos where permitted; explicitly excludes SAS/Ctrl+Alt+Del, secure desktop, lock/login screens, UIPI-blocked scenarios).
- Clipboard sharing: real-time sync + LIFO (stack) history merge (max ~50), persisted locally encrypted using DPAPI.
- Setup: f:\github\MouseKeyProxy root; git init; `director add-workspace`; GitHub repo sharpninja/MouseKeyProxy (origin); .NET 10; manage entirely via project's own PowerShell REPL global dotnet tool; settings in %LOCALAPPDATA%\MouseKeyProxy; register/run as Windows service.
- REPL tool: pairing (UPnP/broadcast discovery + key negotiation/persist), settings, service start/stop/uninstall + reverse firewall (elevate Windows PowerShell 5.1 (powershell.exe; always present in-box on Win11; pwsh is PS7+)), clipboard ops, toggle. REPL is the primary management UX. Explicit `mkp service install` (not automatic on tool install).
- App itself is **NOT** MCP-aware (plain .NET service + WinForms tray for desktop interaction).
- Tray (WinForms): actions (start/stop conns/service, inject text, emergency release, SetMousePos) invoke shared REPL command implementation (no per-click spawn).
- gRPC for comms (TLS + REPL-negotiated secrets).
- New gRPC: InjectInput, SetMousePosition (display/pos without focus change), LocateProcess (name/PID -> hwnd tree), SetFocusByHwnd.
- Nuke build (like McpServer).
- Extensive REPL --help + repo docs.
- Post v1/Transition: social drafts, logo/branding (mouse at desk typing with monitors), 30s video scripts, AIUnit reviews (separate release plan).

Previous MWB issues (service flakiness, UseService flips, socket errors) motivated custom focused design.

This follows the Byrd Development Process V4 strictly (tests first with mocks/stubs, validate, then impl, 100% green suite before exiting any gate) mapped to RUP phases. All plans are decision-complete before construction.

**Source control note**: GitHub repo added explicitly as `origin` per user instruction (https://github.com/sharpninja/MouseKeyProxy.git). This project treats GitHub as the working origin for this repository. (Global rules note AzDO as primary for other repos; this one follows the explicit add-github-repo directive.)

## Codex + Claude Round 2 Review Processing Summary
All required Round 2 fixes (R2-BLK-1/2/3, R2-MAJ-1-6, minors) have been incorporated in this pass:
- R2-BLK-1: Stale duplicate Recommended Approach deleted; single-voice document.
- R2-BLK-2: Identity table added; pairing secrets/settings ownership consistent with agent (CurrentUser DPAPI); service non-secret only.
- R2-BLK-3: Test projects + xUnit v3 + NSubstitute (never Moq) + seams (IInputSource etc.) added to Critical Files; red/green for additional TESTs; full inline mappings; evidence export in docs/requirements-matrix.yaml; TODO contradiction resolved in text.
- R2-MAJ-1: Proto completed (HwndNode, SetFocus, typed wheel/xbutton/text, Control/Heartbeat/Ack, OpenSession bidi); topology stated (remote dials host, one inbound rule).
- R2-MAJ-2: Hook state machine for chords (including emergency and remote Ctrl-Alt-F2); modifier key-ups on *every* toggle; disambiguated.
- R2-MAJ-3: Mouse delta source (WM_INPUT RIDEV_INPUTSINK), pinning (1x1 rect), injection model, PerMonitorV2 TR, stable displayId added.
- R2-MAJ-4: Privacy formats handled with test; file-drop deferred; ordering (receive-time + seq); persistence json decided.
- R2-MAJ-5: mTLS + pinned thumbprints chosen; ProgramData ACL hardening; bind policy; UPnP IGD excluded; Locate restrictions enumerated.
- R2-MAJ-6: Latency TR (<25 ms p95), coalescing, tray timeouts, soak gate (24h), Elaboration measurement prototype + contingency.
- Minors: pwsh terminology, global.json rollForward latestFeature, LICENSE in files, AC verbiage, watcher wording, etc.

Wireframes spec re-homed here as deliverable blocking tray tests. Post-completion successor plan ID: docs/PLAN-MKP-002-Release.md.

## Recommended Approach
.NET 10 Windows service (Microsoft.Extensions.Hosting.WindowsServices + UseWindowsService + current-dir fix for SCM) **for non-interactive concerns only**.

- User-session tray/agent (WinForms or minimal console + tray) owns all interactive desktop work.
- Win32 P/Invoke (in tray/agent only): SetWindowsHookEx (LL KB/mouse), SendInput (full supported events), RegisterHotKey, ClipCursor, AddClipboardFormatListener, etc.
- Local IPC (named pipe or loopback gRPC with mutual auth) between tray/agent and service for commands and events.
- gRPC + protobuf (TLS + REPL negotiated secrets per pair) between machines. Reliable sequenced events with ack/backpressure.
- REPL-driven key negotiation + persist (DPAPI).
- Real-time clipboard (tray/agent listener): send to peer; receiver merges LIFO; cap ~50; at-rest encrypted.
- Settings: %LOCALAPPDATA%\MouseKeyProxy\settings.json (Options + Json). Secrets DPAPI-protected.
- Service: networking, pairing state, persistence, watchdog, lifecycle only.
- Service registration: explicit commands in REPL (adapts McpServer patterns). Uninstall reverses fw.
- No edge mouse: explicit hotkey only. ClipCursor on active (tray/agent). Hooks swallow on inactive.
- v1: exactly two nodes (host + one remote). Multi-hosting extensible in data model but not required for v1 gates.
- REPL is the management surface. `dotnet tool install` installs the tool; `mkp service install` (elevated) does payload copy + sc create + fw. 

Reuse heavily (read-only exploration performed):
- McpServer patterns for service helper, repl install, Nuke, workspace flow, UseWindowsService + dir fix.
- LocalAppData from FunWasHad-reconstruct.
- Input event inspiration from Avalonia.RemoteControl.
- .NET 10 + build props from aiUnit.
- Wireframe review style from RiskyStars.

## Critical Files to Create/Modify (ownership-aware)
- global.json (net10 SDK)
- Directory.Build.props
- MouseKeyProxy.slnx (or .sln)
- src/MouseKeyProxy.Service/ (service-only: networking, pairing, persistence, watchdog; no input)
- src/MouseKeyProxy.Agent/ (user-session tray/agent: Interop/Win32Input.cs (hooks, SendInput, ClipCursor etc.), hotkeys, clipboard listener, tray UI/forms, IPC client to service)
- src/MouseKeyProxy.Common/ (shared: contracts, proto, IPC messages, settings, support matrix)
- src/MouseKeyProxy/Network/mousekeyproxy.proto + generated
- src/MouseKeyProxy.Repl/ (global tool package: explicit `mkp service install` etc.; shared command lib reused by tray; --help with repo links)
- build/Build.cs (Nuke)
- scripts/ (Manage-MouseKeyProxyService.ps1, firewall helpers)
- tests/MouseKeyProxy.Common.Tests/ (xUnit v3 + NSubstitute; seams and pure logic)
- tests/MouseKeyProxy.Agent.Tests/ (xUnit v3 + NSubstitute; hook state machine, LIFO, input matrix, failsafes, mouse capture)
- tests/MouseKeyProxy.Service.Tests/ (xUnit v3 + NSubstitute; pairing, auth, watchdog, IPC server)
- tests/MouseKeyProxy.Integration/ (two-machine harness; E2E with real hooks/SendInput/gRPC; requires physical or VM pair; uses xUnit v3 but no mocking library)
- .gitignore
- README.md (extensive)
- LICENSE (MIT)
- docs/PLAN-MKP-001.md (this, for peer review) + guides + wireframes/
- assets/ (logo: mouse at desk typing surrounded by monitors)
- AGENTS-README-FIRST.yaml (via director)
- docs/todo.yaml (MCP)

## Test Architecture (addresses R2-BLK-3, R2-BLK-4, Byrd compliance)
**Project rules:**
- Always xUnit v3
- Never Moq, always NSubstitute
- **Subagent tasks must use MCP TODOs** (see TODO PLAN-SUBAGENT-001): Every subagent task MUST be backed by an MCP TODO (created via client.Todo.CreateAsync or equivalent) that includes clear scope, implementation plan (as implementationTasks list), and dependencies. Subagents are required to keep the TODO up-to-date (using client.Todo.UpdateAsync) throughout their work. All work must adhere to MCP session logging (workflow.sessionlog.beginTurn / append* / completeTurn or client.SessionLog methods) for every turn/action. Never read/write todo.yaml directly.

Framework: xUnit v3 with `[Fact]`, `[Theory]`, traits via `[Trait("Category", "MKP-IPC")]` for gate filtering. Mocking library: NSubstitute. No heavy UI automation in unit layer.

Seams / interfaces (all defined in Common, implemented thinly in Agent/Service; business logic only behind seams):
- `IInputSource` : observable stream of low-level events (LL hook or raw input adapter).
- `IInputInjector` : SendInput / text inject / SetCursorPos wrapper.
- `IHotkeyMonitor` : chord detection state machine (hook path only for remote mode).
- `IClipboardAccessor` : listen, set, read formats, privacy stamp check.
- `ICursorClip` : ClipCursor + query + release watcher.
- `ISystemClock` / `ISequenceClock` for timing and seq.
- `IPeerTransport` : gRPC bidi stream abstraction for tests (in-memory or fake).
- `ILocalIpc` : named-pipe or loopback client/server seam.

Mock strategy: Win32 adapters are untestable thin wrappers (P/Invoke only). All toggle state, LIFO merge, chord logic, routing, auth, LIFO, failsafe decision trees unit-tested against the interfaces. Integration layer exercises real adapters on one machine; two-machine harness only for cross-machine FRs.

CI vs physical: Unit + component tests (mocks + fakes) run in CI on any agent. Integration/E2E (real hooks + SendInput + gRPC between two machines) run in manual or lab gate only. Soak (24h) is Transition only.

Red/green examples (see full table in Requirements):
- TEST-MKP-001 red: no state flip on chord when inactive; green: state flips + injection direction changes + ClipCursor called only on active side.
- Similar one-liners for 002-008 below.

Inception exit gate: every FR has >=1 TEST with concrete red assertion, green assertion, and exact filter command; full matrix in MCP + appendix.

## Process Ownership and IPC Architecture (addresses B1)
**Decision (v1 and ongoing)**:
- **Service process** (runs as Windows service, Session 0 or appropriate): owns networking/gRPC server+client, non-secret peer state (addresses, thumbprints, seq counters), watchdog/reconnect, service lifecycle, firewall coordination via REPL. (No user secrets, no %LOCALAPPDATA% user data.)
- **User-session tray/agent** (WinForms tray launched at user login, full desktop session): owns low-level hooks (SetWindowsHookEx WH_KEYBOARD_LL / WH_MOUSE_LL), hotkey registration (RegisterHotKey), clipboard listener (AddClipboardFormatListener), ClipCursor, SendInput, GetCursorPos, foreground/focus/window ops (Locate/SetFocus), tray UI and forms.
- **Local IPC boundary** (mandatory, never bypass): named pipe (or loopback secured gRPC) between tray/agent and service. Authenticated (local secret or pipe ACL), with timeouts, reconnect, and explicit failure semantics (e.g. fallback to local-only mode). Commands from REPL or tray flow through this boundary when they require desktop interaction.
- **Startup model**: Service starts at boot (SCM). Tray/agent starts via HKCU Run or scheduled task (user login), connects to local service over IPC. No input code lives in the service binary.

All feature implementation must name the owning process and IPC contract in code comments and tests. Mocks will enforce the boundary.

## Identity and Persistence Table (addresses R2-BLK-2, R2-BLK-5, Codex H2)
**Decision**: Pairing/TLS material and user settings live in the user-session agent only. Service holds only non-secret state (peer addresses, thumbprints, sequence counters, watchdog config). This eliminates the LocalSystem / CurrentUser DPAPI mismatch.

| Artifact                  | Owning Process     | Windows Account | Path pattern                                      | DPAPI Scope     | Round-trip Test |
|---------------------------|--------------------|-----------------|---------------------------------------------------|-----------------|-----------------|
| settings.json            | Agent             | Interactive user| %LOCALAPPDATA%\MouseKeyProxy\settings.json       | CurrentUser    | Agent unit test |
| pairing secrets / cert private key | Agent      | Interactive user| %LOCALAPPDATA%\MouseKeyProxy\identity\*.pfx or key| CurrentUser    | Agent test (with mock store) |
| TLS server cert (self-signed) | Agent (host) / per machine | LocalSystem or user (ACL'd) | ProgramData\MouseKeyProxy\certs\host.pfx (ACL: service read, admin write) | None (machine store preferred) or LocalMachine + entropy | Service + agent test |
| clipboard-history.json   | Agent             | Interactive user| %LOCALAPPDATA%\MouseKeyProxy\clipboard-history.json | CurrentUser    | Agent LIFO test |
| Service binaries/payload | mkp service install | Admin write     | %ProgramData%\MouseKeyProxy\ (ACL: Administrators full, Users read+exec) | N/A            | install/uninstall test verifies ACLs |
| Logs (service)           | Service           | LocalSystem     | %ProgramData%\MouseKeyProxy\logs\*.log            | N/A            | Service test |
| Non-secret peer state    | Service           | LocalSystem     | %ProgramData%\MouseKeyProxy\peers.json            | LocalMachine + entropy | Service unit |

Pairing flow uses agent on both sides for secret exchange and pinning. Service only receives thumbprint + non-secret config over local IPC. All DPAPI CurrentUser operations happen exclusively in the agent process.

## Keyboard and Input Support Matrix (addresses B2, H1, M2, M4)
Supported (normal SendInput path):
- Ordinary virtual keys, scan codes, modifiers (Ctrl/Alt/Shift/Win), Win+ combos where Windows permits from the calling integrity level.
- Media keys, function keys, mouse buttons + wheel (XButton too), mouse move/absolute.
- Text injection via SendInput or Clipboard where appropriate.

Conditionally supported (document + test per layout/elevation):
- IME, dead keys, differing keyboard layouts between machines (scan code mode preferred; layout translation via REPL config).
- Elevated target windows (UIPI may block; graceful no-op + log + user notification).

Explicitly **not supported** (must fail observably, never hang or claim success):
- Ctrl+Alt+Del / Secure Attention Sequence (SAS).
- Secure desktop, lock screen, login screen, UAC prompts.
- Any input blocked by Windows integrity policy or UIPI.

**Hotkey chord detection (R2-MAJ-2)**: Toggle (Ctrl-Alt-F1 local, Ctrl-Alt-F2 remote) and emergency release chords are detected **inside the LL keyboard hook state machine** on the agent (not RegisterHotKey). RegisterHotKey is only a convenience in local/idle mode. On every toggle transition, the losing side synthesizes key-ups for all currently-down modifiers (Ctrl, Alt, Shift, Win). This is tested with the lost-key-up harness. Chord for remote is the full Ctrl-Alt-F2 (bare F2 collides with Rename).

The proto and event handling will carry sufficient fields for the supported set and will explicitly reject or drop unsupported. See updated proto below.

Failsafe: emergency local hotkey (configurable, always local, hook-detected) to release ClipCursor, clear hooks, enter local-only mode. 

## Mouse Capture and Injection Strategy (addresses R2-MAJ-3)
- Delta source while remote: WM_INPUT with RIDEV_INPUTSINK (preferred; hardware deltas, survives clipping) or MSLLHOOKSTRUCT deltas against pinned point. Agent declares PerMonitorV2 awareness (TR).
- Pinning: While remote-active, ClipCursor to a 1x1 rect at the last known local position (or freeze) so the local cursor does not wander. OS resets clip on desktop switch / logon.
- Injection: raw deltas sent; remote applies its own acceleration (remote feel). Or absolute normalized (VIRTUALDESK | ABSOLUTE) for consistency. Decision documented in code + test.
- Display: addressed by stable device name / GUID (not enumeration index). Coordinate space (per-monitor logical) documented in TR and SetMousePosition AC.
- TR-MKP-INPUT-002 (new): Agent must be PerMonitorV2; coordinates round-trip correctly on mixed-DPI.

Failsafe: emergency local hotkey (configurable, always local, hook-detected) to release ClipCursor, clear hooks, enter local-only mode.

## REPL and Service Deployment Contract (addresses B4, H5)
- `dotnet tool install --global MouseKeyProxy.Repl` installs the REPL tool only. It does **not** register any service.
- `mkp service install` (requires elevation): extracts/copies service payload (from tool package or side-by-side), `sc create` "MouseKeyProxy", adds firewall rules (via elevated pwsh 5.1 helper), starts and validates.
- `mkp service uninstall`: stops, `sc delete`, reverses exact firewall rules added, cleans payload if owned.
- `mkp service status`, start, stop, etc.
- Rollback on partial failure. Binaries live under ProgramData or user-chosen path (not inside global tools store). REPL records installed version for updates.
- All ACs now reference these explicit commands (updated FR-MKP-006).

Tray actions use the **same command implementation library** as the REPL (shared assembly or source), invoked in-process or via lightweight local call, not `Start-Process` per click. Timeouts + error UI defined in tray.

## Pairing, TLS, Discovery, and Authorization (addresses B5, H1, M2)
Discovery (v1): UDP broadcast + mDNS (SSDP-style LAN only). UPnP IGD port mapping explicitly excluded (no WAN exposure of input-injection service). Manual IP entry fallback.
Pairing flow: discover, exchange public info, display short pairing code or QR on both, user confirms on both (or one if host-initiated). Generate per-pair long-lived secret (or mTLS cert pair). Persist under DPAPI per user.
Transport: gRPC over TLS 1.3 with **mTLS using per-machine self-signed certs generated at first run/pairing**. Private keys in appropriate store per identity table. Thumbprint pinning during pairing-code confirmation. Revocation = forget thumbprint. No bearer/HMAC path.
Replay/ordering: monotonic sequence numbers + sliding ack window per direction. Heartbeats.
Bind policy: host listens on configured LAN interface(s) only (default not 0.0.0.0); IPC loopback is localhost. Firewall rules scoped to peer address where possible.
Authorization matrix (enforced at service + tray/agent):
- SendInput / InjectInput / SetMousePosition / SetFocusByHwnd / clipboard push: only from currently paired/authenticated peer (and only when in "active proxy" or explicit Mirror/Inject mode).
- LocateProcess: restricted (only processes owned by interactive user, current virtual desktop, depth cap 3, title only, no other text).
UPnP port mapping explicitly out of scope (SSDP discovery only for LAN).
Negative tests required: unpaired, bad/revoked secret, replayed messages, unauthorized RPC all rejected with clear error before any effect.

ProgramData payload dir created by `mkp service install` with Administrators full control, Users read+execute only; uninstall verifies no world-writable remnant.

Revocation: REPL command to forget peer; next connect fails.

## Clipboard Data Model (addresses H2, R2-MAJ-4)
Entry: { id, timestamp, formats: [{formatName, data: byte[]}], sourcePeer, sequence }
Supported formats (v1 decision): CF_UNICODETEXT, HTML (CF_HTML), DIB/PNG images only. File drop (CF_HDROP paths or content) **deferred** to later FR (would require file-transfer). 
Privacy formats: clips with ExcludeClipboardContentFromMonitorProcessing or CF_CLIPBOARD_VIEWER_IGNORE are skipped for sync and history (test asserts no leak).
Deduplication: hash + size.
Merge (LIFO): push or move-to-top on dup. Max 50 or size cap.
Loop prevention: private stamp + suppression window.
Persistence: json under %LOCALAPPDATA% (CurrentUser DPAPI). "db" deferred.
Privacy opt-out: setting + clear-history REPL command.
Concurrency & ordering: receive-time + seq tiebreak (no wall-clock trust); tests for simultaneous copies.
Last-writer or timestamp merge: receive-time + seq chosen.

## Failsafes and Reliability Policy (addresses M3, M4)

## Failsafes and Reliability Policy (addresses M3, M4)
- Emergency release hotkey (hardcoded fallback + configurable) always acts locally: release ClipCursor, unhook, stop proxying, notify.
- On process exit/crash: guaranteed ClipCursor release via dedicated watcher process + OS reset on desktop switch/logon (watcher re-asserts release; test asserts watcher behavior).
- Remote loss / disconnect: within N seconds (configurable, default 5) release clip, clear state, enter local-only, attempt reconnect with backoff.
- Stuck modifier recovery: **on every toggle transition** (both directions) the losing side synthesizes key-ups for currently-down modifiers; resync on gaining side. Explicit in control messages. Lost key-up simulation in tests covers toggle case + disconnect.
- Lost key-up simulation in tests: drop some key-up events; assert cleanup within timeout.
- Buffering/backpressure: bounded queue per peer; on full, drop oldest non-critical or signal. Mouse moves coalesce; keys/buttons never coalesce.
- Rate limiting and ordering: sequence numbers required in every event message. Ack windows. No reliance on TCP alone for input ordering.
- Local-only mode: config or hotkey forces ignore of remote until re-enabled.

## Latency, Hot Path, and Soak (addresses R2-MAJ-6)
**Latency TR (new)**: p95 end-to-end (host keydown to remote SendInput) < 25 ms on LAN under load. 4-hop path (agent-IPC-service-gRPC-service-IPC-agent) measured in Elaboration with mock endpoints before full impl.
Coalescing: mouse moves only.
Tray action feedback: <300 ms or async progress UI.
Soak gate (Transition): 24 h continuous paired session with zero stuck-input incidents and zero unreleased ClipCursor events. Evidence: logs + video + service status.

Elaboration prototype: round-trip measurement harness (IPC + gRPC + IPC) with timing; if budget missed, pivot to agent-hosted data plane (service control plane only) recorded as contingency.

## Revised Protobuf Definitions (addresses H1, R2-MAJ-1)
**Connection topology decision (R2-MAJ-1)**: Remotes (including the single v1 remote) always dial the host with a persistent bidirectional stream. All high-rate events (input batches, clipboard pushes, heartbeats) multiplex over the bidi stream. This gives ordering, liveness, and reduces firewall rules to **one inbound** on the host only. Management RPCs (Inject, SetMousePos, Locate, SetFocus) are routed over the same bidi stream as typed Control messages or use a separate authenticated unary channel that the host can reach because the stream is already open. Unary is acceptable for management because they are infrequent.

```proto
syntax = "proto3";
package mousekeyproxy.v1;

option csharp_namespace = "MouseKeyProxy.Network.V1";

service MouseKeyProxy {
  rpc SendInput (SendInputRequest) returns (CommandResult);
  rpc InjectInput (InjectInputRequest) returns (CommandResult);
  rpc SetMousePosition (SetMousePositionRequest) returns (CommandResult);
  rpc LocateProcess (LocateProcessRequest) returns (LocateProcessResponse);
  rpc SetFocusByHwnd (SetFocusByHwndRequest) returns (CommandResult);
  rpc ClipboardSync (ClipboardSyncRequest) returns (CommandResult);  // real-time
}

message SendInputRequest {
  string protocolVersion = 1;   // "1.0"
  string peerId = 2;
  uint64 seq = 3;
  repeated InputEvent events = 4;
  string correlationId = 5;
}

message InjectInputRequest {
  string protocolVersion = 1;
  string peerId = 2;
  repeated string targetRemotes = 3;
  repeated InputEvent events = 4;
  string correlationId = 5;
}

message SetMousePositionRequest {
  string protocolVersion = 1;
  string peerId = 2;
  string displayId = 3;   // stable device name or GUID, not index
  int32 x = 4;            // per-monitor logical or physical (documented)
  int32 y = 5;
  string correlationId = 6;
}

message LocateProcessRequest {
  string protocolVersion = 1;
  string peerId = 2;
  string processName = 3;
  uint32 pid = 4;
}

message LocateProcessResponse {
  repeated HwndNode nodes = 1;
  string errorCode = 2;
}

message HwndNode {
  uint64 hwnd = 1;
  string title = 2;
  string className = 3;
  uint32 processId = 4;
  repeated HwndNode children = 5;
  // depth-capped; only interactive-user windows on current desktop
}

message SetFocusByHwndRequest {
  string protocolVersion = 1;
  string peerId = 2;
  uint64 hwnd = 3;
  bool bringToFront = 4;
  string correlationId = 5;
}

message InputEvent {
  InputKind kind = 1;
  uint32 vk = 2;
  uint32 scan = 3;
  uint32 flags = 4;
  int32 dx = 5;
  int32 dy = 6;
  sint32 wheelDelta = 7;       // typed
  uint32 xbutton = 8;          // typed
  string text = 9;             // for TEXT_INPUT / inject text (unicode)
  uint64 timestampMs = 10;     // monotonic from source clock
}

enum InputKind {
  INPUT_KIND_UNSPECIFIED = 0;
  KEY_DOWN = 1;
  KEY_UP = 2;
  MOUSE_MOVE = 3;
  MOUSE_DOWN = 4;
  MOUSE_UP = 5;
  MOUSE_WHEEL = 6;
  MOUSE_XBUTTON = 7;
  TEXT_INPUT = 8;
}

message CommandResult {
  bool success = 1;
  string errorCode = 2;  // VERSION_MISMATCH, AUTH_FAILED, etc.
  string message = 3;
  uint64 ackSeq = 4;
}

message ClipboardSyncRequest {
  string protocolVersion = 1;
  string peerId = 2;
  ClipboardEntry entry = 3;
}

message ClipboardEntry {
  string entryId = 1;
  uint64 timestampMs = 2;
  string sourcePeer = 3;
  repeated ClipboardFormat formats = 4;
}

message ClipboardFormat {
  string formatName = 1;
  bytes data = 2;
}

message ControlMessage {
  uint64 seq = 1;
  oneof cmd {
    ToggleActive toggle = 2;
    ModifierResync resync = 3;
    VersionHello hello = 4;
  }
}

message ToggleActive { bool active = 1; }
message ModifierResync { repeated uint32 upVks = 1; }
message VersionHello {
  string myVersion = 1;
  string peerVersion = 2;
}

message Heartbeat { uint64 seq = 1; uint64 monotonicMs = 2; }
message Ack { uint64 lastSeq = 1; }

message ClipboardFormat {
  string formatName = 1;  // "CF_TEXT", "PNG", "FileDrop", "HTML Format"
  bytes data = 2;
}
```

Full contract includes error codes, ack/retry, compatibility rules. Version negotiation on connect.

## Requirements + Acceptance Criteria (FRs + tests) + Traceability
(These were surfaced and mapped via MCP `workflow.requirements.*` immediately after workspace registration + Codex review processing. See current state via `director todo` / requirements queries.)

**FR-MKP-001 (Hotkey toggle only)**: Support configurable hotkey (default local Ctrl-Alt-F1, remote F2) to switch active without edge mouse move.  
**AC**:
- Hotkey switches focus + proxy direction on both.
- No auto edge crossing (ClipCursor + ownership rules).
- Assert no pointer transition occurs at screen edges while inactive/active (no edge hooks in this product); configurable + persisted.
- Mapped to TR-MKP-ARCH-001, TR-MKP-INPUT-001, TR-MKP-RELI-001; TEST-MKP-001,002,003,008.

**FR-MKP-002 (Keyboard follows)**: Keyboard input proxies to current active machine.  
**AC**: All supported keys injected only to active; verified.

**FR-MKP-003 (Full proxy)**: Proxy input per support matrix (see matrix above).  
**AC**: Supported events work; unsupported fail observably, no hang or false success. No Ctrl+Alt+Del etc.

**FR-MKP-004 (Real-time clipboard LIFO)**: Real-time sync + LIFO merge, persist encrypted.  
**AC**:
- Copy on A visible on B as top immediately.
- LIFO, deduped, max 50, no loops.
- Encrypted (DPAPI CurrentUser primary), survives restart.
- Concurrent + multi-format tested.

**FR-MKP-005 (gRPC advanced controls)**: Host can call Inject/SetMousePos/Locate/SetFocusByHwnd.  
**AC**: Succeed only for authenticated paired peers; respect auth matrix and safety constraints; no full toggle side effect.

**FR-MKP-006 (Setup/REPL/service)**: REPL manages pairing, settings, explicit service lifecycle (install/uninstall reverse fw), LocalAppData. .NET 10 + director workspace.  
**AC**:
- `dotnet tool install` does NOT register service.
- `mkp service install` registers + fw; uninstall reverses cleanly.
- REPL pair/discover/neg keys, service control, clipboard.
- Tray uses shared impl, not per-action spawn.
- Settings encrypted persist.

**Current MCP Traceability (as of review processing)**:
- 6 FR-MKP (updated)
- 6 TR-MKP (ARCH-001, INPUT-001, SEC-001, CLIP-001, REPL-001, RELI-001)
- 8 TEST-MKP (001-008 covering ownership, matrix, security negatives, LIFO+merge, contract, failsafes)
- Mappings created for all FRs linking TRs + TESTs (some legacy single-id fields used for compatibility; full arrays preferred).

**First-Test Table (Byrd gates - Inception/Elaboration slices)**:
- TEST-MKP-001 (Hotkey unit): owner tray/agent project; red = no toggle effect; green = hotkey flips state + proxy dir, ClipCursor called only on active; cmd: `dotnet test ... --filter "FullyQualifiedName~HotkeyToggle"`
- TEST-MKP-002 (ownership): mocks prove input only in user session component.
- TEST-MKP-003,005,006,007,008: per TR above. Red states documented in test code.
- Gate commands: specific `dotnet test <TestProject> --filter "Category=MKP-IPC|InputMatrix|Security|Clipboard|ServiceContract|Failsafe"` ; broader `dotnet test` ; manual two-machine smoke (see Verification).
- TODO IDs (canonical): PLAN-CODEX-001 (plan revision), plus future PLAN-ARCH-001 etc created via MCP before each slice.

## Byrd / RUP Iteration Breakdown
Follow Byrd V4 strictly (small gated slices, 100% pass (zero fail, zero skip) in validation scope before next, tests as ledger). Track deferred in MCP TODO/reqs.

- **Inception** (current): plan decision-complete (this revision), FR/TR/TEST + mappings done, first tests named + red/green, wireframes, concrete commands.
- **Elaboration**: Mocks/stubs for ownership boundary, input matrix (fake hooks/Send), pairing/auth rejection, clipboard model (fake LIFO+DPAPI), REPL contract stubs. All mocks validate green. Risk prototypes (IPC, session limits, SAS rejection).
- **Construction**: Failing tests first (real interop where possible), impl, make green per slice. Slice 1: two-node hotkey toggle + supported input only (single remote). Slice 2: IPC + service/tray split + basic gRPC. Slice 3: LIFO clipboard + persist + failsafes. Slice 4: pairing/REPL full contract + Mirror + advanced gRPC. Slice 5: Nuke + full verification green.
- **Transition**: 2x Win11 E2E green, docs, beta.

## Concrete Verification Gates (addresses H5)
- Build/test: `dotnet restore && dotnet build && dotnet test --no-build -c Release --filter "FullyQualifiedName~MKP"`
- Service contract (elevated): `mkp service install`; `sc query MouseKeyProxy`; `mkp service uninstall` (verify fw rules gone).
- REPL pairing: from REPL on both machines: discover/pair with code, verify secret persisted, negative peer tests.
- Two-machine smoke (exactly two Win11):
  1. Hotkey (F1/F2) toggles active; mouse confined (visual + GetClipCursor); supported KB (letters, media, Win+ R etc) works on remote; unsupported (SAS) fails observably.
  2. Copy text/image/file on A; appears as top on B LIFO history; encrypted file check.
  3. Use Inject/SetMouse/SetFocus via REPL; verify no full toggle.
  4. Disconnect one; ClipCursor released locally within timeout; reconnect restores.
  5. Emergency hotkey releases everything locally.
  6. Service survives restart; tray reconnects.
- Evidence: screenshots of clip rects + input, service status, %LOCALAPPDATA%\MouseKeyProxy\*.json (encrypted), logs, REPL output, two-machine video for post if needed.
- Full suite must be 100% green.

## SVG Wireframes, Critical Files, Implementation Phases, Risks
(Wireframes and risks largely unchanged but now scoped to two-node v1 + new ownership/IPC sections above. Post-completion tasks moved to separate Transition/Release plan.)

Critical files updated in ownership:
- Tray/agent project owns Interop + hooks + clipboard listener + Clip + SendInput + tray.
- Service project owns GrpcProxy, pairing, persistence, watchdog.
- Shared contracts + IPC layer.
- REPL project: explicit service install verbs + command lib used by tray.

Phases updated to v1 two-node first, explicit IPC in elaboration, REPL contract commands, full gates with commands before each construction slice.

**Path of this plan**: `f:\github\MouseKeyProxy\docs\PLAN-MKP-001.md`

**MCP TODO tracking**: PLAN-CODEX-001 (plan revision per Codex) + future gated TODOs (e.g. PLAN-ARCH-*, PLAN-INPUT-*) to be created via client.Todo.* once payload shape confirmed. Traceability lives in workflow.requirements (FR/TR/TEST/mappings).

Setup + Codex processing complete. All requested init + review processing done. Stop pending final peer reviews / Byrd gate approval. No construction code yet. (100% per Byrd: requirements + first tests named + red/green before any impl slices.)
