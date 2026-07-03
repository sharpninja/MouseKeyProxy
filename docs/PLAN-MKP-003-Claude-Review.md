# Peer Review: PLAN-MKP-003 (MouseKeyProxy) - Round 4, FINAL

**Reviewer**: Claude (Fable 5, Claude Code session, max effort review)
**Review date**: 2026-07-02 (evening; plan file dated 2026-07-03)
**Artifact reviewed**: `docs/PLAN-MKP-003.md` (working tree, 2026-07-02 20:56) plus evidence artifacts: `docs/requirements-export.yaml`, `docs/requirements-traceability.yaml`, `docs/Project/*` (5 content exports), `docs/PLAN-MKP-002-Codex-Review.md`
**Prior rounds**: Rounds 1-2 in `PLAN-MKP-001-Claude-Review.md`, Round 3 in `PLAN-MKP-002-Claude-Review.md`, Codex rounds in `PLAN-MKP-001-Codex-Review.md` and `PLAN-MKP-002-Codex-Review.md`

---

## FINAL DECISION: GO (conditional)

**GO** to exit Inception and begin Elaboration, conditional on the four-item mechanical fix list in Section 4 being completed with machine evidence attached (protoc output, regenerated store exports, a grep pass). No fifth human review round is required: every remaining defect is enumerated below and machine-verifiable, so evidence replaces re-review.

**NO-GO remains in force for Construction**, exactly as the plan itself states: Construction stays behind the Byrd gate (all Elaboration mocks green, 100% suite pass) regardless of this approval. Additionally, no network-layer Elaboration code (transport mocks aside) until the protoc gate in condition C1 is green, and no test authoring from the requirements store until condition C3 lands, because under Byrd the store is what tests are derived from and it currently contradicts the plan.

### Why GO

1. **Every architectural decision is made, correct, and mutually consistent at the design level.** Across four rounds the open design questions have converged to answers this review endorses without reservation: user-session agent owns all desktop input with the service as mTLS-terminating data plane and watchdog; identity split that dissolves the DPAPI mismatch; remote-dials-host bidirectional stream with one inbound firewall rule; chord detection inside the hook state machine with modifier key-ups on every toggle; WM_INPUT raw deltas, relative injection, remote ballistics, PerMonitorV2; clipboard model with privacy-format exclusion, loop prevention, receive-time ordering, and hard caps (50 entries, 10 MB item, 100 MB total); mTLS with pinned thumbprints and no fallback path; explicit `mkp service install` contract with ACL-hardened payload and rollback; support matrix with observable-failure semantics; failsafe policy with numbers; 25 ms p95 latency TR with an Elaboration measurement prototype and a named contingency; 24 h soak gate.
2. **Both review tracks converge.** The Codex Round 3 review of PLAN-MKP-002 and my Round 3 found the same four blockers; PLAN-MKP-003 resolves Codex conditions 2, 4, 5, and 6 outright, and its conditions 1 (proto) and 3 (verifiable traceability content) are exactly my C1 and C3 below. There is no disputed finding anywhere in the series.
3. **The evidence discipline asked for since Round 1 was delivered.** `docs/Project/` now contains full content exports from the MCP store (Functional, Technical, Testing requirements, TR-per-FR mapping, matrix), regenerated cleanly (`requirements-export.yaml` is a success payload; the Round 3 failure mode is fixed). Reviewers can finally diff stored requirements against the plan, which is precisely how the remaining condition C3 was found.
4. **What remains is transcription, not design.** The proto fix is renames-and-pastes with a compiler to prove it; the TLS fix is deleting one stale sentence and tightening two table cells; the store fix is updating six records and re-exporting; the hygiene fix is a dozen one-liners. None of it requires a decision that is not already written elsewhere in the plan. A fifth review round would add nothing a compiler, a diff, and a grep cannot certify.

### Why not unconditional

