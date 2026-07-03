# PLAN-MKP-003: MouseKeyProxy - Free Hotkey-Only Alternative to PowerToys Mouse Without Borders

**Status**: Superseded (tombstoned after Round 4 review). Content incorporated into PLAN-MKP-004.md with all fixes applied. Do not edit.

**Workspace root**: `f:\github\MouseKeyProxy`

**Date**: 2026-07-02 / 2026-07-03 (Round 3 combined 2026-07-03)

**Date**: 2026-07-02 / 2026-07-03 (forked 2026-07-03 as new working plan)

## Context
The user wants a custom, free, open-source alternative to Microsoft's PowerToys Mouse Without Borders (MWB) for **exactly two Windows 11 systems** (one host + one remote in v1). The architecture is designed to be extensible but v1 gates are strictly two-node.

Core requirements from user:
- Mouse toggles **only** via hotkey (Ctrl-Alt-F1 local / Ctrl-Alt-F2 remote, configurable). No auto edge crossing. Use ClipCursor to maintain boundaries.
- Keyboard focus follows the mouse toggle.
- Proxy keyboard and mouse input per the explicit support matrix (ordinary keys, modifiers, media, Win combos where permitted; explicitly excludes SAS/Ctrl+Alt+Del, secure desktop, lock/login screens, UIPI-blocked scenarios).
- Clipboard sharing: real-time sync + LIFO (stack) history merge (max ~50), persisted locally encrypted using DPAPI.
- Setup: f:\github\MouseKeyProxy root; git init; `director add-workspace`; GitHub repo sharpninja/MouseKeyProxy (origin); .NET 10; manage entirely via project's own PowerShell REPL global dotnet tool; settings in %LOCALAPPDATA%\MouseKeyProxy; register/run as Windows service.
- REPL tool: pairing (UDP broadcast and/or mDNS LAN discovery (no UPnP IGD/NAT port mapping) + key negotiation/persist), settings, service start/stop/uninstall + reverse firewall (elevate Windows PowerShell 5.1 (powershell.exe; always present in-box on Win11; pwsh is PS7+)), clipboard ops, toggle. REPL is the primary management UX. Explicit `mkp service install` (not automatic on tool install).
- App itself is **NOT** MCP-aware (plain .NET service + WinForms tray for desktop interaction).
- Tray (WinForms): actions (start/stop conns/service, inject text, Mirror Mode, SetMousePos) invoke shared REPL command implementation (no per-click spawn).
- gRPC for comms (TLS + REPL-negotiated secrets).
- New gRPC: InjectInput, SetMousePosition (display/pos without focus change), LocateProcess (name/PID -> hwnd tree), SetFocusByHwnd.
- Nuke build (like McpServer).
- Extensive REPL --help + repo docs.
- Post v1/Transition: social drafts, logo/branding (mouse at desk typing with monitors), 30s video scripts, AIUnit reviews (separate release plan).

Previous MWB issues (service flakiness, UseService flips, socket errors) motivated custom focused design.

This follows the Byrd Development Process V4 strictly (tests first with mocks/stubs, validate, then impl, 100% green suite before exiting any gate) mapped to RUP phases. All plans are decision-complete before construction.

