# Peer Review: PLAN-MKP-002 (MouseKeyProxy) - Round 3

**Reviewer**: Claude (Fable 5, Claude Code session, max effort review)
**Review date**: 2026-07-02 (evening; plan file dated 2026-07-03)
**Artifact reviewed**: `docs/PLAN-MKP-002.md` (working-tree version of 2026-07-02 20:42; repo still has zero commits)
**Prior rounds**: Round 1 and Round 2 in `docs/PLAN-MKP-001-Claude-Review.md`; Codex round in `docs/PLAN-MKP-001-Codex-Review.md`. This is Round 3 of the plan series and the first review of the PLAN-MKP-002 file.
**Verdict**: **REVISE: one focused polish pass. No architectural blockers remain.** 2 blockers (both mechanical-to-fix), 3 majors, 9 minors. The design decisions requested across three reviews have now been made and made correctly; what remains is that the document claims several fixes its body does not actually contain, the proto has a compile-breaking duplicate and is missing the stream RPC its own topology decision requires, and one identity contradiction slipped into the new TLS material rows. Every remaining item is an edit, not a design effort. I expect Round 4 to be an approval if the conditions in Section 6 land.

---

## 1. Repo state verification

- `PLAN-MKP-001.md` correctly tombstoned ("Superseded... forked to docs/PLAN-MKP-002.md"); diff confirms 002 is the fork with only header/path deltas. The two-active-plans risk from the fork is handled. Good.
- `global.json`: `rollForward` now `latestFeature`. R2-MIN-4 **resolved** (verified on disk, not just claimed).
- `LICENSE (MIT)` now in Critical Files (plan line 88). R2-MIN-5 **resolved** (file itself not yet created; fine, it is a file-list plan item).
- `docs/requirements-matrix.yaml`: present; MCP output listing FR-MKP-001..006, TR-MKP-{ARCH,CLIP,INPUT,RELI,REPL,SEC}-001, TEST-MKP-001..008 as Tracked. This satisfies **existence** evidence for the store. It does not enable a **content** diff (IDs and statuses only, no AC text), and see R3-MAJ-3 for two TRs the plan invents that the store does not show.
- `docs/requirements-export.md`: **contains a saved error payload**, not requirements: `schema_validation_failed ... 'markdown must be one of: yaml, wiki'`. The export attempt failed and the failure was committed as the evidence artifact. See R3-MAJ-3.
- `docs/PLAN-MKP-002-Release.md`: stub exists, but its ID collides with the working plan's number and the plan text now names the successor `PLAN-MKP-003-Release.md` while also pointing at the 002 stub. See R3-MIN-7.
- `docs/wireframes/`: still absent, and the wireframe specs are still not in the plan body despite the claim at line 46. Folded into R3-BLK-1.

## 2. Disposition of Round 2 conditions