1. **The proto still does not compile, and this is the third consecutive round in which the plan claims it does.** The claim-versus-body failure recurred on the same artifact flagged in Rounds 2 and 3 (detail in Section 3.2). The conditions below are therefore hard gates with required evidence, not advisories.
2. **The requirements store contradicts the plan on load-bearing decisions.** Stored FR-MKP-003 still mandates "proxy all keyboard input... exactly as if typed locally" (the infeasible wording both review tracks killed in Round 1/Codex B2); stored FR-MKP-001 still says bare "F2"; stored FR-MKP-006 still says UPnP, "REPL carries service", "pwsh 5.1", and "tray invokes REPL"; TR-MKP-SEC-001 still says "mTLS or token"; TR-MKP-CLIP-001 still includes file content in v1; the stored mapping document covers only FR-003/005/006 (three of six). Byrd's first principle is that requirements drive tests: tests written from today's store would encode the impossible and the reverted. This is the single most important condition (C3).
3. **One identity contradiction survived the pass that claimed to remove it** (the line-140 sentence, Section 3.3), and the plan misstates its own path twice, which matters in a workspace where subagents execute plans mechanically.

---

## 1. Verification record (what was checked, how)

- **Round 3 conditions**: each verified against the plan body line by line (Section 3).
- **Proto block**: manually parsed for definition/reference consistency (protoc is not on this machine's PATH; C1 requires running it as evidence). Findings in 3.2 are definitional, not stylistic.
- **Store content**: all five `docs/Project/` exports read in full and diffed conceptually against plan decisions (Section 3.5).
- **Export artifacts**: `requirements-export.yaml` confirmed as a fresh successful generation (new requestId, success payload); `requirements-traceability.yaml` confirmed as the generation record for the five content docs.
- **Codex Round 3 review**: read in full; convergence noted; its Win+R smoke-risk minor is carried into C4.
- **Repo state**: PLAN-MKP-001 tombstoned; PLAN-MKP-002 not yet tombstoned (it was modified after 003 was created and still calls itself the working plan; C4 item); stale error-payload `requirements-export.md` still on disk (C4 item).

## 2. Round 3 conditions scorecard

1. **R3-BLK-1 (body matches claims)**: LARGELY RESOLVED. The First-Test Table now has red/green/command for all eight TESTs (plan lines 421-429); inline mappings exist for all six FRs (384-419); wireframe specs restored (455-463); risk register restored with the new latency/acceleration risks (474-481). Residual: the proto claim is false again (3.2), and two self-identity lines still say PLAN-MKP-002 (3.6).
2. **R3-BLK-2 (proto compiles)**: NOT RESOLVED. Duplicate ClipboardFormat removed, OpenSession + SessionFrame + Pair added (good), topology text aligned (good), but the block has dangling type references and prose inside the code fence (3.2).
3. **R3-MAJ-1 (TLS consistency)**: MOSTLY RESOLVED. Ownership section locked correctly (service = data plane + TLS key; agent = pairing UX + user secrets; provisioning over IPC); identity-table cert row now names the service. Residual: one contradicting sentence and two table cells (3.3).
4. **R3-MAJ-2 (decision forks)**: RESOLVED for the three named forks (WM_INPUT locked, relative-delta injection locked, history bounds numbered). Residual legacy forks that predate Round 3 and should close now: IPC "named pipe or loopback gRPC" (lines 60, 133), tray "WinForms or minimal console" (line 58, contradicting the Context's WinForms decision), payload "ProgramData or user-chosen path" (line 191). C4.
5. **R3-MAJ-3 (traceability)**: PARTIALLY RESOLVED. Clean export regenerated; full content docs exported (major step). Residual: store content drift (3.5), latency TR still has no ID (line 235 "Latency TR (new)"), stale error-payload file still present, stored mapping doc missing FR-001/002/004.
6. **R3 minors**: F2 fixed everywhere in the plan (store lags, 3.5); smoke test file-copy fixed with deferral note; duplicate Failsafes heading fixed; "Session 0 or appropriate" fixed; proto moved to a real Network project (also closes a Codex minor). Unfixed: "pwsh 5.1" leftover at line 188, MOUSE_HWHEEL, the one-sentence version rule, release-plan numbering (now collides with the working plan's own number: 3.6), duplicated failsafe paragraph (lines 173/184), duplicate Date header (lines 7/9).

Score: of 6 Round 3 condition groups, 3 fully landed, 3 partially. The partials are all in C1-C4 below.

## 3. Findings detail

### 3.1 What is now right (and should not be touched again)

The ownership/IPC section (129-137), identity table rows for settings/clipboard/payload/logs/peers (142-150), support matrix (154-173), mouse strategy (175-184), deployment contract (186-194), pairing/authz section (196-210), clipboard model (212-222), failsafe policy (224-232), latency/soak section (234-240), topology decision paragraph (243), the full First-Test Table (421-429), the six FR mapping blocks, the restored wireframe specs and risk register. This is a decision-complete core.

### 3.2 Proto block: dangling references, missing messages, prose inside the fence (condition C1)

The block at lines 247-368 is declared "locked, intended-to-be-compilable" (245). It is not compilable:

1. **Three oneof fields reference undefined names**: `SessionFrame.frame` uses `SendInputRequest input`, `ClipboardSyncRequest clipboard`, `ControlMessage control` (289-291), but the block defines `InputBatch`, `ClipboardPush`, and `ControlMsg` instead (297, 322, 339). Renamed definitions, unrenamed references. Fix in one direction consistently (recommend pointing the oneof at `InputBatch`/`ClipboardPush`/`ControlMsg`, since batch/push are the right stream-frame shapes).
2. **Every unary request/response message is absent**: `SetMousePositionRequest`, `LocateProcessRequest`, `LocateProcessResponse`, `HwndNode`, `SetFocusByHwndRequest`, `InjectInputRequest` are referenced by the service (261-264) and defined nowhere. The parenthetical at line 365 ("Full simple messages like SetMouse* are identical to prior version") delegates the contract to a superseded, tombstoned document. HwndNode has now been dropped from the canonical proto twice (Rounds 2 and 4). Paste the definitions from PLAN-MKP-002's block; they were correct there.
3. **Markdown prose sits inside the code fence** (lines 365-367, including a bolded "Validation evidence" paragraph). protoc parses everything in the file; prose is a syntax error. Move it below the closing fence.
4. **Orphans**: `OpenSessionRequest` (267-271) is defined but no RPC or frame uses it. Either delete it or wire it: the natural home for session auth is the first SessionFrame (add it to the oneof) or channel metadata; say which.
5. Evidence required by C1: run `protoc --proto_path=. --csharp_out=<tmp> mousekeyproxy.proto` (or `buf lint`) on the extracted block and attach the output to the plan or docs/. The plan already mandates this gate at lines 245/367-370; it has not yet been executed against its own text.

### 3.3 TLS identity: one sentence and two cells still contradict the locked design (condition C2)

- Line 140 still reads "Pairing/TLS material and user settings live in the user-session agent only": the exact sentence Round 3 required rewording, verbatim, directly above a table whose row 146 correctly gives the TLS server private key to the service. Delete or reword to match lines 131-135 ("user settings and clipboard history live in the agent; machine TLS identity lives with the service; the agent holds no long-lived TLS private keys").
- Row 145 still grants the agent "pairing secrets / cert private key". If the agent's role is pairing confirmation plus provisioning (line 198), the durable artifact it keeps is at most the pairing-confirmation record and the peer thumbprint, not a cert private key. If a transient key exists during generation-then-provisioning, say "transient during pairing, not persisted".
- Row 146's scope cell mixes two mechanisms: a `.pfx` under ProgramData with ACLs, and "LocalMachine store". Pick one (recommend the Windows LocalMachine certificate store with a private-key ACL for the service account; it avoids file-handling code and gives auditability), and make the path cell match.
- Design note, not a condition: provisioning private key material over the local IPC pipe (line 198) is acceptable with the pipe ACL'd, but the simpler pattern is service-side self-generation at install with the agent only confirming thumbprints during pairing. Consider at Elaboration; either passes review.

### 3.4 Test/policy numeric conflict (condition C3/C4 overlap)

TEST-MKP-008 (plan line 429 and the stored TEST text) asserts ClipCursor release "within 2s" on crash/disconnect; the failsafe policy (line 227) sets the disconnect deadline at "default 5" seconds. Reconcile explicitly: recommend clip release hard deadline 2 s (safety) while reconnect give-up remains 5 s (policy), stated as two numbers with two names, in both the plan and the stored TEST record. Also: the stored TEST-MKP-008 says modifier cleanup "on reconnect", but the plan upgraded this to every toggle transition; the store must follow.

### 3.5 Requirements store drift (condition C3: the Byrd-critical one)

The content exports prove the store predates the last two rounds of decisions. Required store updates before any test is derived from it:

1. **FR-MKP-001**: "remote F2" -> "remote Ctrl-Alt-F2" (match plan line 15/169/375).
2. **FR-MKP-003**: replace "Proxy all keyboard input... exactly as if typed locally" with support-matrix wording plus explicit exclusions (SAS, secure desktop, UIPI) and observable-failure requirement. This is the record that, as stored, mandates the impossible.
3. **FR-MKP-006**: remove UPnP (plan excludes IGD/NAT mapping), "REPL carries service" (plan: explicit `mkp service install`), "firewall elevate via pwsh 5.1" (plan: Windows PowerShell 5.1, `powershell.exe`), and "Tray icon actions invoke REPL" (plan: shared command library, no per-click spawn).
4. **TR-MKP-CLIP-001**: drop "file" from v1 supported content (deferred) and add the numeric caps (50/10 MB/100 MB).
5. **TR-MKP-SEC-001**: "mTLS or token" -> mTLS with pinned thumbprints, locked.
6. **TEST-MKP-008**: per 3.4.
7. **Mappings**: stored TR-per-FR mapping covers only FR-003/005/006; add FR-001, FR-002, FR-004 rows to match the plan's inline mappings (lines 414-419).
8. **Latency TR**: create the ID the plan cites as "(new)" (suggest TR-MKP-PERF-001, or extend TR-MKP-RELI-001's text with the 25 ms p95 budget and cite that); the plan must reference a real stored ID.
9. Then regenerate `docs/Project/*` and `docs/requirements-export.yaml` and confirm the regenerated text matches the plan (that diff is the acceptance evidence). All updates via MCP interfaces only, per workspace rules.

### 3.6 Hygiene list (condition C4)

1. Lines 96 and 483 say this plan is `docs/PLAN-MKP-002.md`; both must say PLAN-MKP-003.md (the plan of record misidentifying itself is how a subagent edits the wrong file).
2. Duplicate `**Date**` header (lines 7/9): keep one.
3. Duplicated failsafe paragraph (lines 173 and 184): keep one.
4. Line 188 "elevated pwsh 5.1 helper" -> `powershell.exe` (5.1) per the fixed Context wording.
5. Delete `docs/requirements-export.md` (the Round 3 error payload superseded by the .yaml).
6. Tombstone `docs/PLAN-MKP-002.md` the way 001 was tombstoned (it still calls itself "the active working plan" and was modified after 003 was forked).
7. Release-plan numbering: the successor is named `PLAN-MKP-003-Release.md` (lines 53, 485) while the working plan is itself PLAN-MKP-003, recreating the collision Round 3 flagged one number higher; the legacy stub on disk is still `PLAN-MKP-002-Release.md`. Rename the stub to `PLAN-MKP-004-Release.md` (or `PLAN-MKP-REL-001`), update lines 53/485, and adopt the rule that release plans take the next free number.
8. Decide the two remaining legacy forks: local IPC = named pipe (recommended; no firewall interaction, session ACLs) at lines 60/133; tray stack = WinForms (the Context already says so) at line 58. Payload path: keep "ProgramData default, user-chosen override" but say the override must preserve the ACL requirements.
9. InputKind: add `MOUSE_HWHEEL` or document that horizontal wheel rides MOUSE_WHEEL with a flags bit.
10. Version rule, one sentence at line 243 or in VersionHello's comment: exact major match required; mismatch rejected with VERSION_MISMATCH naming both versions.
11. Codex carry-over: add a dedicated test for Win+R (and Win+L) swallow behavior in the InputMatrix category; high-risk shortcuts deserve named assertions.
12. Optional but recommended: record in the plan that FR-MKP-003's support-matrix wording is a user-approved deviation from the original "all input" ask, so the sign-off is explicit (Round 2 MIN-8, never actioned).

## 4. Conditions attached to the GO

- **C1 (proto)**: fix 3.2 items 1-4; attach protoc/buf success output. Gate: blocks network-layer Elaboration work only; all other Elaboration (seam interfaces, toggle state machine mocks, LIFO logic, measurement harness scaffolding) may start immediately.
- **C2 (TLS text)**: fix 3.3 (one sentence, two cells). Gate: blocks identity/pairing Elaboration mocks.
- **C3 (store sync)**: execute 3.5 via MCP, regenerate exports, attach regenerated files. Gate: blocks writing any test derived from the store (which under Byrd is all of them); this is therefore the true start line for test authoring.
- **C4 (hygiene)**: items 1-11 (12 optional). Gate: none individually, but complete before the next commit so the committed plan is the fixed plan.

Completion is certified by evidence, not re-review: protoc output (C1), the reworded lines (C2), regenerated `docs/Project/*` matching the plan (C3), and a grep pass showing zero hits for the stale strings ("pwsh 5.1", "PLAN-MKP-002.md (this", "or minimal console", "ControlMessage control") (C4). If any condition cannot be met honestly, the GO does not degrade gracefully: Elaboration in the affected area stops until it is met.

## 5. Process note for whoever executes the fixes

Three consecutive rounds asserted proto fixes that were not in the body. The pattern is consistent: summaries were written from the review's fix list rather than from the edited document. The corrective is mechanical and cheap: every claim in a Processing Summary must cite the evidence that proves it (a command output, a line number in the current file, a regenerated artifact). The C-list above is structured so that each item's completion produces its own evidence; use it as the template going forward.

## 6. Review series close-out

- Round 1 (PLAN-MKP-001): 5 blockers, 7 majors: architecture unimplementable as written (session 0), no security model, no test structure.
- Round 2 (PLAN-MKP-001 rev): 3 blockers, 6 majors: architecture fixed in intent, document integrity and identity decisions open.
- Round 3 (PLAN-MKP-002): 2 blockers, 3 majors: design converged, transcription and evidence gaps.
- Round 4 (PLAN-MKP-003, this review): 0 design findings; 4 mechanical condition groups. **GO (conditional) for Elaboration; Construction remains behind the Byrd gate.**

The product this plan describes is buildable, testable, and materially safer and more reliable than the tool it replaces, and the plan now says so in one voice. Finish the transcription, sync the store, and go build the measurement prototype.

---

## 7. Verification Addendum (2026-07-02, post-fix pass, PLAN-MKP-004)

The fix pass was applied as `docs/PLAN-MKP-004.md` (002 and 003 both correctly tombstoned). Conditions verified mechanically by this reviewer, not taken on report:

**C1 (proto): MET, independently verified.** `src/MouseKeyProxy.Network/mousekeyproxy.proto` exists and compiles: this reviewer ran `protoc` 2.81.1 (from the Grpc.Tools NuGet cache) with `--csharp_out`; exit 0, generated Mousekeyproxy.cs (235 KB). The fixer's own `gen/` artifacts are also present. SessionFrame's oneof now references the defined messages (InputBatch/ClipboardPush/ControlMsg); all management messages and HwndNode are defined in-block; no prose inside the proto source file. Residuals: (a) the plan's markdown code fence opened at line 248 never closes, so everything after it renders inside a code block, and the validation prose at lines 411-413 is technically inside the fence again: add the closing fence after the final `}`; (b) the src proto retains the orphan `OpenSessionRequest` that the plan block deleted: remove it from the file (or restore it to the plan) so text and artifact are identical.

**C2 (TLS identity): MET.** The line-140 contradiction is gone (decision text now reads correctly: agent holds no long-lived TLS private keys; service owns the machine key); the pairing row is "transient during pairing, not persisted". Residual: the cert row's path cell still says `%ProgramData%\MouseKeyProxy\certs\` while the decision text and scope cell say LocalMachine certificate store; the decision text governs, but align the cell.

**C3 (store sync): SUBSTANTIALLY MET; three items outstanding.** Regenerated `docs/Project/` exports confirm the store now matches the plan on every load-bearing record: FR-MKP-001 (Ctrl-Alt-F2), FR-MKP-003 (support matrix + observable failure), FR-MKP-006 (explicit install, powershell.exe, no UPnP, shared command lib), TR-MKP-CLIP-001 (v1 formats, caps 50/10 MB/100 MB, privacy formats), TR-MKP-SEC-001 (mTLS pinned, no fallback), TEST-MKP-008 (2 s clip release / 5 s reconnect / modifier key-ups on every toggle). Outstanding: (1) the stored TR-per-FR mapping still covers only FR-003/005/006; add FR-001/002/004 rows (the plan's inline mappings are the source); (2) TR-MKP-RELI-001 in the store does not contain the 25 ms p95 budget the plan (line 236) claims was added to it; (3) `docs/requirements-export.yaml` was not regenerated (pre-fix timestamp). These three gate **test authoring**, not Elaboration prototyping.

**C4 (hygiene): PARTIAL.** Done: self-path fixed (lines 97/527), duplicate Date removed, deployment-contract pwsh wording fixed, error-payload export deleted, stub renamed, tombstones correct, forks locked (WinForms; named pipe primary with explicit fallback; ProgramData-only payload). Not done: the H1 title on line 1 still reads "PLAN-MKP-003"; risk-register line 523 still says "pwsh 5.1"; the renamed release stub's content still titles itself PLAN-MKP-002-Release and links from PLAN-MKP-001; MOUSE_HWHEEL (item 9), the version-rule sentence (item 10), and the Win+R/Win+L swallow test (item 11) were not added.

**Decision: the conditional GO stands and is now substantially earned.** Elaboration may proceed immediately (including network mocks, since the contract compiles). Test authoring remains gated on the three C3 residuals. The nine residual edits above are the complete remaining punch list; no further review round is required for any of them.

### Punch-list close-out (2026-07-02, ~21:29, verified against receipts)

All nine items re-verified mechanically after the fix pass that produced `docs/receipts-004.txt` and `docs/proto-verify-004.txt`:

- CLOSED and independently confirmed: H1 title (PLAN-MKP-004), proto fence closed (line 412), orphan OpenSessionRequest removed from both plan block and src proto, MOUSE_HWHEEL in both, version-rule sentence present, TR-per-FR mapping export now 6/6 FRs and identical to the plan's inline mappings, `requirements-export.yaml` regenerated (21:25), release stub retitled to 004, risk-register pwsh wording fixed, Win+R/Win+L swallow assertion added to the plan's TEST-MKP-003 row. Independent protoc re-run on the updated proto (with HWHEEL and version-rule comment): exit 0.
- REMAINING (one cause, one fix): the three content exports `Functional-Requirements.md`, `Technical-Requirements.md`, `Testing-Requirements.md` under docs/Project/ still carry the 21:12 generation (only the mapping and matrix were regenerated afterward). Consequently the on-disk export of TR-MKP-RELI-001 does not yet show the 25 ms latency budget the getTr receipt reports, and the exported TEST-MKP-003 text does not yet show the Win+R/Win+L assertion. One `generateDocument` (all docs) call regenerates them; confirm those two strings appear, and the ledger is fully clean.

This close-out does not change the decision: GO for Elaboration remains in force; the export regeneration is bookkeeping that should accompany the first commit.

---

*Final review of the PLAN-MKP series review gate. No implementation artifacts were created or modified by the review itself; the verification protoc run wrote generated code to the reviewer scratchpad only.*
