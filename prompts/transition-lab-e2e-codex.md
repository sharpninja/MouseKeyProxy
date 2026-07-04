Process `AGENTS-README-FIRST.yaml` and follow all procedures defined within.

## Workspace

All work in **`f:\github\MouseKeyProxy`** only. `cd f:\github\MouseKeyProxy` before any edit, build, git, or verify operation. Do not edit `MouseKeyProxy-Fresh`.

## Spec

`docs/PLAN-MKP-006.md` Transition phase (two-machine Win11 E2E + soak). Authoritative lab pair:

| Role | Host |
|---|---|
| Node A | `payton-legion2` |
| Node B | `payton-desktop` |

`src/MouseKeyProxy.Common/LabTopology.cs` encodes this pair. **No test may skip.** Zero skipped tests in the validation scope.

## Gates (PLAN-REV-006-001)

1. Visibility: dirty `src/`; `git status --porcelain 'src/'` shows `M src/...`.
2. Sole verify entrypoint: `pwsh -ExecutionPolicy Bypass -File scripts/verify-goal.ps1` before completion claim.
3. Error-path matrix: real gRPC attempt then honest failure paths documented in receipts.
4. Batch elevated commands: write `.ps1`, single `gsudo pwsh -ExecutionPolicy Bypass -File <script>` call, delete script after.

## Your task (Phase 8 delegate)

### 1. Deploy service on BOTH lab machines

On **each** of `payton-legion2` and `payton-desktop`:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/install-lab-service.ps1
```

Run locally on the machine you are on; for the peer, run the same script on that machine (WinRM, RDP session, or physical access). Install must leave gRPC listening on **`0.0.0.0:50051`** (HTTP/2).

Verify:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-lab-grpc.ps1
```

Both hosts must report `REACHABLE`.

### 2. Run two-machine E2E smoke

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/run-transition-e2e.ps1
```

Must exit 0 with `SMOKE: PASS` in `docs/receipts-transition-e2e.txt`. No `REMOTE: SKIPPED`, no `SMOKE: PARTIAL`.

### 3. Run automated lab tests (no skips)

```powershell
dotnet test MouseKeyProxy.slnx -c Release --filter "Category=TwoMachineE2E" --verbosity normal
```

**Required:** 0 failed, **0 skipped**. Tests in `tests/MouseKeyProxy.Integration/TransitionE2ETests.cs` must TCP-connect to both `payton-legion2` and `payton-desktop` on port 50051.

### 4. Full harness verify

```powershell
pwsh -ExecutionPolicy Bypass -File scripts/verify-goal.ps1
```

Exit 0; six scratch artifacts per artifact contract.

### 5. Start / validate soak

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/run-soak.ps1
```

24h gate per plan; capture receipt under `docs/receipts-soak-*.txt`. If starting long soak, document PID and receipt path.

## Deliverables

Write **`docs/receipts-delegation-phase-8-codex.txt`** with:

- Delegate: Codex
- Date (UTC)
- Machine you ran from
- Per-step exit codes and command output excerpts
- `test-lab-grpc.ps1` output (both REACHABLE)
- `TwoMachineE2E` filter result (passed count, skipped must be 0)
- `verify-goal.ps1` exit code
- `receipts-transition-e2e.txt` final line (`SMOKE: PASS` or failure reason)

## Forbidden

- `Assert.Skip` or receipt-fallback passes for lab connectivity
- Editing Fresh orchestration workspace
- Direct `docs/todo.yaml` edits
- Claiming pass without machine-verifiable command output in receipt

## Byrd

Tests are the ledger. If `TwoMachineE2E` fails, fix service bind (`src/MouseKeyProxy.Service/Program.cs` Kestrel on 50051), payload publish (`MouseKeyProxy.Repl.csproj` always republish payloads), reinstall, re-test until 0 skip / 0 fail.