1. **[R2-BLK-1] stale text / single voice**: PARTIAL. The duplicate "Recommended Approach" is gone (good), but new duplication artifacts appeared (duplicate section heading, duplicated failsafe paragraph, duplicate proto message) and the dangling "Wireframes and risks largely unchanged" reference survives at line 452-453 while neither the wireframe specs nor the risk register exist anywhere in the document. See R3-BLK-1, R3-MIN-4.
2. **[R2-BLK-2] identity table**: RESOLVED with one contradiction. The table exists (lines 131-144), the agent-owns-user-secrets decision is right, and clipboard/settings/pairing rows are coherent. The TLS server cert row contradicts both the decision sentence above it and the ownership table. See R3-MAJ-1.
3. **[R2-BLK-3] test artifacts**: PARTIAL. Test projects (4, lines 82-85), framework (xUnit v3), mocking library (NSubstitute, never Moq), seam inventory (lines 102-111), and CI/physical split are all present and well done. The red/green one-liners for TEST-002..008 and the inline TR/TEST mappings for FR-002..006 are still absent despite being claimed. See R3-BLK-1.
4. **[R2-MAJ-1] proto/topology**: PARTIAL. Topology decided (remote dials host, one inbound rule, line 235: the Round 1 recommendation, adopted). HwndNode, SetFocusByHwndRequest, typed wheel/xbutton/text, Control/Heartbeat/Ack/VersionHello messages all added. But the stream RPC itself is missing from the service definition and `ClipboardFormat` is defined twice. See R3-BLK-2.
5. **[R2-MAJ-2] chords/modifiers**: RESOLVED (lines 161, 165, 220): chord detection in the hook state machine, emergency chord hook-detected, Ctrl-Alt-F2 disambiguated, modifier key-ups on every toggle transition. Residual: three spots still say bare "F2" (R3-MIN-1).
6. **[R2-MAJ-3] mouse capture**: MOSTLY RESOLVED (WM_INPUT preferred, 1x1 pinning, stable displayId, PerMonitorV2 TR). Two decision forks remain, one explicitly deferred "to code". See R3-MAJ-2.
7. **[R2-MAJ-4] clipboard**: RESOLVED (privacy formats with test, file-drop deferred, receive-time + seq ordering chosen, json chosen, opt-out + clear-history). Residual: "Max 50 or size cap" fork (R3-MAJ-2) and the smoke test still copies a file (R3-MIN-2).
8. **[R2-MAJ-5] security decisions**: RESOLVED (mTLS + pinned thumbprints, no bearer path, bind policy, UPnP IGD excluded, LocateProcess restrictions enumerated, ProgramData ACLs with uninstall verification). Residual: one stale "long-lived secret (or mTLS cert pair)" sentence directly contradicting the next line (R3-MIN-3).
9. **[R2-MAJ-6] latency/soak**: RESOLVED (p95 < 25 ms TR, coalescing policy, tray < 300 ms, 24 h soak gate with evidence list, Elaboration measurement prototype with named contingency). Residual: the new TRs carry no IDs in the store (R3-MAJ-3).
10. **Minors**: R2-MIN-1 (edge-hooks AC) fixed at line 390. R2-MIN-2 (watcher wording) fixed at line 218. R2-MIN-3 (pwsh terminology) fixed in Context but a stale "pwsh 5.1 helper" survives at line 178. R2-MIN-6 (version rule) still generic. R2-MIN-7 (successor plan ID) now self-conflicting. R2-MIN-8 (user sign-off on the support-matrix deviation) still implicit.

Scorecard: of the 10 Round 2 conditions, 6 fully landed, 4 partially. Nothing regressed architecturally; the misses are all document-integrity and completion misses.

## 3. Blockers

### R3-BLK-1: The Processing Summary claims fixes the document body does not contain. Under Byrd, this is a false ledger.

**Location**: lines 33-46 (claims) versus lines 116-120, 383-428, 452-453 (body).

This is the same failure mode Round 2 flagged as R2-BLK-1 (revision by assertion rather than by edit), now in a subtler form: the summary says the work happened, and the body still contains the Round 2 state. Specific claim-versus-content mismatches:

1. **Line 37 claims "red/green for additional TESTs"**. The body says "Similar one-liners for 002-008 below" (line 118) and the First-Test Table still reads "TEST-MKP-003,005,006,007,008: per TR above. Red states documented in test code" (line 426), verbatim from the previous plan. The one-liners do not exist anywhere. Test code does not exist either (correctly, per the hold), so the red states currently live nowhere. Per the workspace planning standard and Codex B3, expected red state belongs in the plan before the code exists. Write the seven missing lines in the format TEST-MKP-001 already uses: name, owning project, red assertion, green assertion, filter command.
2. **Line 37 claims "full inline mappings"**. Only FR-MKP-001 has a mapping line (line 391). FR-002 through FR-006 still have none. Five lines to write, using the FR-001 format.
3. **Line 46 claims "Wireframes spec re-homed here"**. No wireframe specification exists in this document (the four-SVG list with contents from the original plan was deleted in the Round 2 rewrite and never restored), `docs/wireframes/` does not exist, and line 452 still says "Wireframes and risks largely unchanged", pointing at content that is not there.
4. **The risk register is still missing entirely.** The original plan had a Risks/Tradeoffs section; since the Round 2 rewrite the plan has had a section heading that mentions Risks and no risk list. The register matters more now, not less: it should carry the live risks the reviews surfaced (latency budget over 4 hops with the agent-hosted-data-plane contingency, hook starvation/silent removal, DPAPI/identity edges, IPC failure semantics).
5. **Line 38 claims "OpenSession bidi" was added to the proto.** It was not; see R3-BLK-2.

