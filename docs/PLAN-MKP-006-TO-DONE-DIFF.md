# PLAN-MKP-006 to Current State Diff

Date: 2026-07-07
Baseline: `docs/PLAN-MKP-006.md`
Current pushed head: `564cfd3 feat: expose canonical CLI controls`
Remote sync: `origin/master` verified to match local `HEAD` at `564cfd38aed951eefe9f09d669579afd2fe17249`

This document updates the prior 006-to-done diff to the current project state. It does not replace `docs/PLAN-MKP-006.md`; it records what changed after that plan, what is implemented now, what evidence exists, and what must still be live-validated before a final "done" claim is defensible.

## Current Summary

PLAN-MKP-006 began as an implementation plan for paired MouseKeyProxy control. The repository has since moved beyond the original six FRs: it now includes service/agent control paths, WinForms tray/dashboard UI, hacker mouse workstation branding, Event Viewer logging, Fresh sunset artifacts, MCP requirements exports, exclusive remote-control semantics, emergency release over local pipe and gRPC, global CA1416 suppression, AGENTS/CLAUDE workspace instructions, and a canonical CLI control surface.

The code, docs, tests, and GitHub state are current at `564cfd3`. The latest full Release test run passed 73 tests with zero warnings. Local deployment on `PAYTON-LEGION2` succeeded and the installed `mkp.exe` exposes the current CLI commands.

The live two-host done claim still needs one current end-to-end validation: `mkp emergency-release --json` performed local cleanup on `PAYTON-LEGION2` but returned `EMERGENCY_RELEASE_PARTIAL` because the Desktop peer agent pipe timed out. That means the repository is current, but Desktop-side interactive agent availability must be restored and revalidated before claiming "legion2 is controlling desktop now" as current runtime fact.

## Baseline 006 Scope

| Plan 006 item | Baseline intent |
| --- | --- |
| `FR-MKP-001` | Hotkey-only toggle with no edge crossing. |
| `FR-MKP-002` | Keyboard and mouse focus follow the active machine. |
| `FR-MKP-003` | Full supported proxy input, with excluded Windows secure/UIPI cases failing observably. |
| `FR-MKP-004` | Real-time LIFO clipboard sync with encrypted local persistence. |
| `FR-MKP-005` | Advanced gRPC controls: `InjectInput`, `SetMousePosition`, `LocateProcess`, and `SetFocusByHwnd`. |
| `FR-MKP-006` | Setup, REPL, service lifecycle, LocalAppData state, and explicit service management. |

PLAN-MKP-006 also required observable behavior, exact artifact contracts, actionable error paths, shipped-code tests, deliberate tray UI, and MCP traceability.

## Requirements Diff After 006

| Current requirement | Delta from PLAN-MKP-006 | Current evidence |
| --- | --- | --- |
| `FR-MKP-005` | Strengthened from advanced gRPC contract to real paired-control proof. | `docs/receipts-transition-e2e.txt`, `scripts/assert-paired-control-proof.ps1`, gRPC service tests. |
| `FR-MKP-006` | Expanded setup/REPL/service lifecycle to a usable agent dashboard and a canonical CLI control surface. | `src/MouseKeyProxy.Repl/Program.cs`, `tests/MouseKeyProxy.Repl.Tests/ReplBidiConstructionTests.cs`, local `mkp status --json` smoke. |
| `FR-MKP-007` | Added Windows Event Viewer logging requirement. | `mkp logs`, tray/dashboard `Open logs`, EventLog source setup, logging tests. |
| `FR-MKP-008` | Added required hacker mouse workstation branding. | `assets/logo.branding.md`, UI/branding compliance tests, wireframes. |
| `FR-MKP-009` | Added Codex-design/testing and Claude-implementation workflow requirement. | `AGENTS.md`, `CLAUDE.md`, session/handoff docs. |
| `FR-MKP-010` | Added agent invocation observability: parameter summary, full call signature, free stdout/stderr flow. | Requirements docs, cmdlet guidance, prompts. |
| `FR-MKP-011` | Added hard Moq ban and NSubstitute-only test-double rule. | `TestDoubleComplianceTests`, Moq source/package scans, NSubstitute tests. |
| `FR-HOTKEY-001` | Added compile-time hotkey contract and fallback-hook expectations. | `Win32HotkeyMonitor` tests, Ctrl+Alt+F1 fixes. |
| `FR-OWNERSHIP-001` | Added explicit service-vs-agent ownership boundary. | Service-to-agent pipe seams and ownership tests. |

## Implementation Diff

