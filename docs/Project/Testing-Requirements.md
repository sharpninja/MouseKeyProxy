# Testing Requirements (MouseKeyProxy)

Source: consolidated from Product, MouseKeyProxy-Fresh wiki export, and the July 4, 2026 project direction. Product (`F:\GitHub\MouseKeyProxy`) is the active source of truth.

## TEST-HOTKEY

### TEST-HOTKEY-001

xUnit v3 and NSubstitute tests must fail first for the desired hotkey toggle state machine behavior and compile against the contract seams before production behavior satisfies them.

## TEST-OWNERSHIP

### TEST-OWNERSHIP-001

xUnit v3 and NSubstitute tests must fail first for desired ownership policy and unauthorized service boundary behavior, then pass after the agent/service boundary is implemented.

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
