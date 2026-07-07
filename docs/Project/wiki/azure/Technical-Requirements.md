# Technical Requirements (MCP Server)

## TR-HOTKEY-CONTRACT-001

**Hotkey seam compile contracts** — The .NET 10 solution must compile with interface-only seams and behaviorless stubs for hotkey toggle state, hotkey events, cursor clipping, and input injection before behavior is implemented.
Scope: layer-1+

## TR-MKP-AGENTCMD-001

**Codex and Claude invocation wrappers** — Product scripts must expose Invoke-Codex, Invoke-Claude, and Invoke-MouseKeyProxyDelegation. Wrappers must default to `F:\GitHub\MouseKeyProxy`, print parameter summaries, echo complete executable/argument call signatures, stream stdout/stderr through Tee-Object without suppression, log output, support dry-run, and throw on nonzero exits.
Scope: layer-1+

## TR-MKP-AGENTIPC-001

**Agent command surface** — The CLI/REPL is the canonical implementation of the control surface. REPL, tray, and dashboard actions must call shared command implementations. UI actions must not shell out per click for core operations and must not expose control actions that are unavailable through the CLI. Commands must expose structured status and error results suitable for UI rendering and test assertions.
Scope: layer-1+

## TR-MKP-ARCH-001

**Process ownership and local IPC** — The Windows service owns networking, pairing, persistence, watchdogs, and service lifecycle. The user-session agent/tray owns low-level hooks, hotkeys, clipboard listener, ClipCursor, SendInput, and focus operations. Agent/service communication must use authenticated local IPC with timeouts and reconnect behavior.
Scope: layer-1+

## TR-MKP-BRAND-001

**Brand asset pipeline** — Maintain source and rendered logo/brand assets for the hacker mouse at keyboard/desk/monitor scene. Assets must be usable at tray-icon, window-header, documentation, and release/package scales. The repository must document asset provenance and generated variants.
Scope: layer-1+

## TR-MKP-CLIP-001

**Clipboard LIFO model and persistence** — Define entry schema, format support, dedupe, loop prevention, receive-time and sequence ordering, max 50 entries, per-item size limits, total storage cap, and CurrentUser DPAPI persistence under LocalAppData. Supported formats include Unicode text, CF_HTML, and image formats agreed by implementation; file drop is deferred unless explicitly added later.
Scope: layer-1+

## TR-MKP-HID-001

**Pi Zero 2 .NET USB gadget HID backend** — The hardware backend must use Raspberry Pi OS Lite plus Linux configfs/libcomposite to expose keyboard and relative mouse HID gadget functions at /dev/hidg0 and /dev/hidg1, controlled by a minimal authenticated C#/.NET 10 HTTP service published for linux-arm64.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Provisioning creates a configfs composite gadget with keyboard and relative mouse HID functions and binds it to an available UDC. (evidence: scripts\pi\setup-configfs-gadget.sh added but not executed on discoverable Pi hardware.)
- [x] The Pi service is a C#/.NET 10 HTTP service exposing GET /status and POST /keyboard/report, /mouse/report, /clear-modifiers, and /reset. (evidence: src\MouseKeyProxy.PiHid; tests\MouseKeyProxy.PiHid.Tests)
- [x] Requests missing or using the wrong bearer token receive an authorization failure and do not write HID reports. (evidence: dotnet test tests\MouseKeyProxy.PiHid.Tests -c Release --filter Category=HardwareHID)
- [x] clear-modifiers sends all-up keyboard reports and zero-button mouse reports. (evidence: dotnet test tests\MouseKeyProxy.PiHid.Tests -c Release --filter Category=HardwareHID)
- [x] HID appliance source, docs, and provisioning assets do not depend on Python. (evidence: tests\MouseKeyProxy.Compliance.Tests\HardwareHidComplianceTests.cs)

## TR-MKP-INPUT-001

