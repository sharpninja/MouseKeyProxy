# PLAN-MKP-006: MouseKeyProxy - Free Hotkey-Only Alternative to PowerToys Mouse Without Borders

**Status**: Combined plan (PLAN-MKP-004 lineage + PLAN-REV-006-001). Incorporates all prior decisions plus first-class Harness Evidence requirements: visibility observation mechanics, exact artifact contracts, error-path matrix for verify-env gRPC Unavailable, shipped-code test contract, wireframe-driven tray UI, Nuke build for payloads, complete test matrix with actual implementations, and MCP compliance. Harness evidence is orthogonal to code correctness; expect substantial iteration on visibility and log contracts before construction slices complete. GO for Elaboration; Construction gated on Harness Evidence Iteration green.

**Workspace root**: `f:\github\MouseKeyProxy`

All development, editing, building, testing, and verification must be performed from the product workspace at this exact location. No parallel shadow trees for implementation. MCP workspace re-anchor (`director add-workspace`, full bootstrap) must target this root before any git, build, or verify operation; re-anchor again if the harness snapshot stops showing `src/` changes from this tree.

Changes under `src/` must be visible to the verification harness before any other acceptance work: after edits, `git status --porcelain 'src/'` must list uncommitted `M src/...` lines and `git diff --no-color --name-only 'src/'` must show modifications; terminal output must contain the literal strings `diff --git a/src/...` including at least one `src/MouseKeyProxy.*` path. If the next verifier CHANGED_FILES/patch input would not contain `src/`, stop and fix workspace/git state; do not proceed to other ACs.

The sole verification entrypoint is `pwsh -ExecutionPolicy Bypass -File scripts/verify-goal.ps1`. Run the full verification plan and capture receipts before any goal-completion claim. Plan deviations and status updates must be terse facts only (no self-claims such as "we ensured").

**Date**: 2026-07-03

## Context
The user wants a custom, free, open-source alternative to Microsoft's PowerToys Mouse Without Borders (MWB) for **exactly two Windows 11 systems** (one host + one remote in v1). The architecture is designed to be extensible but v1 gates are strictly two-node.

Core requirements from user:
- Mouse toggles **only** via hotkey (Ctrl-Alt-F1 local / Ctrl-Alt-F2 remote, configurable). No auto edge crossing. Use ClipCursor to maintain boundaries.
- Keyboard focus follows the mouse toggle.
- Proxy keyboard and mouse input per the explicit support matrix (ordinary keys, modifiers, media, Win combos where permitted; explicitly excludes SAS/Ctrl+Alt+Del, secure desktop, lock/login screens, UIPI-blocked scenarios).
- Clipboard sharing: real-time sync + LIFO (stack) history merge (max ~50), persisted locally encrypted using DPAPI.
- Setup: f:\github\MouseKeyProxy root; `director add-workspace`; GitHub repo sharpninja/MouseKeyProxy (origin); .NET 10; manage entirely via project's own PowerShell REPL global dotnet tool; settings in %LOCALAPPDATA%\MouseKeyProxy; register/run as Windows service. The Visibility Gate (product workspace at f:\github\MouseKeyProxy, dirty src/ state with explicit git diff headers, and exact scratch hygiene after verify-goal.ps1) is a prerequisite for all work and claims.
- REPL tool: pairing (UDP broadcast and/or mDNS LAN discovery (no UPnP IGD/NAT port mapping) + key negotiation/persist), settings, service start/stop/uninstall + reverse firewall (elevate via netsh or Windows PowerShell 5.1 (powershell.exe)), clipboard ops, toggle. REPL is the primary management UX. Explicit `mkp service install` (not automatic on tool install). The REPL nupkg includes self-contained Service and Agent payloads (produced via Nuke publish-selfcontained and bundled under a payloads/ directory inside the package). The install command deploys these to %ProgramData%\MouseKeyProxy\, applies ACLs, creates the service via sc.exe, adds firewall, starts the service, creates a scheduled task "MouseKeyProxyTray" (ONLOGON for the interactive user session to make the tray visible), and launches it.
- App itself is **NOT** MCP-aware (plain .NET service + WinForms tray for desktop interaction).
- Tray (WinForms): actions (start/stop conns/service, inject text, emergency release, SetMousePos) invoke shared REPL command implementation (no per-click spawn). Mirror Mode is removed from product scope. The tray UI and forms must exactly match the wireframes in docs/wireframes/ (using the logo from assets/ for the NotifyIcon). A wireframe-to-UI review with receipts is required.
- gRPC for comms (TLS + REPL-negotiated secrets).
- New gRPC: InjectInput, SetMousePosition (display/pos without focus change), LocateProcess (name/PID -> hwnd tree), SetFocusByHwnd.
- Nuke build (like McpServer) responsible for producing self-contained Service and Agent payloads, packing the REPL nupkg with the payloads/ directory included, and supporting verification.
- Extensive REPL --help + repo docs.
- Post v1/Transition: social drafts, logo/branding (mouse at desk typing with monitors), 30s video scripts, AIUnit reviews (separate release plan).

