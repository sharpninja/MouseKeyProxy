# Testing Requirements (MCP Server)

## TEST-HOTKEY

### TEST-HOTKEY-001

xUnit v3 and NSubstitute tests must fail first for the desired hotkey toggle state machine behavior and compile against the contract seams before production behavior satisfies them.



## TEST-MKP

### TEST-MKP-001

Verify configurable hotkeys toggle proxy direction without edge detection or ClipCursor violations.


### TEST-MKP-002

Mocks confirm hooks, ClipCursor, SendInput, focus, and clipboard listener operations are only attempted by the user-session agent; service components reject direct input attempts.


### TEST-MKP-003

SendInput succeeds for supported keys and pointer actions, while SAS, secure desktop, lock/login, and UIPI-blocked scenarios fail observably without hangs.


### TEST-MKP-004

Copy on machine A appears on machine B as the top LIFO clipboard entry; encrypted persistence, max entry count, and restart reload behavior are verified.


### TEST-MKP-005

Unpaired peer, bad secret, revoked peer, or unauthorized RPC is rejected before any input, focus, or clipboard side effect.


### TEST-MKP-006

Concurrent copies produce correct LIFO order, dedupe loops, preserve supported formats, roundtrip DPAPI persistence, and enforce size/count caps.


### TEST-MKP-007

`mkp service install` registers and starts the service; uninstall stops and removes it, reverses firewall changes, and rolls back partial failure.


### TEST-MKP-008

Failsafe tests prove ClipCursor release within 2 seconds on crash/disconnect, modifier key-up synthesis on every toggle transition, and reconnect give-up behavior within the configured deadline.


### TEST-MKP-009

InjectInput gRPC contract tests verify peer targeting, authorization, sequencing, acknowledgement, and observable failure semantics.


### TEST-MKP-010

SetMousePosition gRPC tests verify display plus x/y movement without focus change unless focus is explicitly requested.


### TEST-MKP-011

LocateProcess and SetFocusByHwnd tests verify process lookup, HWND tree return shape, focus behavior, and authorization boundaries.


### TEST-MKP-012

REPL and shared command tests verify the CLI is the canonical control surface, including pairing, pair status, agent status, service status, emergency release, logs, settings persistence, clipboard commands, remote-control commands, toggle commands, and structured errors.


### TEST-MKP-013

Historical Fresh release artifacts for v0.5.0 remain verifiable, but final completion must use a new v0.5.1 release and must not move the existing v0.5.0 tag.


### TEST-MKP-014

TransitionE2E must run on payton-legion2 plus payton-desktop with zero skips and real paired-control evidence. Passing network calls alone are insufficient.


### TEST-MKP-015

Tray/dashboard UI smoke tests verify service state, pairing state, active peer, clipboard state, recent errors, and action controls render without overlap at expected Windows scaling.


### TEST-MKP-016

Branding visual receipts verify the hacker mouse typing at a keyboard/desk surrounded by monitors appears in the app and documentation at relevant sizes.


### TEST-MKP-017

Invoke-Codex and Invoke-Claude dry-run tests verify parameter summaries, complete call signature echo, argument vector echo, Product workspace default, log path handling, and no agent process launch.


### TEST-MKP-018

A live or controlled invocation test verifies agent stdout/stderr flows to the host and log file without `Out-Null` suppression in the agent output pipeline.


### TEST-MKP-019

Logging tests verify ILogger calls for startup, shutdown, pairing, gRPC sessions, input batches, failures, and failsafe triggers without writing to the real Event Log.


### TEST-MKP-020

Pairing receipt must show payton-legion2 controlling payton-desktop with cursor movement and sentinel text/input produced remotely.


### TEST-MKP-021

Package/install validation for v0.5.1 verifies clean install, service payload, REPL commands, UI launch, rollback, and release receipts.


### TEST-MKP-022

Moq ban compliance validation scans active source, project, package, and test files and fails if it finds a Moq package reference, `using Moq`, `Mock<T>`, `new Mock`, `MockBehavior`, or Moq `Times` verification usage. NSubstitute package references and usage remain allowed.


### TEST-MKP-023

Verify Alt-Space and permitted Win+Arrow chords preserve modifier ordering, key-up, scan, and extended-key semantics, and verify both peers clear left/right Shift/Ctrl/Alt/Win on toggle and release paths.

**Acceptance Criteria:**
- [x] Alt-Space emits Alt down, Space down/up, and Alt up as one ordered batch. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=InputRegression)
- [x] Win+Left/Right preserves extended arrow flags and releases Win. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=InputRegression)
- [x] Toggle/disconnect/emergency release clears left/right Shift, Ctrl, Alt, and Win on both systems. (evidence: dotnet test tests\MouseKeyProxy.Common.Tests -c Release --filter Category=ModifierCleanup)

