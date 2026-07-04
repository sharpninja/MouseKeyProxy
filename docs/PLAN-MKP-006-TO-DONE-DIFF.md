# PLAN-MKP-006 to Done Diff

Date: 2026-07-04
Baseline: `docs/PLAN-MKP-006.md`
Done evidence: `docs/PLAN-GATES.md`, `docs/receipts-transition-e2e.txt`, `docs/receipts-plan-readiness-20260704T144720Z.txt`, `docs/Project/wiki/github/*.md`
Local completion commits: `5008f0e` and `7554434`

This document is the transition diff for moving PLAN-MKP-006 from an elaboration/construction plan to a done-state claim. It does not replace the original plan. It identifies what changed, which requirements expanded after 006, and which evidence must be present before another agent can call the project done.

Remote synchronization is intentionally separated from implementation completion. The local repository contains the completion commits listed above. A final wrap-up still needs a verified `git push origin master` result if remote sync is required for the handoff.

## Executive Diff

PLAN-MKP-006 started as a combined plan with construction gated on harness evidence. The done state is no longer just a plan or harness target: the product workspace now contains implemented service/agent control paths, REPL and cmdlet surfaces, UI/branding requirements, consolidated Fresh sunset artifacts, MCP requirements exports, and a real two-host paired-control receipt.

The strict done condition from the user is satisfied by the paired-control evidence: `payton-legion2` paired with `payton-desktop`, moved the Desktop cursor, focused a Desktop Notepad window, injected sentinel text, and passed the transition smoke gate.

## Baseline 006 Scope

PLAN-MKP-006 covered these core requirements:

| Plan 006 item | Baseline intent |
| --- | --- |
| `FR-MKP-001` | Hotkey-only toggle with no edge crossing. |
| `FR-MKP-002` | Keyboard and mouse focus follow the active machine. |
| `FR-MKP-003` | Full supported proxy input, with excluded Windows secure/UIPI cases failing observably. |
| `FR-MKP-004` | Real-time LIFO clipboard sync with encrypted local persistence. |
| `FR-MKP-005` | Advanced gRPC controls: `InjectInput`, `SetMousePosition`, `LocateProcess`, and `SetFocusByHwnd`. |
| `FR-MKP-006` | Setup, REPL, service lifecycle, LocalAppData state, and explicit service management. |

PLAN-MKP-006 also required these process and evidence gates:

| Gate | Baseline intent |
| --- | --- |
| Visibility gate | Completion claims require observable behavior and durable receipts, not just code shape. |
| Artifact contract | `verify-env` and post-run scratch artifacts must have exact contracts. |
| Error-path matrix | gRPC `Unavailable` and related setup failures must be observable and actionable. |
| Shipped-code test contract | Tests must exercise shipped command code, not duplicate-only test logic. |
| Wireframe-driven tray UI | UI had to be deliberate and validated, not a throwaway diagnostic window. |
| MCP compliance | TODOs, requirements, logs, and exports must remain traceable through MCP. |

## Requirements Diff After 006

The current requirements export expands the done definition beyond the original six FRs. The added requirements below are not optional embellishments; they reflect explicit user requirements added during completion work.

| Current requirement | Delta from PLAN-MKP-006 | Done evidence |
| --- | --- | --- |
| `FR-MKP-005` | Strengthened from advanced gRPC contract to real paired-control proof. | `docs/receipts-transition-e2e.txt`, `scripts/assert-paired-control-proof.ps1`, `docs/PLAN-GATES.md`. |
| `FR-MKP-006` | Expanded setup/REPL/service lifecycle to include usable agent dashboard UI. | `docs/PLAN-GATES.md`, `tests/MouseKeyProxy.Compliance.Tests/AgentUiBrandingComplianceTests.cs`, readiness receipt. |
| `FR-MKP-007` | Added full logging requirement through `ILogger` and Windows Event Viewer. | Current requirements matrix and test requirements export. |
| `FR-MKP-008` | Added required hacker mouse workstation branding. | `docs/PLAN-GATES.md`, `AgentUiBrandingComplianceTests`, readiness receipt. |
| `FR-MKP-009` | Added Codex-design/testing and Claude-implementation workflow requirement. | Current requirements export, handoff prompts, docs, and session receipts. |
| `FR-MKP-010` | Added agent invocation observability: parameter summary, full call signature, free stdout/stderr flow. | Current requirements export and cmdlet implementation tests. |
| `FR-MKP-011` | Added hard Moq ban and NSubstitute-only test-double rule. | `tests/MouseKeyProxy.Compliance.Tests/TestDoubleComplianceTests.cs`, readiness receipt. |
| `FR-HOTKEY-001` | Added compile-time hotkey contract requirement for red-first behavior. | Current requirements matrix and `tests/MouseKeyProxy.Common.Tests/ToggleStateTests.cs`. |
| `FR-OWNERSHIP-001` | Added explicit service-vs-agent ownership boundary. | Current requirements matrix and ownership tests. |

## Implementation Diff

