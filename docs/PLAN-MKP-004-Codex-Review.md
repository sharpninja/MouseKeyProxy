# Codex Final Review: PLAN-MKP-004

**Reviewed plan**: `docs/PLAN-MKP-004.md`  
**Review date**: 2026-07-03  
**Reviewer**: Codex  
**Final go/no-go**: **NO-GO for Byrd gate approval as written. GO for one narrow cleanup pass.**

PLAN-MKP-004 is very close. The actual source proto now exists at `src/MouseKeyProxy.Network/mousekeyproxy.proto`, and generated C# files exist under `gen/`, which is real progress. The architectural decisions are also largely locked. However, the plan artifact still fails a final handoff review because it contains several mechanical contradictions that directly affect Byrd traceability and the locked proto section.

## What Passed

- Service/agent ownership is coherent enough for Elaboration: service owns the cross-machine gRPC/mTLS data plane; agent owns desktop input, clipboard interaction, tray, and pairing UX.
- Input limitations are realistic and user-visible.
- Mouse capture/injection is locked: `WM_INPUT` + `RIDEV_INPUTSINK`, 1x1 pinning, relative deltas, remote ballistics.
- First-test rows now exist for TEST-MKP-001..008 with red/green criteria and commands.
- The actual proto file is self-contained enough to have produced `gen/Mousekeyproxy.cs` and `gen/MousekeyproxyGrpc.cs`.
- Requirements exports are materially improved: FR-MKP-001/003/006 and TR-MKP-CLIP/SEC now reflect the newer decisions.

## Blocking Findings

### B1. PLAN-MKP-004 still identifies itself as PLAN-MKP-003

**Evidence**: Line 1 is `# PLAN-MKP-003...`, while the file path is `docs/PLAN-MKP-004.md`.

This is small, but it is a final-plan identity error. The plan series has already had drift from copied text; the active plan title must match the active file.

**Required fix**: Change the H1 to `# PLAN-MKP-004: ...`.

### B2. The embedded proto fence is still malformed in the plan

**Evidence**: The plan opens a proto fence at line 248 and never closes it. The validation paragraphs, requirements section, and rest of the document are still inside the code fence in Markdown. Lines 411-413 are prose inside the `proto` fence.

The actual `.proto` file on disk may be valid, but the plan says the embedded block is the locked, intended-to-be-compilable contract. As embedded, it is not a valid Markdown/proto artifact.

**Required fix**: Add the closing triple backticks immediately after the last proto brace at line 409. Keep validation evidence outside the fence.

### B3. Requirements mapping export still does not match the plan claim

**Evidence**: `docs/Project/TR-per-FR-Mapping.md` maps only:

- FR-MKP-003
- FR-MKP-005
- FR-MKP-006

PLAN-MKP-004 lines 456-462 claim explicit mappings for all six FRs. The plan can include inline mappings, but the generated source-of-record mapping artifact still does not match that claim.

**Required fix**: Regenerate or repair the MCP mapping export so it includes FR-MKP-001..006.

## Major Findings

### M1. Stale `pwsh 5.1` remains

Line 523 still says `Firewall elevation via pwsh 5.1 in REPL`. The plan correctly uses `powershell.exe` elsewhere. Fix this last occurrence.

### M2. Requirements matrix export is still only existence-level

`docs/requirements-export.yaml` is a wrapper around a matrix response, not a full content export. The richer `docs/Project/*` files exist and are more useful. If the matrix file remains, do not describe it as the full requirements export; describe `docs/Project/*` as the content export and `docs/requirements-export.yaml` as the ID matrix response.

### M3. The actual proto still contains an unused `OpenSessionRequest`

`src/MouseKeyProxy.Network/mousekeyproxy.proto` still defines `OpenSessionRequest`, though the user summary says it was removed. This is not a compile blocker, but it is another claim/body mismatch. Either use it in the stream envelope or delete it from the actual proto and the embedded plan block.

## Final Verdict

**NO-GO for final Byrd gate approval as written.**  
**NO-GO for Construction.**  
**GO for one narrow cleanup pass.**

After these exact fixes, I would approve **GO for Elaboration**:

1. Fix H1 to PLAN-MKP-004.
2. Close the proto code fence after the actual proto block.
3. Regenerate mapping export so all six FRs are mapped.
4. Replace the remaining `pwsh 5.1` text.
5. Either remove or use `OpenSessionRequest`.

This is no longer an architecture problem. It is final artifact hygiene and traceability consistency. Once those are fixed, the plan is ready to hand to an implementation agent for Elaboration, with Construction still gated on green Elaboration tests and prototypes.