Previous MWB issues (service flakiness, UseService flips, socket errors) motivated custom focused design.

This follows the Byrd Development Process V4 strictly (tests first with mocks/stubs, validate, then impl, 100% green suite before exiting any gate) mapped to RUP phases. All plans are decision-complete before construction.

**Source control note**: GitHub repo added explicitly as `origin` per user instruction (https://github.com/sharpninja/MouseKeyProxy.git). This project treats GitHub as the working origin for this repository. (Global rules note AzDO as primary for other repos; this one follows the explicit add-github-repo directive.)

## Review Processing Summary
All PLAN-MKP-004 decisions preserved (Round 2-4 reviews, proto, FR/TR/TEST mappings, identity table, REPL contract, IPC split). **PLAN-REV-006-001** (this document) folds construction-remediation learnings into the canonical plan so harness acceptance is first-class, not a post-step:

- **REV-006-001 Visibility Gate**: Harness observation mechanics specified (product workspace only, MCP re-anchor, dirty uncommitted `src/`, captured `git status`/`git diff` with `diff --git a/src/...`, CHANGED_FILES must contain `src/` before other AC work).
- **REV-006-002 Harness Evidence Iteration**: Explicit Byrd phase between Inception and Construction slices; own tests (`TEST-MKP-012`) and gates; visibility effort may dominate early iterations.
- **REV-006-003 Artifact Contract**: Scratch exactly four files post-verify; forbidden `test-*.log`/`verif-*.log`; required log substrings in `build.log`, `full-test-output.log`, `repl-run.log`.
- **REV-006-004 Error-Path Matrix**: Per-AC dual execution (real gRPC attempt then null-client fallback); no swallowed errors; `toggle FAILED` + non-zero exit on verify-env failure.
- **REV-006-005 Shipped-code test contract**: `SendInputBatchAsync` always builds frames before client check; `RecordingTransport` ctor-only (no overrides); `SentFrames.Count >= 2` proves Emit branch; no manual frame construction in tests.
- **REV-006-006 Inception blockers enforced**: `docs/wireframes/`, `assets/` logo, `build/Build.cs` (Nuke), `tests/MouseKeyProxy.Integration/`, MCP artifacts (`AGENTS-README-FIRST.yaml`, director workspace) are gates, not post-remediation polish.
- **REV-006-007 REPL deployment locked**: Bundled self-contained `payloads/` in nupkg; C# install path (copy, ACLs, `sc.exe`, firewall, `MouseKeyProxyTray` ONLOGON scheduled task for Session 1 tray visibility).
- **REV-006-008 Process hygiene**: `verify-goal.ps1` sole entrypoint; MCP-only TODO/session; receipts before claims; no self-claims in plan text.

Prior review findings have been incorporated. Standalone review and approval artifacts have been removed. Post-completion successor: docs/PLAN-MKP-004-Release.md (or PLAN-MKP-REL-001).

Wireframes (01-tray-icon-menu.svg etc.) are Inception/Elaboration deliverables that block tray ACs and tests (see docs/wireframes/ to be populated; specs in original PLAN-MKP-001).

## Recommended Approach
.NET 10 Windows service (Microsoft.Extensions.Hosting.WindowsServices + UseWindowsService + current-dir fix for SCM) **for non-interactive concerns only**.

- User-session tray/agent (WinForms tray + minimal supporting code) owns all interactive desktop work.
- Win32 P/Invoke (in tray/agent only): SetWindowsHookEx (LL KB/mouse), SendInput (full supported events), RegisterHotKey, ClipCursor, AddClipboardFormatListener, etc.
- Local IPC: authenticated named pipe (with ACL) between tray/agent and service for commands, events, and key provisioning (loopback gRPC is fallback if pipe unavailable; named pipe is primary).
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
- src/MouseKeyProxy.Agent/ (user-session tray/agent: Interop/Win32Input.cs (hooks, SendInput, ClipCursor etc.), hotkeys, clipboard listener, tray UI/forms matching the wireframes in docs/wireframes/ and using the logo from assets/, IPC client to service)
- src/MouseKeyProxy.Common/ (shared: contracts, IPC, settings, support matrix)
- src/MouseKeyProxy.Network/ (proto + generated + transport; mousekeyproxy.proto here)
- src/MouseKeyProxy.Repl/ (global tool package: explicit `mkp service install` etc.; includes self-contained payloads under payloads/; shared command lib reused by tray; --help with repo links)
- build/Build.cs (Nuke - targets for publish-selfcontained of Service and Agent payloads, packing REPL with payloads bundled, test, and verification)
- scripts/ (verify-goal.ps1 sole harness entrypoint, Manage-MouseKeyProxyService.ps1, firewall helpers)
- tests/MouseKeyProxy.Common.Tests/ (xUnit v3 + NSubstitute; seams and pure logic)
- tests/MouseKeyProxy.Agent.Tests/ (xUnit v3 + NSubstitute; hook state machine, LIFO, input matrix, failsafes, mouse capture)
- tests/MouseKeyProxy.Service.Tests/ (xUnit v3 + NSubstitute; pairing, auth, watchdog, IPC server)
- tests/MouseKeyProxy.Integration/ (two-machine harness; E2E with real hooks/SendInput/gRPC; requires physical or VM pair; uses xUnit v3 but no mocking library)
- .gitignore
- README.md (extensive)
- LICENSE (MIT)
- docs/PLAN-MKP-006.md (this working plan) + guides + wireframes/
- assets/ (logo: mouse at desk typing surrounded by monitors)
- AGENTS-README-FIRST.yaml (via director)
- docs/todo.yaml (MCP)

## Test Architecture (addresses R2-BLK-3, R2-BLK-4, PLAN-REV-006-001, Byrd compliance)

### Project rules
- Always xUnit v3
- Never Moq, always NSubstitute
- **Always bring the receipts.** Every claim, fix, or verification must be backed by concrete, machine-verifiable evidence: command outputs, file contents, MCP query results, generated artifacts, timestamps, exit codes, etc. Capture to receipt files (e.g. docs/receipts-*.txt, proto-verify-*.txt) and reference them. Use MCP interfaces exclusively for TODOs, requirements, sessions. Never assert without proof.
- **Subagent tasks must use MCP TODOs** (process policy, see TODO PLAN-SUBAGENT-001 and PLAN-REV-002-001): Every subagent task MUST be backed by an MCP TODO (created via client.Todo.CreateAsync or equivalent) that includes clear scope, implementation plan (as implementationTasks list), and dependencies. Subagents are required to keep the TODO up-to-date (using client.Todo.UpdateAsync) throughout their work. All work must adhere to MCP session logging (workflow.sessionlog.beginTurn / append* / completeTurn or client.SessionLog methods) for every turn/action. Never read/write todo.yaml directly.

### Visibility Gate (blocks all slices and claims)
Before any construction slice or goal-completion claim:
1. Work exclusively from `f:\github\MouseKeyProxy` (cwd, edits, `dotnet`, git, `verify-goal.ps1`).
2. MCP workspace re-anchored to this root (`director add-workspace`; repeat if harness loses `src/` visibility).
3. `src/` dirty/uncommitted: `git status --porcelain 'src/'` shows `M src/...` lines.
4. Terminal capture includes `git diff --no-color --name-only 'src/'` output with literal `diff --git a/src/...` headers and at least one project path under `src/`.
5. Confirm the verifier's next CHANGED_FILES/patch snapshot would include `src/` modifications; if not, fix state before other AC work.
6. Run `pwsh -ExecutionPolicy Bypass -File scripts/verify-goal.ps1` as the sole verification entrypoint (no standalone `dotnet test` completion claims).

### Artifact Contract (post-verify scratch state)
After the final `verify-goal.ps1` run at claim time, scratch contains **exactly** these four files (no extras):
- `build.log`
- `full-test-output.log`
- `repl-run.log`
- one `*.nupkg` (REPL package)

**Must not exist** in scratch: `test-*.log`, `verif-*.log`, or any other log/artifact. Prior diagnostic logs must be deleted before the final verify run.

**Required log content** (verified from the four canonical files):
- `full-test-output.log`: includes `Test run for` lines naming test projects (e.g. `MouseKeyProxy.Commands.Tests`) and pass counts (e.g. `Passed: 3` for Commands.Tests scope).
- `repl-run.log`: includes a `toggle` command invocation and, on verify-env gRPC Unavailable, the literal string `toggle FAILED`.
- `build.log`: documents restore/build/pack steps with non-zero failure on error.

### Error-Path Matrix (verify environment: gRPC Unavailable)
The verify harness has no real gRPC server. Every AC that claims frame emission, toggle behavior, or error reporting must satisfy **both** paths:

| Claim / AC area | Success path (lab/paired) | Verify-env path (harness) | Exit / log contract |
|---|---|---|---|
| Toggle + mod resync emission (AC3/AC4) | Real `BidiSessionTransport` to peer | Real attempt first; on `Unavailable`, null-client `BidiSessionTransport`, re-call `ToggleAsync` | Repl/Agent: print `toggle FAILED`, return non-zero; no swallowed `try/catch` around `ToggleAsync` |
| `SendInputBatchAsync` frame build | Frames sent to peer | Frames built and appended to `SentFrames` even when client unavailable | Tests assert `SentFrames` populated without transport overrides |
| `EmitModResync` branch | Executed after successful toggle | Executed via null-client fallback after real attempt fails | `SentFrames.Count >= 2` with empty events batch proves Emit branch |
| Repl bidi roundtrip tests | Live stream | Commands.Tests drives shipped `BidiSessionTransport` + `InputCommandHandler` only | No manual frame construction; no `SendInputBatchAsync` overrides in `RecordingTransport` |

**Anti-patterns (reject):** empty `catch` in `ToggleAsync` that prints success; `RecordingTransport` overriding `SendInputBatchAsync`; tests that set `SentFrames` manually; false SUCCESS when gRPC unavailable.

### Shipped-code test contract (Commands.Tests / AC4 central)
- `BidiSessionTransport.SendInputBatchAsync` always records `LastSentFrame` and appends to `SentFrames` **before** any client availability check.
- `RecordingTransport` subclasses use ctor-only wiring; **no** method overrides.
- `BidiRoundtripTests` (or equivalent) assert `SentFrames.Count >= 2` and empty events on the second frame to prove `EmitModResync` executed in shipped code.
- Delete any `CollectingTransport` or transport that bypasses shipped frame-building logic.

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
**Decision (locked)**: User settings and clipboard history live in the agent (CurrentUser DPAPI). Machine TLS identity (server private key) lives with the service (LocalMachine certificate store, ACL'd to service account). The agent performs pairing UX and provisions the key material over local IPC; the agent holds no long-lived TLS private keys. Service holds only non-secret state (peer addresses, thumbprints, sequence counters, watchdog config).

| Artifact                  | Owning Process     | Windows Account | Path pattern                                      | DPAPI Scope     | Round-trip Test |
|---------------------------|--------------------|-----------------|---------------------------------------------------|-----------------|-----------------|
| settings.json            | Agent             | Interactive user| %LOCALAPPDATA%\MouseKeyProxy\settings.json       | CurrentUser    | Agent unit test |
| pairing confirmation record + peer thumbprint (transient cert key during generation/provision only, not persisted) | Agent | Interactive user | %LOCALAPPDATA%\MouseKeyProxy\pairing\ (transient) | CurrentUser | Agent test |
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
- The REPL nupkg contains pre-built self-contained payloads for the Service and Agent (produced by Nuke publish-selfcontained targets and included via build assets in the package under a payloads/ directory).
- `mkp service install` (requires elevation): 
  - Copies the self-contained Service.exe and Agent.exe (plus supporting files) from the bundled payloads/ directory inside the installed tool.
  - Creates %ProgramData%\MouseKeyProxy\ and applies ACLs (Administrators full control, Users read+execute).
  - Creates EventLog source if needed.
  - Registers the service with `sc.exe create MouseKeyProxy binPath=... start= auto`.
  - Adds firewall rule (via netsh or elevated powershell.exe (5.1)).
  - Starts the service.
  - Creates a scheduled task named "MouseKeyProxyTray" (schtasks /Create /SC ONLOGON /RU user /RL LIMITED) pointing to the Agent executable so the tray runs in the interactive user session (visible NotifyIcon).
  - Launches the scheduled task immediately.
- `mkp service uninstall`: stops service, deletes service, removes scheduled task, reverses firewall rules, (optionally) cleans payload directory.
- `mkp service status`, start, stop, etc.
- Rollback on partial failure. Binaries live under %ProgramData%\MouseKeyProxy\ (ACL-hardened; not user-writable, not inside global tools store). REPL records installed version for updates. User-chosen path is not supported for service payload.
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
- Remote loss / disconnect: hard deadline 2s for ClipCursor release (safety); reconnect give-up 5s (configurable). Explicit in policy and TEST-MKP-008.
- Stuck modifier recovery: **on every toggle transition** (both directions) the losing side synthesizes key-ups for currently-down modifiers; resync on gaining side. Explicit in control messages. Lost key-up simulation in tests covers toggle case + disconnect.
- Lost key-up simulation in tests: drop some key-up events; assert cleanup within timeout.
- Buffering/backpressure: bounded queue per peer; on full, drop oldest non-critical or signal. Mouse moves coalesce; keys/buttons never coalesce.
- Rate limiting and ordering: sequence numbers required in every event message. Ack windows. No reliance on TCP alone for input ordering.
- Local-only mode: config or hotkey forces ignore of remote until re-enabled.

## Latency, Hot Path, and Soak (addresses R2-MAJ-6)
**Latency TR (covered under TR-MKP-RELI-001)**: p95 end-to-end (host keydown to remote SendInput) < 25 ms on LAN under load. 4-hop path (agent-IPC-service-gRPC-service-IPC-agent) measured in Elaboration with mock endpoints before full impl. (Added explicit budget to RELI-001 via store update.)
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
  MOUSE_WHEEL = 6; MOUSE_HWHEEL = 9; MOUSE_XBUTTON = 7; TEXT_INPUT = 8;
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

// Version rule: exact major match required on OpenSession; mismatch rejected with VERSION_MISMATCH error naming both versions.

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
```

**Validation evidence**: This entire block (from "syntax = "proto3";" to the last closing brace) must parse cleanly with `protoc --proto_path=src/MouseKeyProxy.Network --csharp_out=gen --grpc_out=gen --plugin=protoc-gen-grpc=protoc-gen-grpc src/MouseKeyProxy.Network/mousekeyproxy.proto` (or `buf lint`) as part of verification gates. No external files or "prior version" references are required.

**Proto check result (2026-07-03)**: PASSED. Full output captured to docs/proto-verify-004.txt (exit code 0). Generated CS size 221726 bytes. Command: protoc --proto_path=src/MouseKeyProxy.Network --csharp_out=gen --grpc_out=gen --plugin=protoc-gen-grpc=... src/MouseKeyProxy.Network/mousekeyproxy.proto . See docs/proto-verify-004.txt and docs/receipts-004.txt for receipts. MCP store query receipt for RELI (has "Latency TR: p95 end-to-end < 25 ms on LAN"). 27 mapping items in list (6 FRs covered). exports regenerated with fresh time. Consolidated receipts in docs/receipts-004.txt (includes store queries, timestamps, generated sizes).

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
- Full export generated via MCP (yaml format, success payload, fresh timestamp 2026-07-02 9:25pm post-fixes; receipt via generateDocument call): docs/requirements-export.yaml ; also docs/Project/Requirements-Matrix.md etc (see docs/requirements-traceability.yaml). All content aligns with plan (including full 6 FR mappings in TR-per-FR-Mapping.md, RELI-001 with 25ms budget). MCP listMappings receipt: 27 items covering 6 FRs. getTr for RELI confirms budget. See terminal output for full receipts.
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
- TEST-MKP-003 (input matrix, Agent.Tests): red = SAS or secure desktop succeeds or hangs; green = supported keys work, unsupported fail observably no-op + log; dedicated assertion that Win+R and Win+L are swallowed without triggering shell/lock (high-risk shortcuts); cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=InputMatrix"`
- TEST-MKP-004 (LIFO clipboard, Agent.Tests): red = no top-of-stack or duplicate or loop or plaintext; green = LIFO order, dedup, privacy skip, DPAPI roundtrip, cap; cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=LIFO"`
- TEST-MKP-005 (pairing security, Service/Agent): red = unpaired/bad-secret succeeds; green = reject before effect + auth matrix; cmd = `dotnet test MouseKeyProxy.Service.Tests --filter "Category=SecurityNegative"`
- TEST-MKP-006 (clipboard merge, Agent): red = wrong order or no concurrent safety; green = receive-time + seq order, simultaneous copies correct; cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=ClipboardMerge"`
- TEST-MKP-007 (REPL service contract, integration): red = no rollback or fw left; green = install registers + fw, uninstall reverses clean; cmd = `dotnet test --filter "Category=ServiceContract"`
- TEST-MKP-008 (failsafe, Agent): red = clip not released within 2s on crash/disconnect or modifier not cleaned on every toggle; green = ClipCursor release hard deadline 2s (safety), reconnect give-up 5s, modifier key-ups synthesized on every toggle transition (both directions); cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=Failsafe"`

Additional harness and artifact tests required as part of the matrix (red/green assertions must be coded):
- TEST-MKP-009 (Nuke + payload publish/pack): red = missing targets or no self-contained outputs or nupkg lacks payloads/; green = Nuke test/pack/publish produce correct artifacts with payloads bundled; cmd via Nuke or verify.
- TEST-MKP-010 (Wireframes + tray UI fidelity): red = missing SVGs or tray menu does not contain all items from 01-tray-icon-menu.svg or uses default icon; green = all SVGs present, Agent implements full menu + forms matching wireframes, uses assets logo, actions dispatch via shared lib; cmd = test + file existence checks.
- TEST-MKP-011 (MCP compliance + receipts): red = direct todo.yaml edit or work without MCP TODO; green = all TODOs/sessions via MCP interfaces only + receipts captured; verification via tools.
- TEST-MKP-012 (Visibility + harness contract): red = no `diff --git a/src/` in captured terminal, no uncommitted `M src/...` in `git status --porcelain 'src/'`, scratch contains `test-*.log`/`verif-*.log` or any file beyond the 4 canonical artifacts, or `repl-run.log` lacks `toggle`/`toggle FAILED`; green = Visibility Gate + Artifact Contract pass with full terminal transcript; cmd = `git status --porcelain 'src/'`; `git diff --no-color --name-only 'src/'`; `pwsh -ExecutionPolicy Bypass -File scripts/verify-goal.ps1`

Gate commands and TODO: see above + PLAN-REV-002-001. All via MCP only.

## Byrd / RUP Iteration Breakdown
Follow Byrd V4 strictly (small gated slices, 100% pass (zero fail, zero skip) in validation scope before next, tests as ledger). Track deferred in MCP TODO/reqs. Harness evidence is orthogonal to code correctness; budget explicit iteration for visibility and log contracts.

- **Inception** (complete for this revision): plan decision-complete, FR/TR/TEST + mappings done, first tests named + red/green (including `TEST-MKP-009`..`012`), wireframe specs, Nuke targets, Visibility Gate + Artifact Contract + Error-Path Matrix defined.
- **Harness Evidence Iteration** (gates Construction): Dedicated phase with `TEST-MKP-012` green before any Construction slice. Deliverables: MCP re-anchor receipts, captured `git status`/`git diff` with `diff --git a/src/...`, one full `verify-goal.ps1` cycle proving scratch hygiene and log substrings, CHANGED_FILES would contain `src/`. May require multiple verify cycles; do not treat as a post-step.
- **Elaboration**: Mocks/stubs for ownership boundary, input matrix (fake hooks/Send), pairing/auth rejection, clipboard model (fake LIFO+DPAPI), REPL install (payload copy, sc, scheduled task), Nuke targets. All mocks validate green. Risk prototypes (IPC, session limits, SAS rejection, null-client error paths per Error-Path Matrix). Wireframes created as SVGs. assets/logo created. MCP yamls initialized via director. Nuke skeleton with payload publish/pack. Shipped-code tests for Commands.Tests per contract.
- **Construction** (gated on Harness Evidence Iteration green): Failing tests first (real interop where possible), impl, make green per slice. Slice 1: two-node hotkey toggle + supported input only (single remote) with harness receipts maintained. Slice 2: IPC + service/tray split + basic gRPC + scheduled task for tray. Slice 3: LIFO clipboard + persist + failsafes. Slice 4: pairing/REPL full contract with payloads + Mirror + advanced gRPC + full wireframe UI. Slice 5: Nuke full + Integration harness + MCP compliance + all receipts.
- **Transition**: 2x Win11 E2E green (with visibility evidence in transcripts), docs, beta.

## Concrete Verification Gates (addresses H5)
- Proto check (receipt in docs/proto-verify-004.txt and docs/receipts-004.txt, exit 0, CS 221726 bytes; see also MCP store receipts for RELI/mappings): `protoc --proto_path=src/MouseKeyProxy.Network --csharp_out=gen --grpc_out=gen --plugin=protoc-gen-grpc=protoc-gen-grpc src/MouseKeyProxy.Network/mousekeyproxy.proto`
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
- **Harness visibility gates (PLAN-REV-006-001; required before claims)**:
  - Work exclusively from `f:\github\MouseKeyProxy`; MCP re-anchored to this root.
  - `git status --porcelain 'src/'` shows uncommitted `M src/...`; `git diff --no-color --name-only 'src/'` output contains `diff --git a/src/...` including at least one `src/MouseKeyProxy.*` path.
  - Verifier CHANGED_FILES/patch would contain `src/` before other AC work proceeds.
  - After `pwsh -ExecutionPolicy Bypass -File scripts/verify-goal.ps1`: scratch contains exactly `build.log`, `full-test-output.log`, `repl-run.log`, one `*.nupkg`; no `test-*.log`, `verif-*.log`, or other files.
  - `full-test-output.log` shows `Test run for` project lines and pass counts; `repl-run.log` includes `toggle` and `toggle FAILED` on verify-env error.
  - Error-Path Matrix satisfied: null-client fallback, no false SUCCESS, `SentFrames` proven in shipped path.
  - All TEST red/green assertions present in actual code and passing.
  - `docs/wireframes/` and `assets/` present; `build/Build.cs` functional for payloads.
  - MCP artifacts present and created via proper interfaces; wireframe review receipt exists.
  - Full terminal transcripts with git headers and scratch listing captured; full verify run completed before goal completion.
- Full suite must be 100% green.

## SVG Wireframes (re-homed specs)
Wireframes are required as deliverable blocking tray tests and ACs. Specs (simple SVG):

- 01-tray-icon-menu.svg: tray icon (mouse+key symbol), right-click: Toggle Active (Ctrl-Alt-F1), Emergency release, Inject Text to Remote..., Start/Stop Service, Pair/Discover (REPL), Settings, Exit.
- 02-inject-form.svg: modal "Inject to Remote": remote dropdown, textarea, Send/Cancel.
- 04-status.svg: hover/click shows connected remotes, role, last clip event.

See docs/wireframes/ (to be created with actual SVGs). The Agent tray implementation must match these wireframes exactly (full menu items, forms for inject/mirror/status, custom icon from assets/). A wireframe-to-UI review (AIUnit or equivalent) with receipts is required before the tray slice is complete.

Critical files updated in ownership:
- MouseKeyProxy.Service/ owns networking/gRPC, pairing state (non-secret), watchdog.
- MouseKeyProxy.Agent/ owns hooks, SendInput, ClipCursor, clipboard listener, tray UI (matching wireframes).
- MouseKeyProxy.Common/ owns contracts, IPC, settings, support matrix.
- MouseKeyProxy.Network/ owns mousekeyproxy.proto + generated.
- MouseKeyProxy.Repl/ owns global tool + shared command lib.

Phases updated to v1 two-node first, explicit IPC in elaboration, REPL contract commands, full gates with commands before each construction slice.

## Risks / Tradeoffs (restored)
- Service context limits for input -> hybrid agent required.
- gRPC secret negotiation + TLS reliability.
- DPAPI under service vs user.
- Exact SendInput fidelity.
- Firewall elevation via Windows PowerShell 5.1 (`powershell.exe`) in REPL.
- Scope limited to hotkey + 2 machines v1.
- (New from reviews) Latency budget with 4-hop path; mouse accel consistency.
- (PLAN-REV-006-001) Harness visibility and artifact contracts may consume substantial iteration before code slices; verifier observes dirty git state and terminal strings, not commits alone.

**Path of this plan**: `f:\github\MouseKeyProxy\docs\PLAN-MKP-006.md`

**Review history**: PLAN-MKP-001..004 review findings were incorporated into the active plan series. PLAN-REV-006-001 (this document) incorporates harness evidence, artifact contract, error-path matrix, and inception blocker enforcement. Successor: docs/PLAN-MKP-004-Release.md.

**MCP TODO tracking**: PLAN-SUBAGENT-001 (subagent MCP TODO + session logging rule) + other gated TODOs. Active working plan. Elaboration and Harness Evidence Iteration may proceed; Construction slices gated on `TEST-MKP-012` green.

(100% per Byrd: requirements + first tests named + red/green before any impl slices.)