| Area | 006 target | Current-state delta | Evidence |
| --- | --- | --- | --- |
| Advanced control service | Define and test gRPC advanced controls. | Implemented service-to-agent control path for set mouse, locate process, focus HWND, inject input, and emergency release. | `MouseKeyProxyImpl`, `AgentControlPipeClient`, service tests. |
| Exclusive input forwarding | Proxy active-machine input. | Mirror Mode removed; forwarding now consumes local keyboard/mouse events while active, except explicit control chords. | `RemoteInputForwarder`, hotkey/forwarding tests. |
| Emergency release | Restore local control. | Local UI/CLI release stops forwarding and can notify peer; peer gRPC release routes to the agent pipe and avoids recursive peer notification unless requested. | `EmergencyRelease` RPC, `NotifyPeer`, agent/service/common tests. |
| Canonical CLI | REPL manages setup/control. | CLI is now the canonical control surface: `status`, `agent status`, `pair status`, `emergency-release`, `release`, `logs`, service lifecycle, remote-control commands. UI must not expose CLI-missing controls. | `564cfd3`, REPL tests, requirements docs. |
| Service/agent split | Service must not perform direct desktop input. | Desktop-affecting operations route through the user-session agent and named-pipe/control seams. | Common/service/agent tests. |
| UI and branding | Wireframe-driven tray UI. | Dashboard/tray controls were implemented, actions gated by pairing/connection, Event Viewer opens to MKP log, icon uses MKP branding. | UI source/tests and wireframes. |
| Logging | Not fully specified in 006. | Logs go to Windows Event Viewer via dedicated MouseKeyProxy log/source instead of arbitrary folders. | RePL install setup and UI log launcher. |
| Warnings policy | Warnings must be fixed. | `TreatWarningsAsErrors=true`; CA1416 is globally suppressed by approval, and latest Release test run emitted zero warnings. | `Directory.Build.props`, full test output. |
| Fresh sunset | Migration context. | Fresh docs, artifacts, scripts, and requirement exports are migrated under `docs/sunset-fresh/`. | `docs/sunset-fresh/`. |
| Workspace instructions | Not part of 006. | `AGENTS.md` and `CLAUDE.md` are committed workspace contract documents. | `3d9b831`. |

## Current Evidence Snapshot

| Evidence | Current result |
| --- | --- |
| Local/remote git head | Local `HEAD` and `origin/master` match `564cfd38aed951eefe9f09d669579afd2fe17249`. |
| Full Release tests | `dotnet test MouseKeyProxy.slnx -c Release --no-restore` passed 73 tests, zero warnings. |
| Local deploy | `scripts/install-lab-service.ps1` completed on `PAYTON-LEGION2`; service reachable at `payton-legion2:50051`. |
| Installed CLI help | Installed temp `mkp.exe` shows `status`, `agent status`, `pair status`, `emergency-release`, `release`, and `logs`. |
| Local status | `mkp status --json` reports service `Running`, agent connected to `payton-desktop`, `forwardingActive=false`. |
| Emergency release smoke | `mkp emergency-release --json` cleaned local state but returned `EMERGENCY_RELEASE_PARTIAL` due Desktop peer agent pipe timeout. |

## Done Gate To Reproduce

Run these from `F:\GitHub\MouseKeyProxy`.

1. Verify current source and GitHub sync:

```powershell
git log --oneline -8
git rev-parse HEAD
git ls-remote origin refs/heads/master
```

Expected head:

```text
564cfd38aed951eefe9f09d669579afd2fe17249
```

2. Run the full Release suite:

```powershell
dotnet test MouseKeyProxy.slnx -c Release --no-restore --logger "console;verbosity=minimal"
```

Expected result: all test projects pass, total 73 tests, zero warnings other than globally suppressed CA1416.

3. Confirm canonical CLI surface:

```powershell
$mkp = 'C:\Users\kingd\AppData\Local\Temp\mkp-lab-install\tool\mkp.exe'
& $mkp --help
& $mkp status --json
& $mkp pair status --json
& $mkp emergency-release --json
```

Expected local status facts:

```text
service.status=Running
agent.remotePeer=payton-desktop
agent.remoteState=Connected
agent.forwardingActive=false after release
```

4. Re-run live paired-control proof with all required parameters:

```powershell
$receipt = 'docs\receipts-transition-e2e.txt'
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\assert-paired-control-proof.ps1 -ReceiptPath $receipt -LocalHost 'payton-legion2' -RemoteHost 'payton-desktop' -Sentinel 'MKP-CONTROL-PROOF' -RequireSmokePass
```

If the receipt is stale or contested, run the full transition flow with valid WSMan access and then re-run the proof:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\run-transition-e2e.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\verify-goal.ps1
```

5. Validate workspace hygiene:

```powershell
git diff --check
git status --short
```

## Current Done-State Decision

The repository implementation is current and pushed, but the live runtime done claim is conditional.

Implemented and current:

- Mirror Mode is removed from product behavior, UI, tests, and current wireframes.
- Keyboard/mouse forwarding is exclusive to one machine while remote control is active.
- Emergency Release exists locally, over the agent pipe, over gRPC, and through the canonical CLI.
- CLI/REPL is the canonical control surface.
- UI and CLI both expose status/logs/release paths.
- CA1416 is globally suppressed by approval; all other warnings are treated as errors.
- Moq remains banned.
- `AGENTS.md` and `CLAUDE.md` are committed.
- GitHub is synchronized to the current head.

Current live caveat:

- Desktop-side agent availability is not currently proven. The latest `mkp emergency-release --json` on `PAYTON-LEGION2` returned partial failure because the peer release path timed out reaching the Desktop agent pipe. Before declaring the project done in the user's strict sense, `PAYTON-DESKTOP` must have an interactive agent session available and `payton-legion2` must be revalidated controlling `payton-desktop`.

## Commit Timeline Since 006

```text
564cfd3 feat: expose canonical CLI controls
3d9b831 docs: add agent workspace instructions
2aedf65 fix: enforce exclusive remote control
411471d fix: make ctrl-alt-f1 hotkey reliable
61b4536 fix: sync repl pairing state to agent
9d20978 fix: clarify unpaired endpoint state
d5a7ce9 fix: open dedicated mkp event log
3e89668 fix: gate remote dashboard actions
```