| Area | 006 target | Done-state delta | Evidence |
| --- | --- | --- | --- |
| Advanced control service | Define and test gRPC advanced controls. | Implemented service-to-agent control path for set mouse, locate process, focus HWND, and inject text/input. | `FR-MKP-005`, `TEST-MKP-009` through `TEST-MKP-011`, `docs/receipts-transition-e2e.txt`. |
| Real paired control | Completion requires actual visible remote control. | `payton-legion2` controlled `payton-desktop`: pairing, cursor movement, Notepad focus, sentinel text injection. | `PAIRING: PASS`, `CURSOR_CONTROL: PASS`, `SENTINEL_INPUT: PASS`, `SMOKE: PASS`. |
| Service/agent split | Service must not perform direct desktop input. | Desktop-affecting input is routed through the user-session agent and named-pipe/control seams. | Ownership and service tests; readiness receipt. |
| REPL/service lifecycle | REPL manages pairing/settings/service lifecycle and status. | REPL and scripts participate in transition E2E, goal verification, service checks, and paired-control smoke. | `scripts/run-transition-e2e.ps1`, `scripts/verify-goal.ps1`, readiness receipt. |
| UI | 006 required tray UI and wireframe-driven design. | Agent dashboard and tray branding were implemented and made testable. | `AgentUiBrandingComplianceTests`, `docs/PLAN-GATES.md`, readiness receipt. |
| Branding | Not originally the central 006 theme. | Branding now centers on a hacker mouse typing at a keyboard at a desk surrounded by monitors. | `FR-MKP-008`, branding tests, requirements export. |
| Agent cmdlets | Not a baseline 006 requirement. | Codex/Claude agent cmdlets must print parameter summaries, echo full call signatures, and let output flow to host. | `FR-MKP-010`, current requirements matrix, cmdlet tests. |
| Clipboard | 006 required LIFO clipboard sync and persistence. | Requirements and tests track LIFO merge, bounded history, DPAPI persistence, and reload behavior. | `FR-MKP-004`, `TEST-MKP-004`, `TEST-MKP-006`, `tests/MouseKeyProxy.Common.Tests/LifoClipboardTests.cs`. |
| Fresh sunset | 006 referenced Fresh migration as project context. | Product workspace now contains migrated Fresh docs, artifacts, scripts, requirements, and sunset records. | `docs/sunset-fresh/`, `docs/PLAN-GATES.md`. |
| Test doubles | 006 did not ban Moq by itself. | Moq is banned; NSubstitute is the accepted test-double framework. | `FR-MKP-011`, `TestDoubleComplianceTests`, readiness receipt. |
| Requirements export | 006 required MCP traceability. | Current wiki exports exist under `docs/Project/wiki`, plus ZIP export under `docs/requirements/requirements-wiki-documents.zip`. | `7554434 docs: export MCP requirements wrap-up`. |

## Done Gate To Reproduce

A follow-on agent can move from 006 to done only when all checks below are true.

1. Verify the local source contains the completion commits:

```powershell
git log --oneline -5
```

Required local commits:

```text
7554434 docs: export MCP requirements wrap-up
5008f0e feat: complete MouseKeyProxy paired-control proof
```

2. Run the readiness gate:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-plan-readiness.ps1
```

Expected result:

```text
PLAN READINESS: PASS
```

3. Confirm paired-control proof remains present:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/assert-paired-control-proof.ps1
```

Expected receipt facts:

```text
PAIRING: PASS local=payton-legion2 remote=payton-desktop
CURSOR_CONTROL: PASS from=payton-legion2 to=payton-desktop
SENTINEL_INPUT: PASS from=payton-legion2 to=payton-desktop text=MKP-CONTROL-PROOF
SMOKE: PASS
```

4. If the lab proof is stale or contested, rerun the full transition flow with WSMan credentials from `~/.creds`:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/run-transition-e2e.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/verify-goal.ps1
```

5. Confirm the MCP requirements matrix has current mappings for `FR-MKP-001` through `FR-MKP-011`, `FR-HOTKEY-001`, and `FR-OWNERSHIP-001`:

```powershell
rg -n '^\| (FR-MKP|FR-HOTKEY|FR-OWNERSHIP)-' docs/Project/wiki/github/Requirements-Matrix.md docs/Project/wiki/github/TR-per-FR-Mapping.md
```

6. Confirm there are no remaining implementation TODO gates under the MKP workstream. Historical plan/process TODOs are not implementation blockers unless they reference an unverified product requirement.

7. Validate workspace hygiene:

```powershell
git diff --check
git status --short
```

8. If remote completion is required, push and verify remote head:

```powershell
git push origin master
git ls-remote origin refs/heads/master
```

The done claim is not remote-synced until the remote head matches the local completion commit.

## Done-State Decision

PLAN-MKP-006 can be marked done when these conditions are simultaneously true:

- `scripts/check-plan-readiness.ps1` passes.
- Real paired-control evidence still shows `payton-legion2 -> payton-desktop` control.
- Requirements exports include all current FR/TR/TEST mappings introduced during the project.
- The Moq ban scan passes and active tests use NSubstitute or purpose-built fakes/stubs.
- Hacker mouse workstation branding is present and covered by tests/receipts.
- Fresh sunset artifacts and requirements are migrated into the Product workspace.
- The repository is committed locally.
- Remote sync is verified separately if the handoff requires GitHub state to match local state.

## Open Caveat

The prior wrap-up produced local commit `7554434`, but the runtime blocked `git push origin master` during that turn. Do not state that GitHub is current until a later push succeeds and `git ls-remote origin refs/heads/master` matches local `HEAD`.