**Input support matrix and limits** — Document and enforce supported keyboard, mouse, focus, and window-control inputs. Exclude SAS/Ctrl+Alt+Del, secure desktop, lock/login screens, and UIPI-blocked scenarios. Unsupported operations must return observable failures and release any captured local state.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Keyboard, modifier, pointer, wheel, media, permitted Windows chords, and explicit exclusions are documented and enforced.
- [x] Alt-Space and Win-Arrow preserve modifier ordering, key-up, scan, and extended-key semantics. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=InputRegression)
- [x] Mouse forwarding uses raw relative deltas rather than clipped cursor screen coordinates. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=RawMouseCapture)
- [ ] The optional pi-hid backend maps MouseKeyProxy input batches into fixed-size keyboard and mouse HID reports.

## TR-MKP-LOG-001

**ILogger and EventLog integration** — Service logging must use Microsoft.Extensions.Logging and EventLog provider configuration. Event source creation belongs to elevated install. Tests must validate ILogger calls through substitutes or test sinks without requiring real Event Log writes.
Scope: layer-1+

## TR-MKP-ORCH-001

**Paired lab orchestration** — Provide repeatable scripts/commands and receipts for pairing payton-legion2 with payton-desktop, starting services, verifying port reachability, running gRPC calls, and proving visible remote control from Legion2 to Desktop.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Scripts or CLI commands can pair hosts, start services, check ports, run gRPC calls, and prove visible remote control.
- [x] WindowProbe JSON and timestamped screenshot metadata are collected for key-effect proof. (evidence: dotnet test tests\MouseKeyProxy.Service.Tests -c Release --filter Category=WindowProbeE2E)
- [ ] Orchestration can check Pi network reachability, token auth, and target-host HID evidence without mutating target state.
- [ ] Transition hardware runs write receipts with source host, target host, Pi host, correlation id, and UTC timestamps.

## TR-MKP-RELI-001

**Event reliability and failsafes** — Input events require sequence/ack handling, stuck-key cleanup, disconnect cleanup, emergency unclip hotkey, auto-release on exit, and local-only fallback. Safety deadlines: ClipCursor release within 2 seconds and reconnect give-up within 5 seconds unless configuration says otherwise.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Input events use sequence or ack handling where remote delivery can fail.
- [x] Toggle, disconnect, emergency release, and local regain clear left/right Shift, Ctrl, Alt, and Win locally and remotely. (evidence: dotnet test tests\MouseKeyProxy.Common.Tests -c Release --filter Category=ModifierCleanup)
- [ ] Captured local mouse state and ClipCursor are released within the configured safety deadline.
- [ ] Pi HID reset and clear-modifiers leave keyboard and mouse reports in neutral state.

## TR-MKP-REPL-001

**REPL install and payload publish** — REPL commands must support explicit service install/uninstall/update/status, pairing, pair status, agent status, emergency release, logs, settings, clipboard, remote-control operations, and toggle operations. Install bundles self-contained service payloads, configures Kestrel HTTP/2 on the configured gRPC port, uses Windows PowerShell 5.1 where required for firewall/service operations, and separates service payload location from global tool installation.
Scope: layer-1+

## TR-MKP-SEC-001

**Pairing auth and RPC authorization** — Discovery may use UDP broadcast and/or mDNS on LAN. Pairing must use a pairing code or ToFU flow and persist secret/certificate material securely. RPC authorization must reject unpaired, revoked, or bad-secret peers before any input/clipboard/focus effect. UPnP IGD/NAT port mapping is excluded.
Scope: layer-1+

## TR-MKP-TESTDOUBLE-001

**NSubstitute-only .NET test doubles** — Test projects must not reference Moq. Mock/substitute behavior must use NSubstitute APIs such as `Substitute.For<T>()`, `Returns`, `Received`, and `DidNotReceive`, or explicit hand-written fakes where a substitute is inappropriate. Repository validation must scan package and source files for Moq before accepting implementation slices.
Scope: layer-1+

## TR-MKP-UI-001

**Tray and dashboard UX** — Implement a WinForms tray agent and compact dashboard or equivalent native Windows UI. It must show pairing state, active peer, service state, clipboard sync state, recent errors, and controls for pair/toggle/reconnect/emergency release/service status/logs. The design must be dense, legible, and visually consistent with MouseKeyProxy branding.
Scope: layer-1+

## TR-OWNERSHIP-CONTRACT-001

**Ownership seam compile contracts** — The .NET 10 solution must compile ownership and input boundary contracts without giving the service direct user-session authority.
Scope: layer-1+

