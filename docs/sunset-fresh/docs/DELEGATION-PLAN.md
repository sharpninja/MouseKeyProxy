# MouseKeyProxy Delegation Plan (via PowerShell.MCP)

**Date**: 2026-07-03 (aligned with PLAN-MKP-006 / PLAN-REV-006-001)

## Workspace roles

| Workspace | Path | Role |
|---|---|---|
| **Orchestration** | `F:\GitHub\MouseKeyProxy-Fresh` | Delegation prompts, `Invoke-Agents.psm1`, session orchestration, receipts for delegation runs. **No product `src/` or `tests/` here.** |
| **Product** | `f:\github\MouseKeyProxy` | All implementation, git, build, test, verify, and harness evidence per PLAN-MKP-006. |

**Authoritative spec**: `f:\github\MouseKeyProxy\docs\PLAN-MKP-006.md` (PLAN-MKP-004 lineage + PLAN-REV-006-001). Delegated agents must treat 006 as the sole requirements source. Do not use PLAN-MKP-004 alone or ad-hoc remediation docs.

## Prerequisites

- `director add-workspace` run in **`f:\github\MouseKeyProxy`** (product root), not Fresh.
- PowerShell.MCP module (1.11.0+) active. All shell via `pwsh__start_console` / `pwsh__invoke_expression` inside the MCP context. Never invoke `pwsh.exe` or `powershell.exe` directly from the orchestrator.
- Product workspace marker: `AGENTS-README-FIRST.yaml` at `f:\github\MouseKeyProxy` (via director).
- Fresh orchestration marker: `AGENTS-README-FIRST.yaml` with `workspace: MouseKeyProxy-Fresh` for Grok/orchestrator session only.
- All delegated prompts **must** start exactly with:
  ```
  Process `AGENTS-README-FIRST.yaml` and follow all procedures defined within.
  ```
- Byrd Development Process V4: tests first, mocks validated, then impl, 100% green before exiting any gate.
- MCP plugin only for sessionlog / todo / requirements (never edit `docs/todo.yaml` directly).
- No em-dashes in any output.

## PLAN-REV-006-001 constraints (delegate must enforce)

These are native plan requirements; every delegation prompt must restate the gates relevant to that slice:

1. **Visibility Gate**: Work only from `f:\github\MouseKeyProxy`. MCP re-anchor to this root. Dirty uncommitted `src/` (`git status --porcelain 'src/'` shows `M src/...`). Terminal capture includes `diff --git a/src/...`. Do not proceed to other ACs if CHANGED_FILES would not contain `src/`.
2. **Harness Evidence Iteration**: `TEST-MKP-012` green before Construction slices. Budget multiple verify cycles; harness evidence is orthogonal to code correctness.
3. **Artifact Contract**: After final `verify-goal.ps1`, scratch has exactly six files: `git-visibility.log`, `build.log`, `full-test-output.log`, `repl-install.log`, `repl-run.log`, one `*.nupkg`. No `test-*.log`, `verif-*.log`, or extras. Product `src/` visibility is in `git-visibility.log` (not Fresh CHANGED_FILES).
4. **Error-Path Matrix**: Verify env returns gRPC Unavailable. Real attempt then null-client `BidiSessionTransport` fallback. `toggle FAILED` + non-zero exit. No swallowed `try/catch` in `ToggleAsync`.
5. **Shipped-code test contract**: `SendInputBatchAsync` builds frames before client check; `RecordingTransport` ctor-only; `SentFrames.Count >= 2` for Emit branch.
6. **Inception blockers**: `docs/wireframes/`, `assets/` logo, `build/Build.cs`, `tests/MouseKeyProxy.Integration/`, MCP artifacts are gates, not optional polish.
7. **Sole verify entrypoint**: `pwsh -ExecutionPolicy Bypass -File scripts/verify-goal.ps1`. Full verify before any goal-completion claim. Receipts required.

## Proven invocation method

All agent invocations run inside a PowerShell.MCP console. Logic is in `scripts/Invoke-Agents.psm1` (`Invoke-Codex`, `Invoke-Claude`). Cmdlets inherit the active MCP context; they do not start their own console.

```powershell
Import-Module .\scripts\Invoke-Agents.psm1 -Force
Invoke-Codex -PromptFile prompts\<prompt>.md
Invoke-Claude -PromptFile prompts\<prompt>.md
```

Cmdlet behavior:
- Reads prompt file (AGENTS prefix required)
- Launches agent with proven non-interactive flags
- Logs output with timestamps

**Codex**: `codex.exe exec --dangerously-bypass-approvals-and-sandbox <prompt-content>`

**Claude**: `claude.exe --model opus --effort max --dangerously-skip-permissions --permission-mode bypassPermissions --print <prompt-content>`

Delegated agents receive prompts that instruct them to `cd f:\github\MouseKeyProxy` (or equivalent) before any file edit, build, git, or verify operation.

## Delegation sequence (Byrd / 006 phases)

Orchestrator (Grok in Fresh) drafts prompts; execution agents (Codex primary for impl, Claude for review) run against the **product** workspace.