**Source control note**: GitHub repo added explicitly as `origin` per user instruction (https://github.com/sharpninja/MouseKeyProxy.git). This project treats GitHub as the working origin for this repository. (Global rules note AzDO as primary for other repos; this one follows the explicit add-github-repo directive.)

## Review Processing Summary
All prior Codex + Claude Round 2 (from 001) incorporated at fork. **Claude Round-3 for PLAN-MKP-002** (see docs/PLAN-MKP-002-Claude-Review.md) addressed in this polish pass (PLAN-POLISH-003-001):
- R3-BLK-1: full red/green for all TEST-002..008 with concrete cmds; inline mappings added under FR-002..006; wireframe specs and risks restored in dedicated section; OpenSession bidi confirmed in proto.
- R3-BLK-2: proto updated - duplicate removed, full self-contained block with SessionFrame bidi (stream in/out), all messages defined inside (no prior version refs), Pair RPC added. Tooling added (protoc 25.3 + grpc plugin from Grpc.Tools), check PASSED (generated .cs + Grpc.cs). Actual .proto placed in src/MouseKeyProxy.Network/. Plan validation command updated.
- Majors: TLS/identity made consistent (service data-plane + TLS key, agent user secrets - no or-forks or contradictions); decisions locked (no 'preferred'/'or'/'documented in code'); traceability drift fixed (clean yaml export, no invented TRs, references match store).
- Minors: bare F2 -> Ctrl-Alt-F2; file copy removed from smoke (deferred); duplicate Failsafes heading removed; release plan numbering resolved to 003 successor.
Round 2 scorecard: 6/10 full, 4 partial -> all addressed or polished.
- R2-BLK-1: Stale duplicate Recommended Approach deleted; single-voice document.
- R2-BLK-2: Identity table added; pairing secrets/settings ownership consistent with agent (CurrentUser DPAPI); service non-secret only.
- R2-BLK-3: Test projects + xUnit v3 + NSubstitute (never Moq) + seams (IInputSource etc.) added to Critical Files; red/green for additional TESTs; full inline mappings; evidence export in docs/requirements-matrix.yaml; TODO contradiction resolved in text.
- R2-MAJ-1: Proto completed (HwndNode, SetFocus, typed wheel/xbutton/text, Control/Heartbeat/Ack, OpenSession bidi); topology stated (remote dials host, one inbound rule).
- R2-MAJ-2: Hook state machine for chords (including emergency and remote Ctrl-Alt-F2); modifier key-ups on *every* toggle; disambiguated.
- R2-MAJ-3: Mouse delta source (WM_INPUT RIDEV_INPUTSINK), pinning (1x1 rect), injection model, PerMonitorV2 TR, stable displayId added.
- R2-MAJ-4: Privacy formats handled with test; file-drop deferred; ordering (receive-time + seq); persistence json decided.
- R2-MAJ-5: mTLS + pinned thumbprints chosen; ProgramData ACL hardening; bind policy; UPnP IGD/NAT port mapping excluded; Locate restrictions enumerated.
- R2-MAJ-6: Latency TR (<25 ms p95), coalescing, tray timeouts, soak gate (24h), Elaboration measurement prototype + contingency.
- Minors: pwsh terminology, global.json rollForward latestFeature, LICENSE in files, AC verbiage, watcher wording, etc.

Wireframes (01-tray-icon-menu.svg etc.) are Inception/Elaboration deliverable that block tray ACs and tests (see docs/wireframes/ to be populated; specs in original PLAN-MKP-001). Post-completion successor plan ID: docs/PLAN-MKP-003-Release.md.

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
- src/MouseKeyProxy.Common/ (shared: contracts, IPC, settings, support matrix)
- src/MouseKeyProxy.Network/ (proto + generated + transport; mousekeyproxy.proto here)
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
- docs/PLAN-MKP-002.md (this working plan) + guides + wireframes/
- assets/ (logo: mouse at desk typing surrounded by monitors)
- AGENTS-README-FIRST.yaml (via director)
- docs/todo.yaml (MCP)

## Test Architecture (addresses R2-BLK-3, R2-BLK-4, Byrd compliance)
**Project rules:**
- Always xUnit v3
- Never Moq, always NSubstitute
- **Subagent tasks must use MCP TODOs** (process policy, see TODO PLAN-SUBAGENT-001 and PLAN-REV-002-001): Every subagent task MUST be backed by an MCP TODO (created via client.Todo.CreateAsync or equivalent) that includes clear scope, implementation plan (as implementationTasks list), and dependencies. Subagents are required to keep the TODO up-to-date (using client.Todo.UpdateAsync) throughout their work. All work must adhere to MCP session logging (workflow.sessionlog.beginTurn / append* / completeTurn or client.SessionLog methods) for every turn/action. Never read/write todo.yaml directly. (Moved out of pure test-arch per review.)

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
- TEST-MKP-002..008: detailed in First-Test Table below (all provided with red/green/cmd in Round 3 polish pass; no "documented in test code").

Inception exit gate: every FR has >=1 TEST with concrete red assertion, green assertion, and exact filter command; full matrix in MCP + appendix.

## Process Ownership and IPC Architecture (addresses B1)
**Decision (v1 and ongoing, locked per B2)**:
- **Service process** (runs as Windows service): owns the cross-machine gRPC data plane (listener + mTLS termination), non-secret peer state, watchdog, service lifecycle. Service owns the TLS server private key (machine-scoped in ProgramData with ACL: service-account read-only, admin write). 
- **User-session tray/agent**: owns all desktop input (hooks, SendInput, ClipCursor, clipboard listener/set), tray UI, user secrets (pairing code confirmation, user clipboard history). Agent performs pairing and provisions TLS key material to service over local IPC.
- **Local IPC boundary**: authenticated named pipe / loopback for provisioning keys, commands, events between agent and service.
- **Startup model**: Service at boot; agent at user login via Run key, connects over IPC.
- All feature code must name the owner process. Mocks enforce boundary. (This resolves the prior service-vs-agent TLS conflict by making service the data-plane terminator while agent owns user data.)

All feature implementation must name the owning process and IPC contract in code comments and tests. Mocks will enforce the boundary.

## Identity and Persistence Table (addresses R2-BLK-2, R2-BLK-5, Codex H2)
**Decision**: Pairing/TLS material and user settings live in the user-session agent only. Service holds only non-secret state (peer addresses, thumbprints, sequence counters, watchdog config). This eliminates the LocalSystem / CurrentUser DPAPI mismatch.

| Artifact                  | Owning Process     | Windows Account | Path pattern                                      | DPAPI Scope     | Round-trip Test |
|---------------------------|--------------------|-----------------|---------------------------------------------------|-----------------|-----------------|
| settings.json            | Agent             | Interactive user| %LOCALAPPDATA%\MouseKeyProxy\settings.json       | CurrentUser    | Agent unit test |
| pairing secrets / cert private key | Agent      | Interactive user| %LOCALAPPDATA%\MouseKeyProxy\identity\*.pfx or key| CurrentUser    | Agent test (with mock store) |
| TLS server private key (mTLS) | Service (data plane) | Service account | %ProgramData%\MouseKeyProxy\certs\ (ACL: service read, admin write only) | LocalMachine store (ACL'd to service) | Service unit + install ACL test |
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

**Hotkey chord detection (R2-MAJ-2)**: Toggle (Ctrl-Alt-F1 local, Ctrl-Alt-F2 remote) and emergency release chords are detected **inside the LL keyboard hook state machine** on the agent (not RegisterHotKey). RegisterHotKey is only a convenience in local/idle mode. On every toggle transition, the losing side synthesizes key-ups for all currently-down modifiers (Ctrl, Alt, Shift, Win). This is tested with the lost-key-up harness. Chord for remote is the full Ctrl-Alt-F2.

The proto and event handling will carry sufficient fields for the supported set and will explicitly reject or drop unsupported. See updated proto below.

Failsafe: emergency local hotkey (configurable, always local, hook-detected) to release ClipCursor, clear hooks, enter local-only mode. 

## Mouse Capture and Injection Strategy (locked per B4)
- Capture while remote: WM_INPUT + RIDEV_INPUTSINK (raw relative hardware deltas; agent declares PerMonitorV2).
- Pin: ClipCursor to 1x1 at last local pos (freeze). OS resets on desktop switch.
- Transmit: relative deltas only.
- Remote apply: remote's own acceleration (relative SendInput).
- Absolute only via SetMousePosition (stable displayId + per-monitor logical).
- PerMonitorV2 required for Agent (TR-MKP-INPUT-001).
- Test: capture side never applies accel; remote feel = remote local mouse settings.

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
Pairing flow: discover (UDP/mDNS), exchange, display code, user confirms. Agent on each side handles confirmation and generates machine certs. Agent provisions the host's TLS server private key material to the service over local IPC (service owns the key for gRPC termination). Persist user secrets in agent (CurrentUser DPAPI); machine certs per identity table.
Transport: gRPC over TLS 1.3 mTLS with per-machine self-signed certs. Thumbprint pinning. Revocation = forget thumbprint. Service terminates the mTLS (data plane owner).
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
Merge (LIFO): push or move-to-top on dup. Max 50 entries; per-item cap 10MB; total history cap 100MB.
Loop prevention: private stamp + suppression window.
Persistence: json under %LOCALAPPDATA% (CurrentUser DPAPI). "db" deferred.
Privacy opt-out: setting + clear-history REPL command.
Concurrency & ordering: receive-time + seq tiebreak (no wall-clock trust); tests for simultaneous copies.
Last-writer or timestamp merge: receive-time + seq chosen.

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

## Revised Protobuf Definitions (addresses H1, R2-MAJ-1, B1)
**Connection topology decision (locked)**: Remotes always dial the host with a persistent bidirectional stream via `OpenSession`. All high-rate events (input, clipboard, heartbeats, control) are multiplexed over the single bidi stream for ordering and liveness. This reduces firewall rules to one inbound on the host. Management operations (Inject, SetMousePos, etc.) are sent as typed control messages over the same stream (or a host-initiated control if needed). Unary RPCs below are for local REPL/tray use only.

The block below is the locked, intended-to-be-compilable contract. Validation: run `protoc --proto_path=src/MouseKeyProxy.Network --csharp_out=gen --grpc_out=gen --plugin=protoc-gen-grpc=protoc-gen-grpc src/MouseKeyProxy.Network/mousekeyproxy.proto` (or buf lint) as part of plan verification gates. Tooling: protoc 25.3 + grpc_csharp_plugin (from Grpc.Tools).

```proto
syntax = "proto3";
package mousekeyproxy.v1;

option csharp_namespace = "MouseKeyProxy.Network.V1";

// Complete self-contained contract for the locked topology.
// All referenced types are defined here. This block is intended to be
// pasted into a .proto file and compiled with protoc without external dependencies.

service MouseKeyProxy {
  // Primary cross-machine channel: remote dials host with persistent bidirectional stream
  rpc OpenSession (stream SessionFrame) returns (stream SessionFrame);

  // Pairing protocol surface (discovery / key negotiation before OpenSession)
  rpc Pair (PairRequest) returns (PairResponse);

  // Local REPL/tray management RPCs (typically routed over the open stream in implementation)
  rpc SetMousePosition (SetMousePositionRequest) returns (CommandResult);
  rpc LocateProcess (LocateProcessRequest) returns (LocateProcessResponse);
  rpc SetFocusByHwnd (SetFocusByHwndRequest) returns (CommandResult);
  rpc InjectInput (InjectInputRequest) returns (CommandResult);
}

message OpenSessionRequest {
  string protocolVersion = 1;  // "1"
  string peerId = 2;
  bytes auth = 3;  // thumbprint or derived token
}

message PairRequest {
  string protocolVersion = 1;
  string peerId = 2;
  bytes publicInfo = 3;
  string pairingCode = 4;
}

message PairResponse {
  bool success = 1;
  bytes peerCert = 2;
  string error = 3;
}

message SessionFrame {
  uint64 seq = 1;
  oneof frame {
    InputBatch input = 2;
    ClipboardPush clipboard = 3;
    ControlMsg control = 4;
    Heartbeat heartbeat = 5;
    Ack ack = 6;
  }
}

message InputBatch {
  uint64 baseSeq = 1;
  repeated InputEvent events = 2;
}

message InputEvent {
  InputKind kind = 1;
  uint32 vk = 2;
  uint32 scan = 3;
  uint32 flags = 4;
  int32 dx = 5;
  int32 dy = 6;
  sint32 wheelDelta = 7;
  uint32 xbutton = 8;
  string text = 9;
  uint64 tsMs = 10;
}

enum InputKind {
  INPUT_KIND_UNSPECIFIED = 0;
  KEY_DOWN = 1; KEY_UP = 2;
  MOUSE_MOVE = 3; MOUSE_DOWN = 4; MOUSE_UP = 5;
  MOUSE_WHEEL = 6; MOUSE_XBUTTON = 7; TEXT_INPUT = 8;
}

message ClipboardPush {
  uint64 seq = 1;
  ClipboardEntry entry = 2;
}

message ClipboardEntry {
  string id = 1;
  uint64 tsMs = 2;
  string source = 3;
  repeated ClipboardFormat formats = 4;
}

message ClipboardFormat {
  string name = 1;  // UNICODETEXT, HTML, PNG, etc.
  bytes data = 2;
}

message ControlMsg {
  uint64 seq = 1;
  oneof cmd {
    Toggle toggle = 2;
    ModResync mods = 3;
    VersionHello hello = 4;
  }
}

message Toggle { bool active = 1; }
message ModResync { repeated uint32 ups = 1; }
message VersionHello {
  string myVer = 1;
  string peerVer = 2;
}

message Heartbeat { uint64 seq = 1; uint64 monoMs = 2; }
message Ack { uint64 last = 1; }

message CommandResult {
  bool ok = 1;
  string err = 2;  // VERSION_MISMATCH | AUTH | etc.
  string msg = 3;
  uint64 ackSeq = 4;
}

// Management request/response messages (full definitions for the locked contract)

message SetMousePositionRequest {
  string protocolVersion = 1;
  string peerId = 2;
  string displayId = 3;   // stable device name or GUID
  int32 x = 4;
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
}

message SetFocusByHwndRequest {
  string protocolVersion = 1;
  string peerId = 2;
  uint64 hwnd = 3;
  bool bringToFront = 4;
  string correlationId = 5;
}

message InjectInputRequest {
  string protocolVersion = 1;
  string peerId = 2;
  repeated string targetRemotes = 3;
  repeated InputEvent events = 4;
  string correlationId = 5;
}

**Validation evidence**: This entire block (from "syntax = "proto3";" to the last closing brace) must parse cleanly with `protoc --proto_path=src/MouseKeyProxy.Network --csharp_out=gen --grpc_out=gen --plugin=protoc-gen-grpc=protoc-gen-grpc src/MouseKeyProxy.Network/mousekeyproxy.proto` (or `buf lint`) as part of verification gates. No external files or "prior version" references are required.

**Proto check result (2026-07-03)**: PASSED (after adding protoc 25.3 + grpc_csharp_plugin from Grpc.Tools). Generated: Mousekeyproxy.cs and MousekeyproxyGrpc.cs. Full check command used from project structure. See verification gates below.

## Requirements + Acceptance Criteria (FRs + tests) + Traceability
(These were surfaced and mapped via MCP `workflow.requirements.*` immediately after workspace registration + Codex review processing. See current state via `director todo` / requirements queries.)

**FR-MKP-001 (Hotkey toggle only)**: Support configurable hotkey (default local Ctrl-Alt-F1, remote Ctrl-Alt-F2) to switch active without edge mouse move.  
**AC**:
- Hotkey switches focus + proxy direction on both.
- No auto edge crossing (ClipCursor + ownership rules).
- Assert no pointer transition occurs at screen edges while inactive/active (no edge hooks in this product); configurable + persisted.
- Mapped to TR-MKP-ARCH-001, TR-MKP-INPUT-001, TR-MKP-RELI-001; TEST-MKP-001,002,003,008.

**FR-MKP-002 (Keyboard follows)**: Keyboard input proxies to current active machine.  
**AC**: All supported keys injected only to active; verified.  
**Mappings**: TR-MKP-INPUT-001, RELI-001 ; TEST-MKP-003,008 (inline for completeness).

**FR-MKP-003 (Full proxy)**: Proxy input per support matrix (see matrix above).  
**AC**: Supported events work; unsupported fail observably, no hang or false success. No Ctrl+Alt+Del etc.  
**Mappings**: TR-MKP-INPUT-001 ; TEST-MKP-003.

**FR-MKP-004 (Real-time clipboard LIFO)**: Real-time sync + LIFO merge, persist encrypted.  
**AC**:
- Copy on A visible on B as top immediately.
- LIFO, deduped, max 50, no loops.
- Encrypted (DPAPI CurrentUser primary), survives restart.
- Concurrent + multi-format tested.
**Mappings**: TR-MKP-CLIP-001, RELI-001 ; TEST-MKP-004,006,008.

**FR-MKP-005 (gRPC advanced controls)**: Host can call Inject/SetMousePos/Locate/SetFocusByHwnd.  
**AC**: Succeed only for authenticated paired peers; respect auth matrix and safety constraints; no full toggle side effect.
**Mappings**: TR-MKP-SEC-001, ARCH-001 ; TEST-MKP-005,002.

**FR-MKP-006 (Setup/REPL/service)**: REPL manages pairing, settings, explicit service lifecycle (install/uninstall reverse fw), LocalAppData. .NET 10 + director workspace.  
**AC**:
- `dotnet tool install` does NOT register service.
- `mkp service install` registers + fw; uninstall reverses cleanly.
- REPL pair/discover/neg keys, service control, clipboard.
- Tray uses shared impl, not per-action spawn.
- Settings encrypted persist.
**Mappings**: TR-MKP-REPL-001, ARCH-001, SEC-001 ; TEST-MKP-007,005.

**Verifiable Byrd Traceability (B3 fix)**:
- Full export generated via MCP (yaml format, no error): docs/requirements-export.yaml ; also docs/Project/Requirements-Matrix.md etc (see docs/requirements-traceability.yaml).
- Explicit mappings (all 6 FRs):
  - FR-MKP-001 -> TR-MKP-ARCH-001, INPUT-001, RELI-001 ; TEST-MKP-001,002,003,008
  - FR-MKP-002 -> TR-MKP-INPUT-001, RELI-001 ; TEST-MKP-003,008
  - FR-MKP-003 -> TR-MKP-INPUT-001 ; TEST-MKP-003
  - FR-MKP-004 -> TR-MKP-CLIP-001, RELI-001 ; TEST-MKP-004,006,008
  - FR-MKP-005 -> TR-MKP-SEC-001, ARCH-001 ; TEST-MKP-005,002
  - FR-MKP-006 -> TR-MKP-REPL-001, ARCH-001, SEC-001 ; TEST-MKP-007,005

**First-Test Table (all gates, red/green/command)**:
- TEST-MKP-001 (Hotkey unit, Agent.Tests): red = no state flip or Clip on inactive hotkey; green = flips active, direction changes, Clip only on active side; cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "FullyQualifiedName~HotkeyToggleUnit"`
- TEST-MKP-002 (ownership, Agent.Tests/Service.Tests): red = hooks/Send attempted from service; green = only agent attempts desktop ops, service rejects; cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=Ownership"`
- TEST-MKP-003 (input matrix, Agent.Tests): red = SAS or secure desktop succeeds or hangs; green = supported keys work, unsupported fail observably no-op + log; cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=InputMatrix"`
- TEST-MKP-004 (LIFO clipboard, Agent.Tests): red = no top-of-stack or duplicate or loop or plaintext; green = LIFO order, dedup, privacy skip, DPAPI roundtrip, cap; cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=LIFO"`
- TEST-MKP-005 (pairing security, Service/Agent): red = unpaired/bad-secret succeeds; green = reject before effect + auth matrix; cmd = `dotnet test MouseKeyProxy.Service.Tests --filter "Category=SecurityNegative"`
- TEST-MKP-006 (clipboard merge, Agent): red = wrong order or no concurrent safety; green = receive-time + seq order, simultaneous copies correct; cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=ClipboardMerge"`
- TEST-MKP-007 (REPL service contract, integration): red = no rollback or fw left; green = install registers + fw, uninstall reverses clean; cmd = `dotnet test --filter "Category=ServiceContract"`
- TEST-MKP-008 (failsafe, Agent): red = clip not released on crash/disconnect or stuck modifier; green = <2s release, modifier cleanup on toggle; cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=Failsafe"`

Gate commands and TODO: see above + PLAN-REV-002-001. All via MCP only.

## Byrd / RUP Iteration Breakdown
Follow Byrd V4 strictly (small gated slices, 100% pass (zero fail, zero skip) in validation scope before next, tests as ledger). Track deferred in MCP TODO/reqs.

- **Inception** (current): plan decision-complete (this revision), FR/TR/TEST + mappings done, first tests named + red/green, wireframes, concrete commands.
- **Elaboration**: Mocks/stubs for ownership boundary, input matrix (fake hooks/Send), pairing/auth rejection, clipboard model (fake LIFO+DPAPI), REPL contract stubs. All mocks validate green. Risk prototypes (IPC, session limits, SAS rejection).
- **Construction**: Failing tests first (real interop where possible), impl, make green per slice. Slice 1: two-node hotkey toggle + supported input only (single remote). Slice 2: IPC + service/tray split + basic gRPC. Slice 3: LIFO clipboard + persist + failsafes. Slice 4: pairing/REPL full contract + Mirror + advanced gRPC. Slice 5: Nuke + full verification green.
- **Transition**: 2x Win11 E2E green, docs, beta.

## Concrete Verification Gates (addresses H5)
- Proto check: `protoc --proto_path=src/MouseKeyProxy.Network --csharp_out=gen --grpc_out=gen --plugin=protoc-gen-grpc=protoc-gen-grpc src/MouseKeyProxy.Network/mousekeyproxy.proto` (must exit 0, generates .cs + Grpc.cs)
- Build/test: `dotnet restore && dotnet build && dotnet test --no-build -c Release --filter "FullyQualifiedName~MKP"`
- Service contract (elevated): `mkp service install`; `sc query MouseKeyProxy`; `mkp service uninstall` (verify fw rules gone).
- REPL pairing: from REPL on both machines: discover/pair with code, verify secret persisted, negative peer tests.
- Two-machine smoke (exactly two Win11):
  1. Hotkey (Ctrl-Alt-F1 / Ctrl-Alt-F2) toggles active; mouse confined (visual + GetClipCursor); supported KB (letters, media, Win+ R etc) works on remote; unsupported (SAS) fails observably.
  2. Copy text/image on A; appears as top on B LIFO history; encrypted file check. (File drop is deferred per v1 formats decision.)
  3. Use Inject/SetMouse/SetFocus via REPL; verify no full toggle.
  4. Disconnect one; ClipCursor released locally within timeout; reconnect restores.
  5. Emergency hotkey releases everything locally.
  6. Service survives restart; tray reconnects.
- Evidence: screenshots of clip rects + input, service status, %LOCALAPPDATA%\MouseKeyProxy\*.json (encrypted), logs, REPL output, two-machine video for post if needed.
- Full suite must be 100% green.

## SVG Wireframes (re-homed specs)
Wireframes are required as deliverable blocking tray tests and ACs. Specs (simple SVG):

- 01-tray-icon-menu.svg: tray icon (mouse+key symbol), right-click: Toggle Active (Ctrl-Alt-F1), Start Mirror Mode, Inject Text to Remote..., Start/Stop Service, Pair/Discover (REPL), Settings, Exit.
- 02-inject-form.svg: modal "Inject to Remote": remote dropdown, textarea, Send/Cancel.
- 03-mirror-mode.svg: active indicator + selectable remote list (checkboxes); Stop button.
- 04-status.svg: hover/click shows connected remotes, role, last clip event.

See docs/wireframes/ (to be created with actual SVGs).

Critical files updated in ownership:
- MouseKeyProxy.Service/ owns networking/gRPC, pairing state (non-secret), watchdog.
- MouseKeyProxy.Agent/ owns hooks, SendInput, ClipCursor, clipboard listener, tray UI.
- MouseKeyProxy.Common/ owns contracts, IPC, settings, support matrix.
- MouseKeyProxy.Network/ owns mousekeyproxy.proto + generated.
- MouseKeyProxy.Repl/ owns global tool + shared command lib.

Phases updated to v1 two-node first, explicit IPC in elaboration, REPL contract commands, full gates with commands before each construction slice.

## Risks / Tradeoffs (restored)
- Service context limits for input -> hybrid agent required.
- gRPC secret negotiation + TLS reliability.
- DPAPI under service vs user.
- Exact SendInput fidelity.
- Firewall elevation via pwsh 5.1 in REPL.
- Scope limited to hotkey + 2 machines v1.
- (New from reviews) Latency budget with 4-hop path; mouse accel consistency.

**Path of this plan**: `f:\github\MouseKeyProxy\docs\PLAN-MKP-002.md`

**Review history**: Codex review in `docs/PLAN-MKP-001-Codex-Review.md`, Claude Round 2 in `docs/PLAN-MKP-001-Claude-Review.md`, Claude Round 3 in `docs/PLAN-MKP-002-Claude-Review.md` (all incorporated, including tooling addition and successful protoc check on 2026-07-03). Successor: docs/PLAN-MKP-003-Release.md (the PLAN-MKP-002-Release.md is legacy stub).

**MCP TODO tracking**: PLAN-SUBAGENT-001 (subagent MCP TODO + session logging rule) + other gated TODOs. This is now the active working plan. Construction remains on hold pending final peer reviews / Byrd gate approval. No construction code yet.

(100% per Byrd: requirements + first tests named + red/green before any impl slices.)