**Required fix**: Make the body match the claims (items 1-5), or amend the claims to say "deferred". Given each item is minutes of writing, do the former. Then re-read the Processing Summary line by line against the body before resubmitting; Round 4 will verify each claim mechanically.

### R3-BLK-2: The proto contradicts the topology decision and does not compile.

**Location**: lines 235 (decision), 243-250 (service), 351-354 and 375-378 (duplicate).

1. **`ClipboardFormat` is defined twice** (lines 351-354 and again at 375-378). protoc rejects duplicate message definitions in a package; this block fails to compile as written. Delete the second copy (the one with the stale comment).
2. **The bidi stream carrying all high-rate traffic has no RPC.** Line 235 decides: remotes dial the host with a persistent bidirectional stream; input batches, clipboard pushes, and heartbeats multiplex over it. The service definition still lists only six unary RPCs. `ControlMessage`, `Heartbeat`, and `Ack` messages exist with nothing to carry them. Add the stream RPC and its envelope, e.g.:

   ```proto
   service MouseKeyProxy {
     rpc OpenSession (stream SessionFrame) returns (stream SessionFrame);
     // unary management RPCs unchanged below
     ...
   }
   message SessionFrame {
     uint64 seq = 1;
     oneof frame {
       SendInputRequest input = 2;
       ClipboardSyncRequest clipboard = 3;
       ControlMessage control = 4;
       Heartbeat heartbeat = 5;
       Ack ack = 6;
     }
   }
   ```

   and state which unary RPCs remain unary (per line 235, the infrequent management calls) versus which duplicate into the stream. As proposed in Round 2, add a `protoc`/`buf lint` dry run of this block to the verification gates so this class of defect cannot recur.
3. **Pairing still has no protocol surface.** The pairing flow (line 188) exchanges public info and confirms codes, but there are no pairing messages in the proto and no statement that pairing runs out-of-band of gRPC (and if out-of-band: over what, in what format, on which port relative to the discovery transport?). One paragraph plus two or three messages (PairingOffer, PairingConfirm) or an explicit "pairing rides the UDP discovery channel, format X" decision closes this.

## 4. Majors

### R3-MAJ-1: TLS material ownership contradicts the identity decision, the table, and the process ownership section simultaneously.

**Location**: lines 124, 132, 138, 188-189.

- Line 132 (decision): "Pairing/TLS material and user settings live in the user-session agent only."
- Line 124: the **service** owns "networking/gRPC server+client": the service terminates mTLS, so the service must read the TLS private key.
- Line 138 (cert row): key in `ProgramData\MouseKeyProxy\certs\host.pfx`, "ACL: service read" - contradicting line 132 - with owner "LocalSystem **or** user (ACL'd)" and scope "None (machine store preferred) **or** LocalMachine + entropy": two or-forks in the one row that exists to remove or-forks.

**Required fix**: One consistent story. Recommended: the machine identity certificate (with private key) lives in the LocalMachine certificate store, private key ACL'd to the service account; the agent drives pairing UX (code display/confirmation) and hands the service the peer's pinned thumbprint over local IPC; user-scope DPAPI artifacts remain exactly the settings and clipboard rows. Then reword line 132 to "user settings and clipboard history live in the agent (CurrentUser DPAPI); machine TLS identity lives in the LocalMachine store ACL'd to the service; the agent holds no TLS private keys" and collapse the cert row to a single choice. Also delete the "per-pair long-lived secret (or mTLS cert pair)" sentence at line 188: mTLS was chosen at line 189, so the per-pair secret is dead text (R3-MIN-3 overlaps).

### R3-MAJ-2: Three decision forks remain, one explicitly deferred to code.

**Location**: lines 168, 170, 207.