| Order | Phase / slice | Primary delegate | Gate before next |
|---|---|---|---|
| 0 | Harness Evidence Iteration | Codex | `TEST-MKP-012` green; visibility + artifact receipts |
| 1 | Elaboration: wireframes + assets + Nuke skeleton | Codex | SVGs exist; `build/Build.cs` stub; logo in `assets/` |
| 2 | Elaboration: mocks/stubs + shipped-code tests | Codex | Commands.Tests contract tests red then green |
| 3 | Construction Slice 1: hotkey toggle + input (single remote) | Codex | `TEST-MKP-001`..`003` green; harness receipts maintained |
| 4 | Construction Slice 2: IPC + service/tray + gRPC + scheduled task | Codex | `TEST-MKP-002`, `007`; tray task `MouseKeyProxyTray` |
| 5 | Construction Slice 3: LIFO clipboard + failsafes | Codex | `TEST-MKP-004`, `006`, `008` green |
| 6 | Construction Slice 4: REPL payloads + Mirror + wireframe UI | Codex | `TEST-MKP-010`; full tray menu per wireframes |
| 7 | Construction Slice 5: Nuke full + Integration + MCP compliance | Codex | `TEST-MKP-009`, `011`, Integration project exists |
| 8 | Transition | Codex + manual 2x Win11 | E2E smoke per 006 verification gates |

**Review checkpoints**: After slices 2, 4, and 7, orchestrator may delegate a Claude review prompt against the product diff and PLAN-MKP-006 ACs.

**Construction is gated**: No Construction delegation until Harness Evidence Iteration receipts exist.

## Prompt template requirements

Each prompt file under `prompts/` must include:

1. AGENTS prefix (exact string above)
2. Explicit product workspace: `f:\github\MouseKeyProxy`
3. Reference: `docs/PLAN-MKP-006.md` section(s) for the slice
4. Byrd rule: tests first for the slice; red receipt before impl
5. Harness rules applicable to the slice (from REV-006-001 list)
6. Deliverables list with receipt file name (e.g. `docs/receipts-slice-N.txt`)
7. Forbidden: editing Fresh workspace for product code; direct `todo.yaml` edits; standalone `dotnet test` completion claims without `verify-goal.ps1` when slice requires verify

Planned prompt files (to be authored before delegation):

| File | Purpose |
|---|---|
| `prompts/harness-evidence-codex.md` | Visibility gate + `TEST-MKP-012` + artifact contract |
| `prompts/elaboration-wireframes-nuke-codex.md` | SVGs, assets, Nuke skeleton |
| `prompts/elaboration-mocks-codex.md` | Mocks + Commands.Tests shipped-code contract |
| `prompts/construction-slice1-codex.md` | Hotkey toggle + input matrix |
| `prompts/construction-slice2-codex.md` | IPC, service/tray, scheduled task |
| `prompts/construction-slice3-codex.md` | Clipboard LIFO + failsafes |
| `prompts/construction-slice4-codex.md` | REPL payloads + wireframe tray UI |
| `prompts/construction-slice5-codex.md` | Nuke full, Integration, MCP compliance |
| `prompts/transition-lab-e2e-codex.md` | Phase 8: payton-legion2 + payton-desktop lab E2E, no skips |
| `prompts/review-slice-N-claude.md` | AC review against 006 |

## Test proofs (cmdlet plumbing verified)

Historical proofs confirm delegation **infrastructure** works. They do not constitute product delivery.

- **Codex test** (`prompts/test-codex.md`): created `codex-test-output.txt` with success marker (2026-07-03).
- **Claude test** (`prompts/test-claude.md`): created `claude-test-output.txt` with success marker (2026-07-03).

**Superseded**: Phase 1 red test delegation (`prompts/phase1-codex-red-tests.md`, `PLAN-PHASE-001`) targeted an earlier Fresh-local skeleton. That approach is retired. Product implementation is a **fresh one-shot build** from PLAN-MKP-006 in `f:\github\MouseKeyProxy` only.

## Orchestrator responsibilities (Grok in Fresh)

1. Draft/update prompts per table above; user approval before `Invoke-Codex` / `Invoke-Claude`.
2. Route all shell through PowerShell.MCP.
3. MCP session log + TODO for each delegation turn (via plugin, not direct yaml).
4. Collect receipts from product workspace after each delegate run.
5. Verify harness gates mechanically (`git diff`, scratch listing, log substrings) before claiming slice complete.
6. Do not assert delegate success without reading receipt artifacts on disk.

## Next steps

Phases 0-7 completed via consolidated Grok implementer run (2026-07-03). Receipts: `f:\github\MouseKeyProxy\docs\receipts-delegation-phase-0.txt` through `phase-7.txt` and `receipts-delegation-completion.txt`.

Verification: `scripts/verify-goal.ps1` exit 0; scratch at `C:\Users\kingd\AppData\Local\Temp\grok-goal-517f749f32af\implementer` with four canonical artifacts.

Phase 8 (Transition) is **in scope**, delegated to **Codex** via `prompts/transition-lab-e2e-codex.md` (`Invoke-Codex`). Lab pair: `payton-legion2` + `payton-desktop` (see `LabTopology.cs`). Zero skipped `Category=TwoMachineE2E` tests required.

## Receipts / artifacts

**Fresh (orchestration)**:
- `docs/DELEGATION-PLAN.md` (this file)
- `scripts/Invoke-Agents.psm1`
- `codex-test-output.txt`, `claude-test-output.txt` (plumbing proofs)
- `prompts/` (to be populated per table above)

**Product (implementation evidence)**:
- `f:\github\MouseKeyProxy\docs\PLAN-MKP-006.md`
- `docs/receipts-*.txt`, `docs/proto-verify-*.txt` (per slice)
- Harness scratch artifacts after `verify-goal.ps1` (four canonical files only at claim time)