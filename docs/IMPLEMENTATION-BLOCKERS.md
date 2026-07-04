# MouseKeyProxy Implementation Blockers

This is a Codex-owned execution note for the active finish plan. It does not change product requirements or product implementation code.

## Claude execution blocked by tenant policy

The project requirement says Codex owns design/testing/receipts and Claude owns product implementation code. The approved project wrapper is `scripts/Invoke-Agents.psm1` with `Invoke-Claude`.

Current policy evidence:

- `docs/receipts-claude-advanced-control-policy-block-20260704T122900Z.txt` - `Invoke-Claude` for `prompts/implementation-advanced-control-claude.md` was blocked before execution because private workspace context would be sent to an external Claude service.
- `docs/receipts-claude-ui-branding-policy-block-20260704T123200Z.txt` - `Invoke-Claude` for `prompts/implementation-ui-branding-claude.md` was blocked before execution for the same tenant-policy reason.

Codex must not route around the policy rejection. The red implementation gates remain blocked until one of these is true:

- an approved Claude execution path is available for this workspace,
- the user explicitly changes the implementation-ownership constraint so Codex may edit product implementation code,
- or the required product implementation is supplied by another approved mechanism and Codex can validate it.

## Gates still red

- `MKP-CTRL-001`: advanced control service seam is not implemented; `AdvancedControlServiceTests` still fails to compile because `IRemoteDesktopController` is missing.
- `MKP-UI-001`: dashboard UI and hacker mouse branding are not implemented; `AgentUiBrandingComplianceTests` still fails on 32x32 logo and placeholder tray UI strings.
- `MKP-LAB-001`: paired-control proof is missing; `docs/receipts-transition-e2e.txt` lacks `PAIRING: PASS`, `CURSOR_CONTROL: PASS`, and `SENTINEL_INPUT: PASS` evidence for payton-legion2 controlling payton-desktop.

Latest readiness receipt at creation time: `docs/receipts-plan-readiness-20260704T123253Z.txt`.