1. Delta source: "WM_INPUT with RIDEV_INPUTSINK (preferred...) **or** MSLLHOOKSTRUCT deltas" (line 168). "Preferred" is not a decision. Pick WM_INPUT; keep the hook-delta approach out of the plan or name it explicitly as the fallback with the trigger condition that would invoke it.
2. Injection model: "raw deltas... **Or** absolute normalized... Decision documented in code + test" (line 170). "Decision documented in code" is the exact opposite of a decision-complete plan; an implementer cannot start TEST-MKP design for mouse fidelity without knowing which behavior is asserted. Pick one (recommend raw deltas, remote applies its own ballistics, matching the "remote feel" the plan already leans toward) and put the sentence in the plan, not the code.
3. History bound: "Max 50 **or** size cap" (line 207). The clipboard section elsewhere says max 50 plus per-item size caps; make line 207 say both explicitly with the numbers (50 entries; per-item cap N MB; total-store cap M MB) so the LIFO test has constants to assert.

### R3-MAJ-3: Traceability drift between the plan and the requirements store, and one evidence artifact is a saved failure.

**Location**: lines 172, 227, 417-421; `docs/requirements-export.md`; `docs/requirements-matrix.yaml`.

1. The plan introduces **TR-MKP-INPUT-002** (PerMonitorV2, line 172) and an unnamed **latency TR** (line 227). The attached matrix shows exactly six TRs, and neither of these is among them. Either the store was updated after the export (re-export) or the TRs exist only in prose (create them, with IDs; the latency TR needs a name, e.g. TR-MKP-PERF-001). Every requirement the plan cites must exist in the store under the cited ID; that is the entire point of the matrix.
2. **`requirements-export.md` is a saved error payload** (`schema_validation_failed`: the generateDocument call passed `markdown`, which must be `yaml` or `wiki`). Re-run the export with a valid format and replace the file; as it stands, the plan's content-level evidence is a failure artifact. Delete or overwrite; do not leave a file named "export" whose content is an error.
3. The matrix rows are existence-only (ID, Tracked, source file). For Round 4 sign-off attach or export the content documents it references (Functional-Requirements.md, Technical-Requirements.md, Testing-Requirements.md) so the stored AC text can be diffed against the plan. Note also the matrix envelope carries `deprecated: true` from the generating method; if the REPL is signaling a deprecated API, note the replacement so future exports do not silently break.

## 5. Minors

- **R3-MIN-1**: Bare "F2" survives in three places that outrank the fix: Context line 13 ("Ctrl-Alt-F1 local / F2 remote"), FR-MKP-001 line 386 ("remote F2"), and the smoke test line 443 ("Hotkey (F1/F2)"). The decision at line 161 is Ctrl-Alt-F2; propagate it, especially into the FR of record.
- **R3-MIN-2**: Smoke step 2 (line 444) copies "text/image/file on A", but file drop was explicitly deferred (line 204). Drop "file" from the smoke or mark it as the deferred-FR trigger.
- **R3-MIN-3**: Line 178 still says "elevated pwsh 5.1 helper" (terminology fixed in Context line 18 but not here); line 188's "long-lived secret (or mTLS cert pair)" contradicts line 189's mTLS decision (overlaps R3-MAJ-1). Both are one-line deletions/edits.
- **R3-MIN-4**: Editorial duplicates: the "Failsafes and Reliability Policy" heading appears twice back-to-back (lines 214-216, the first one empty), and the emergency-hotkey failsafe paragraph is pasted verbatim at both line 165 and line 174. Keep one of each.
- **R3-MIN-5**: `InputKind` has MOUSE_WHEEL but no MOUSE_HWHEEL; horizontal wheel (tilt) events need either a second enum value or a documented flags convention on MOUSE_WHEEL. One line either way.
- **R3-MIN-6**: Version negotiation rule still unstated (line 381 remains generic; `VERSION_MISMATCH` exists as an error code, and `VersionHello` exists as a message). One sentence: exact major match required; on mismatch reject with VERSION_MISMATCH naming both versions.
- **R3-MIN-7**: Release-plan numbering is now self-colliding: the working plan is PLAN-MKP-**002**, the stub on disk is PLAN-MKP-**002**-Release.md, and line 46 names the successor PLAN-MKP-**003**-Release.md while also pointing at the 002 stub. Rename the stub to PLAN-MKP-003-Release.md, update its header and line 46, and keep plan numbers unique across the series.
- **R3-MIN-8**: The support-matrix deviation from the original "proxy ALL keyboard input" requirement (Context line 15) is still presented as if it were the original ask. Carried from R2-MIN-8: when the user approves this plan, the approval should explicitly cover the exclusion list; one sentence in Context ("deviation from original wording approved at plan sign-off") closes it.
- **R3-MIN-9**: Line 124 "runs as Windows service, Session 0 **or appropriate**" is vague where the rest of the section is precise; the service runs in session 0, full stop, and the sentence should say so (its precision is what keeps input code out of it).

