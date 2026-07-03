# Codex Final Review: PLAN-MKP-003

**Reviewed plan**: `docs/PLAN-MKP-003.md`  
**Review date**: 2026-07-03  
**Reviewer**: Codex  
**Final go/no-go**: **NO-GO for Construction and no-go for Byrd gate approval as written.**

This is not a broad design rejection. PLAN-MKP-003 is much closer than prior versions and most hard architecture decisions are now in the right shape. The remaining problems are specific and fixable, but two of them invalidate the plan as a handoff artifact: the "locked" proto contract still cannot compile or guide implementation, and the exported requirements source of record still contradicts the plan.

## What Is Ready

- The service/agent ownership split is finally explicit: service owns the cross-machine data plane and lifecycle; agent owns desktop input, clipboard interaction, tray UI, and user-session concerns.
- The keyboard/input support matrix is realistic and no longer promises impossible Windows behavior such as SAS/Ctrl+Alt+Del injection.
- Mouse capture is now a real decision: `WM_INPUT` + `RIDEV_INPUTSINK`, 1x1 cursor pinning, relative deltas, remote-side ballistics.
- Install semantics are corrected: `dotnet tool install` installs the REPL; `mkp service install` performs service/firewall work.
- First-test rows now exist for TEST-MKP-001..008 with red/green expectations and commands.
- Clipboard file drop is deferred in the plan and removed from the smoke test.

## Blocking Findings

### B1. The locked proto is still invalid and incomplete

**Evidence**: `docs/PLAN-MKP-003.md` lines 247-368.

The plan says the proto block is "locked" and "intended-to-be-compilable" (line 245), but it is not:

- `SessionFrame` references `SendInputRequest`, `ClipboardSyncRequest`, and `ControlMessage` (lines 289-291). The block defines `InputBatch`, `ClipboardPush`, and `ControlMsg` instead (lines 297, 322, 339).
- The service references `SetMousePositionRequest`, `LocateProcessRequest`, `LocateProcessResponse`, `SetFocusByHwndRequest`, and `InjectInputRequest` (lines 261-264), but none of those messages are defined in the locked block.
- `OpenSessionRequest` is defined (line 267) but unused by `OpenSession`, which accepts `stream SessionFrame`.
- Line 365 is plain English inside the `proto` code fence, so `protoc` would fail immediately.
- The block says "Full simple messages like SetMouse* are identical to prior version" instead of defining the contract in this plan.

**Why this blocks go**: Network and test implementation would have to invent the contract. That is exactly what the planning gate is supposed to prevent.

**Required fix**: Replace the proto with a self-contained compilable block. No "same as prior version" text inside the fence. Add one validation line showing `protoc` or `buf` succeeded against the extracted proto.

### B2. Requirements source of record still contradicts the plan

**Evidence**:

- `docs/Project/Functional-Requirements.md` still says remote hotkey is bare `F2`, "Proxy all keyboard input", pairing uses `UPnP`, service is carried by the REPL install, firewall elevation uses `pwsh 5.1`, and tray actions invoke REPL.
- `docs/Project/TR-per-FR-Mapping.md` maps only FR-MKP-003, FR-MKP-005, and FR-MKP-006. PLAN-MKP-003 claims explicit mappings for all six FRs at lines 411-419.
- `docs/Project/Technical-Requirements.md` still says `mTLS or token` and includes file clipboard support, while the plan has selected mTLS and deferred file drop.

**Why this blocks go**: The plan claims MCP/requirements traceability as evidence, but the exported documents preserve older requirements that directly conflict with the reviewed design. An implementation agent following the exported requirements would build the wrong thing.

**Required fix**: Update the MCP requirements store, regenerate `docs/Project/*`, and ensure the exported FR/TR/TEST text matches PLAN-MKP-003. The mapping file must cover FR-MKP-001..006.

### B3. TLS ownership text still contradicts itself

**Evidence**: Lines 131-132 correctly say the service owns mTLS termination and the TLS server private key. Line 140 says "Pairing/TLS material and user settings live in the user-session agent only" and that the service holds only non-secret state. Line 152 says the service only receives thumbprint and non-secret config.

**Why this blocks go**: This is the security boundary. Service-terminated mTLS requires service-readable private key material. The table at line 146 agrees with service ownership; the surrounding prose does not.

**Required fix**: Reword the identity section to match the selected model: agent owns user secrets and pairing UX; service owns machine TLS private key and mTLS termination; service stores peer thumbprints and required machine-scope secret material under locked ACLs.

## Major Findings

### M1. The plan still contains stale file/path and PowerShell wording

- Critical files line 96 says `docs/PLAN-MKP-002.md (this working plan)`.
- Line 483 says this plan path is `docs/PLAN-MKP-002.md`.
- Lines 188 and 479 still say `pwsh 5.1`; Windows PowerShell 5.1 is `powershell.exe`, while `pwsh` is PowerShell 7+.

These are easy fixes, but they are exactly the kind of stale text that caused earlier plan drift.

### M2. Wireframes are specified but not present

Lines 455-463 now restore the wireframe specs, which is good. However, `docs/wireframes/` is still missing while the plan says wireframes block tray tests. This does not block architecture, but it does block tray UI implementation and should be an Inception task before tray test work starts.

### M3. User-approved deviation from "all keyboard input" is still not explicit

The plan correctly excludes SAS, secure desktop, lock/login screens, and UIPI-blocked scenarios. Because this changes the original "proxy all keyboard input" wording, approval should explicitly acknowledge the exclusion list.

## Final Verdict

**NO-GO for Construction.**  
**NO-GO for final Byrd gate approval as written.**

The reason is narrow but decisive: the network contract and requirements source of record are not safe handoff artifacts yet. A less expensive implementation model would have to reconcile undefined proto messages, prose inside a proto fence, stale FR text, partial mappings, and contradictory TLS ownership language before writing code.

**GO criteria**:

1. Proto block compiles as pasted.
2. MCP/exported requirements match PLAN-MKP-003, including support-matrix exclusions and all six FR mappings.
3. TLS identity prose matches service-terminated mTLS.
4. Stale path/PowerShell wording is cleaned up.

After those edits, I would approve a **GO for Elaboration**, starting with proto validation, data-plane/mTLS prototype, IPC boundary tests, and the first red unit tests. Construction should still wait until Elaboration gates are green.
