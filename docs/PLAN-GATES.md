# MouseKeyProxy Plan Gates

This document lists the current machine-checkable gates for the remaining plan completion work. It is a Codex-owned validation artifact, not product implementation code.

## Green Gates

- Moq is banned and guarded by `tests/MouseKeyProxy.Compliance.Tests/TestDoubleComplianceTests.cs`.
- Fresh sunset artifacts and consolidated requirements have been migrated to the Product workspace and mirrored back into Fresh wiki folders.
- Agent invocation wrappers exist and dry-run with visible parameter summaries and full call signatures.
- Advanced gRPC control service seam is implemented and guarded by `tests/MouseKeyProxy.Service.Tests/AdvancedControlServiceTests.cs`.
- Agent dashboard UI and hacker mouse workstation branding are implemented and guarded by `tests/MouseKeyProxy.Compliance.Tests/AgentUiBrandingComplianceTests.cs`.
- Real paired-control proof is green: `payton-legion2` paired with `payton-desktop`, moved the Desktop cursor, focused a Desktop Notepad window, injected `MKP-CONTROL-PROOF`, and passed smoke validation.

## Active MCP TODOs

- `MKP-CTRL-001` - Codex implemented the advanced control service seam after approval; Codex validates advanced-control tests, service tests, Moq scan, and readiness gate.
- `MKP-UI-001` - Codex implemented the dashboard UI and hacker mouse workstation branding after approval; Codex validates UI/branding tests, visual receipts, Moq scan, and readiness gate.
- `MKP-LAB-001` - Completed; Codex validated the real paired-control lab proof that payton-legion2 controls payton-desktop.

## Completed External Proof

- Real paired-control proof (`MKP-LAB-001`):
  - Script: `scripts/assert-paired-control-proof.ps1`
  - Wired into: `scripts/run-transition-e2e.ps1` and `scripts/verify-goal.ps1`
  - Passing receipt lines:
    - `PAIRING: PASS local=payton-legion2 remote=payton-desktop`
    - `CURSOR_CONTROL: PASS from=payton-legion2 to=payton-desktop ...`
    - `SENTINEL_INPUT: PASS from=payton-legion2 to=payton-desktop text=MKP-CONTROL-PROOF ...`
    - `SMOKE: PASS`
  - Evidence:
    - `docs/receipts-transition-e2e.txt`
    - `docs/receipts-plan-readiness-20260704T141229Z.txt`
    - `C:\Users\kingd\AppData\Local\Temp\grok-goal-517f749f32af\implementer\paired-control-proof.log`

## Readiness Command

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-plan-readiness.ps1
```

The readiness command writes `docs/receipts-plan-readiness-<timestamp>.txt`; the latest validated run exits 0.