## 6. Conditions for approval (Round 4)

1. **[R3-BLK-1]** Body matches claims: red/green one-liners for TEST-MKP-002..008; inline mappings for FR-002..006; wireframe specs restored in-plan (or as files under docs/wireframes/ referenced by name); risk register restored with the live risks; every Processing Summary claim true against the body.
2. **[R3-BLK-2]** Proto compiles (duplicate ClipboardFormat removed), OpenSession stream + SessionFrame envelope added with the unary-vs-stream routing stated, pairing surface defined (in-proto or explicit out-of-band design), and a protoc/buf dry run added to the verification gates.
3. **[R3-MAJ-1]** TLS material story made consistent (one owner per artifact, no or-forks in the cert row, line 132 reworded, line 188 leftover deleted).
4. **[R3-MAJ-2]** The three forks decided in plan text (delta source, injection model, history bounds with numbers).
5. **[R3-MAJ-3]** Store and plan reconciled (TR-MKP-INPUT-002 and the named latency TR created and visible in a regenerated matrix; requirements-export.md replaced with a successful export in yaml or wiki format).
6. Minors R3-MIN-1..7 and R3-MIN-9 fixed (each is a one-to-three line edit); R3-MIN-8 handled at sign-off.

Round 4 scope: verify each Processing Summary claim against the body, protoc the proto block, diff the regenerated matrix against the plan's cited IDs, and spot-check the six fixed minors. If Section 6 lands as described, Round 4 is an approval and Elaboration can start at the measurement prototype (line 232), which remains the right first slice.

## 7. What this round settled (credit where due)

The hard part is done, and it is worth saying so plainly:

1. **Topology**: remote-dials-host with a single inbound firewall rule, adopted with its reasoning stated. This was the largest open protocol decision across all three rounds.
2. **Identity**: the agent-owns-user-secrets model with a per-artifact table (one contradiction to iron out, but the model is right and the DPAPI mismatch that blocked Rounds 1 and 2 is genuinely dissolved).
3. **Test architecture**: four test projects, xUnit v3, NSubstitute, a real seam inventory, and a CI-versus-two-machine split. This is what Byrd compliance looks like structurally; it was absent for two rounds.
4. **Security**: mTLS with pinned thumbprints and no fallback path, bind policy, UPnP IGD excluded, LocateProcess constrained to enumerable limits, ProgramData ACL hardening with uninstall verification.
5. **Input correctness**: chord detection moved into the hook state machine (including the emergency chord), modifier key-ups on every toggle, Ctrl-Alt-F2 disambiguation, support matrix with observable-failure semantics.
6. **Reliability has numbers**: 25 ms p95, 5 s disconnect deadline, 300 ms tray feedback, 24 h soak, and an Elaboration measurement prototype with a named contingency (agent-hosted data plane) if the 4-hop budget fails.
7. **Evidence discipline started**: the MCP matrix export exists and shows the 20 tracked IDs; the remaining evidence work is fixing one failed export and adding two TRs.

The residue is bookkeeping: write the seven test lines and five mapping lines the summary already claims, fix the proto block, and reconcile three contradictory sentences. One sitting.

---

*Review produced under the Byrd Development Process review gate defined in PLAN-MKP-002 ("Construction remains on hold pending final peer review / Byrd gate"). No implementation artifacts were created or modified; this document is the sole output.*