### TEST-MKP-024

Verify mouse forwarding uses raw relative deltas while the local cursor is clipped, unregisters raw input on stop, releases ClipCursor, and local mouse control is immediately usable without Alt-Tab.

**Acceptance Criteria:**
- [x] Mouse movement forwarding uses raw relative WM_INPUT deltas while the local cursor is clipped. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=RawMouseCapture)
- [x] Stop/deactivation unregisters raw input, unhooks, and releases ClipCursor. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=InputRegression)
- [ ] Local mouse control is usable after release without Alt-Tab.

### TEST-MKP-025

Verify WindowProbe on the remote records window/system-menu/window-state effects after key injection and that CaptureScreenshot returns a PNG with capturedAtUtc, sourceHost, correlationId, target, hwnd, dimensions, and sha256 metadata.

**Acceptance Criteria:**
- [x] WindowProbe records key messages, WM_SYSCOMMAND/SC_KEYMENU, bounds, monitor, WindowState, and movement/sizing messages. (evidence: tests\MouseKeyProxy.WindowProbe)
- [x] CaptureScreenshot returns PNG data and metadata including capturedAtUtc, sourceHost, correlationId, target, hwnd, width, height, and sha256. (evidence: dotnet test tests\MouseKeyProxy.Service.Tests -c Release --filter Category=WindowProbeE2E)
- [ ] payton-legion2 to payton-desktop run proves Alt-Space and Win-Arrow effects and clipboard/screenshot correlation.

### TEST-MKP-026

Verify the repository records an ADR concluding physical USB/Bluetooth HID is viable without a custom Windows driver, while software-only virtual HID requires Microsoft driver signing/dashboard flow.

**Acceptance Criteria:**
- [x] ADR concludes a physical USB/Bluetooth HID-compliant device is valid without custom Windows driver, signing key, or Microsoft driver validation. (evidence: docs\adr\ADR-2026-07-07-hid-input-backend-feasibility.md)
- [x] ADR concludes software-only virtual HID requires Microsoft driver signing/dashboard flow. (evidence: docs\adr\ADR-2026-07-07-hid-input-backend-feasibility.md)
- [x] ADR records Microsoft HID transport, keyboard/mouse client driver, VHF, and driver signing sources. (evidence: docs\adr\ADR-2026-07-07-hid-input-backend-feasibility.md)

### TEST-MKP-027

Verify the lab can discover mkp-hid-pi, authenticate to the .NET Pi HID service, and capture before/after Windows PnP evidence for HID enumeration on the controlled host.

**Acceptance Criteria:**
- [x] With MKP_HARDWARE_E2E=1 and missing Pi/network/token/target, the test fails with the missing prerequisite named. (evidence: tests\MouseKeyProxy.Compliance.Tests\HardwareHidComplianceTests.cs)
- [x] mkp hid provision-check reports network status, .NET HID service status, and expected HID device evidence locations. (evidence: docs\receipts-hid-provision-20260707T103933Z.txt)
- [x] Compliance tests fail if the HID appliance path introduces Python implementation files or runtime dependencies. (evidence: dotnet test tests\MouseKeyProxy.Compliance.Tests -c Release --filter Category=HardwareHID)
- [x] The hardware discovery receipt records source host, target host, Pi host, timestamp, and observed PnP/network state. (evidence: docs\receipts-hid-provision-20260707T103933Z.txt)

### TEST-MKP-028

Verify Alt-Space and Win-Arrow injected through the Pi HID backend reach the focused WindowProbe on the target host and produce expected system-menu/window-state evidence.

**Acceptance Criteria:**
- [ ] Alt-Space through Pi HID is observed by WindowProbe as system-menu activation or SC_KEYMENU evidence.
- [ ] Win-Arrow through Pi HID preserves extended arrow semantics and WindowProbe records a dock/move-related bounds or state change.
- [ ] Screenshot metadata includes capturedAtUtc, sourceHost, correlationId, target, hwnd, dimensions, and sha256 matching the WindowProbe run.

### TEST-MKP-029

Verify relative mouse movement, zero-button release, and modifier cleanup through the Pi HID backend with real hardware connected.

**Acceptance Criteria:**
- [ ] Relative mouse reports through Pi HID move the pointer on the controlled host while the source host retains a recoverable state.
- [ ] Pi clear-modifiers emits all-up keyboard and mouse reports, and follow-up WindowProbe input is not contaminated by stuck modifiers.
- [ ] After HID mouse test cleanup, the connected mouse on the gaining system is usable without Alt-Tab.


## TEST-OWNERSHIP

### TEST-OWNERSHIP-001

xUnit v3 and NSubstitute tests must fail first for desired ownership policy and unauthorized service boundary behavior, then pass after the agent/service boundary is implemented.
