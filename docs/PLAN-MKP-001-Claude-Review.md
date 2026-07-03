# Peer Review: PLAN-MKP-001 (MouseKeyProxy) - Round 2

**Reviewer**: Claude (Fable 5, Claude Code session, max effort review)
**Round 2 date**: 2026-07-02 (evening; plan file dated 2026-07-03)
**Artifact reviewed**: `docs/PLAN-MKP-001.md` as revised after `docs/PLAN-MKP-001-Codex-Review.md` was processed (working-tree version, repo still has zero commits)
**Prior round**: Round 1 (this file's original content) is archived unmodified at the bottom of this document, since nothing is committed yet and the history must not be lost.
**Verdict**: **REVISE AND RE-REVIEW. Materially improved, not yet approvable.** 3 blockers, 6 majors, 10 minors remain. The distance to approval is much shorter than Round 1: most of the remaining work is deleting stale contradictory text, converting "A or B" statements into decisions, and completing artifacts the revision started (proto, identity table, test inventory).

The Codex review (B1-B5, H1-H5, M1-M5) and my Round 1 review converged on substantially the same defect list, and the revision genuinely addressed most of it: process ownership and IPC boundary, input support matrix with observable-failure semantics, explicit `mkp service install` contract (payload out of the tools store), pairing/auth matrix with negative tests, clipboard data model with loop prevention, failsafe and reliability policy, v1 locked to two nodes, executable verification gates, and the source-control contradiction resolved (origin = GitHub, documented exception). That is real progress and the strengths from Round 1 all still stand.

What remains falls into three buckets: (1) the revision was applied by insertion rather than replacement, leaving the document self-contradictory; (2) two Round 1 blockers were answered with text that still defers or contradicts the decision (identity/DPAPI, test artifacts); (3) several majors from both reviews were not picked up at all (hook-vs-RegisterHotKey conflict, mouse capture strategy, clipboard privacy formats, either/or security choices).

---

## 1. Round 2 repo state verification

- Plan revised: confirmed (24.9 KB, status line claims all Codex blockers addressed).
- `docs/PLAN-MKP-001-Codex-Review.md`: present, read in full; my assessment of its findings: all valid, no disagreements. Where Codex and Round 1 overlap (B1/BLK-01, B2/MAJ-01, B3/BLK-04, B4/BLK-05.1, B5/BLK-05.2-5, H1/BLK-03, H2/MAJ-05, H3/MIN-01, H5, M1/MAJ-06, M3/MAJ-03, M4/MAJ-07), the revision's dispositions are evaluated below once, not twice.
- Still zero commits; `docs/todo.yaml` is still a 32-byte header; `mcp.db` still 0 bytes; `docs/wireframes/` still absent; no test project, no `tests/` directory (nothing built yet, which is correct per the hold, but see R2-BLK-3 for what the plan text must contain).
- Plan status line asserts "Traceability (FR/TR/TEST + mappings) populated via MCP" while line 370 says TODO IDs are "to be created via client.Todo.* once payload shape confirmed". These two claims conflict; see R2-BLK-3.

## 2. Disposition of Round 1 findings

- **BLK-01 (session 0 / component placement)**: LARGELY RESOLVED. Process Ownership and IPC Architecture section (plan lines 109-116) puts hooks, hotkeys, clipboard, ClipCursor, SendInput, focus ops in the user-session agent; service keeps networking, pairing state, persistence, watchdog; named-pipe/loopback IPC with auth, timeouts, and local-only fallback; startup model stated. Residuals: connection topology and hot-path latency are new findings R2-MAJ-1 and R2-MAJ-6; and the stale duplicate section contradicts all of this, R2-BLK-1.
- **BLK-02 (identity / DPAPI)**: NOT RESOLVED. The revision restates the problem and defers the decision. Now R2-BLK-2.
- **BLK-03 (proto gaps/shape)**: PARTIALLY RESOLVED. ClipboardSync RPC, protocolVersion/peerId/seq/correlationId fields, error codes, ackSeq, UNSPECIFIED=0, wheel/xbutton/text kinds all added. Residuals (undefined messages, missing pairing/heartbeat/hello surface, unary shape) in R2-MAJ-1.
- **BLK-04 (Byrd test artifacts)**: PARTIALLY RESOLVED. 6 TRs and 8 TESTs claimed in MCP, first-test table exists for TEST-MKP-001 with red/green/command, gate commands listed. Residuals (no test project in file list, no framework choice, red states deferred to nonexistent test code, mappings inline for FR-001 only, no repo-visible evidence) in R2-BLK-3.
- **BLK-05 (security)**: PARTIALLY RESOLVED. Payload moved out of the global tools store with rollback; pairing code flow, auth matrix, revocation, negative tests, TLS 1.3 all specified. Residuals (either/or crypto decisions, ProgramData ACLs, bind policy, UPnP, LocateProcess limits) in R2-MAJ-5; clipboard privacy formats in R2-MAJ-4.
- **MAJ-01 (input support matrix)**: RESOLVED. Matrix with supported/conditional/excluded classes and observable-failure ACs (plan lines 118-135). See R2-MIN-8 for the sign-off note.
- **MAJ-02 (hotkey mechanics)**: NOT RESOLVED. Now R2-MAJ-2.
- **MAJ-03 (failsafe)**: MOSTLY RESOLVED. Emergency hotkey with hardcoded fallback, disconnect timeout (default 5 s), local-only mode, bounded queues, watchdog. Residuals: stuck-modifier trigger (R2-MAJ-2), ClipCursor release wording (R2-MIN-2).
- **MAJ-04 (mouse capture strategy)**: NOT RESOLVED. Now R2-MAJ-3.
- **MAJ-05 (clipboard algorithm)**: PARTIALLY RESOLVED. Entry schema, format list, dedupe hash, LIFO merge with move-to-top, loop prevention, caps, per-format privacy toggle. Residuals in R2-MAJ-4.
- **MAJ-06 (tray-REPL channel)**: RESOLVED. Shared command implementation library, no per-click spawn (plan lines 20, 145). Timeout numbers still absent; folded into R2-MAJ-6.
- **MAJ-07 (resilience/observability)**: PARTIALLY RESOLVED. Reconnect/backoff, heartbeats, sequence and ack windows, backpressure policy, lost-key-up tests. Residuals: no latency numbers, no soak gate, observability still thin; R2-MAJ-6.
- **MIN-01 (source control)**: RESOLVED. Plan line 31 documents origin = GitHub as an explicit user-directed exception; matches actual remote.
- **MIN-02 ("pwsh 5.1")**: NOT RESOLVED (plan lines 18, 139). Now R2-MIN-3.
- **MIN-03 (global.json rollForward)**: NOT RESOLVED (file unchanged). Now R2-MIN-4.
- **MIN-04 (.gitignore binary globs)**: OPEN, unchanged; carry forward as-is (accepted if deliberate).
- **MIN-05 (LICENSE)**: NOT RESOLVED. Now R2-MIN-5.
- **MIN-06 (proto polish)**: PARTIALLY RESOLVED (csharp_namespace, error codes added). HwndNode field naming is moot because HwndNode is now undefined entirely; R2-MAJ-1.
- **MIN-07 (evidence for MCP claims)**: NOT RESOLVED; escalated into R2-BLK-3 because the plan now leans on the MCP store as its traceability system of record.
- **MIN-08 (FR-006 bundling, AC phrasing)**: PARTIALLY RESOLVED. FR-006 ACs now reference concrete commands; process assertion removed. Leftover "Test with edge hooks disabled" phrase persists; R2-MIN-1.
- **MIN-09 (wireframes)**: OPEN. The revision made this slightly worse by removing the wireframe specs from the document body; folded into R2-BLK-1.
- **MIN-10 (lifecycle)**: PARTIALLY RESOLVED. Rollback on partial install and recorded version for updates added. Version-mismatch behavior claimed ("compatibility rules") but not specified; R2-MIN-6 note.
- **MIN-11 (estimates)**: Unaddressed; still optional; dropped from conditions.

## 3. Round 2 blockers

### R2-BLK-1: The revision was inserted, not applied. The document now contradicts itself and lost content it still references.

**Location**: plan lines 47-68 versus 70-89; line 355-364.

There are two "Recommended Approach" sections. The first (lines 47-68) is the revised one. The second (lines 70-89) is the stale Round 1 text and directly contradicts the revision and the Codex fixes it claims to incorporate:

- Line 73 puts the full P/Invoke list back without the tray/agent-only qualifier, and line 78 restores the "Hybrid: service for background + WinForms tray... Tray menus/forms call into REPL to send gRPC commands" model that lines 20, 111-116, and 145 replaced (shared command library, ownership table).
- Line 79 restores "Service registration: REPL tool handles" ambiguity that the deployment contract (lines 137-145) explicitly killed with `mkp service install`.
- Line 80 restores the "ClipCursor to screen bounds" no-op phrasing.
- The reuse list appears twice (lines 63-68 and 84-89).

A plan whose stated purpose is decision-completeness cannot contain two competing architectures: an implementation agent that lands in the stale section will build the wrong thing while quoting the plan as authority. Additionally, the consolidation section (line 355, "Wireframes and risks largely unchanged") refers to wireframe specs, a risk list, and phase detail that no longer exist anywhere in the document: they were deleted in the rewrite, so "unchanged" content is now dangling references.

**Required fix**: Delete lines 70-89 entirely. Restore (or explicitly re-home with paths) the wireframe specifications, the risk register, and the implementation-phase detail that line 355 claims are still present. Re-read the final document end to end for single-voice consistency before resubmitting; this failure mode (append-instead-of-replace) is exactly what a re-review will grep for.

### R2-BLK-2: Identity and DPAPI decisions are still deferred, and the ownership table reintroduces the contradiction (Round 1 BLK-02, Codex H2 residual).

**Location**: plan lines 56, 111, 149, 165-168.

The ownership table assigns "settings persistence (non-UI)" and the "pairing state machine + key negotiation" to the **service** (line 111). But settings live in `%LOCALAPPDATA%\MouseKeyProxy\settings.json` (line 56) and pairing secrets "Persist under DPAPI per user" (line 149). A LocalSystem (or any service-account) process resolves `%LOCALAPPDATA%` to its own profile, not the user's, and cannot decrypt CurrentUser-scope DPAPI blobs belonging to the desktop user. This is the same wall Round 1 flagged, now embedded in the new architecture section. Line 168 says it out loud: "Service uses LocalMachine or DPAPI with care; document the choice and tests" - that is a deferred decision inside a plan whose status line claims decision-completeness.

**Required fix**: Add the identity table Round 1 asked for: one row per persisted artifact (settings, pairing secrets, TLS certs/keys, clipboard history, logs, service payload) with owning process, Windows account, absolute path pattern, DPAPI scope, and the test that proves round-trip under that identity. Then make the pairing design consistent with it. Two coherent options:

1. Pairing secrets are user-scope: then key negotiation and secret storage move to the agent, and the service holds only non-secret connection state (peer address, port, thumbprint). The service can hold the pinned peer certificate thumbprint (not secret) and do TLS with a machine cert whose private key lives in the machine store with an ACL for the service account.
2. Pairing secrets are service-side: then they live under ProgramData with machine-scope DPAPI plus secondary entropy, with the ACL rationale written down (machine-scope DPAPI is decryptable by any local process; entropy plus directory ACL is the mitigation), and the agent never needs them.

Pick one. "CurrentUser for tray data" (line 168) is right for clipboard history; the unsettled half is pairing/TLS material and settings, which is exactly the half the service owns.

### R2-BLK-3: Byrd gate artifacts remain unverifiable and structurally incomplete (Round 1 BLK-04, Codex B3 residual).

**Location**: plan lines 91-107, 286-331, 370; repo state.

Improvements acknowledged (TR/TEST inventory, first-test table for TEST-MKP-001, gate commands). What still fails the workspace planning standard:

1. **No test project exists in the Critical Files list.** The ownership-aware file list (lines 91-107) names four `src/` projects and zero `tests/` projects. Under a tests-first process the test projects are the first files created; they must be enumerated (e.g. `tests/MouseKeyProxy.Agent.Tests/`, `tests/MouseKeyProxy.Service.Tests/`, `tests/MouseKeyProxy.Common.Tests/`, plus a two-machine integration harness location), with the framework named (the `--filter "Category=..."` syntax implies xUnit traits or NUnit categories; say which, and name the mocking library).
2. **Red states are deferred to test code that does not exist.** Line 329: "Red states documented in test code." The planning standard (and Codex B3) require expected red state in the plan/requirements before the code exists. TEST-MKP-001 does this correctly (line 327); TEST-MKP-002 through 008 need the same one-line treatment: name, owning project, expected red failure, green criterion, command.
3. **Inline traceability covers FR-001 only.** FR-MKP-001 lists its TRs and TESTs (line 294); FR-002 through FR-006 have no inline mapping lines. The plan says full mappings live in MCP, which leads to:
4. **The MCP claims are still not evidenced, and the plan contradicts itself about them.** Status line 3: traceability "populated via MCP". Line 370: TODO IDs "to be created via client.Todo.* once payload shape confirmed". Both cannot be true. `docs/todo.yaml` remains a bare header. Round 1 MIN-07 asked for an attached export or query transcript; now that the plan's entire traceability story is "see MCP", that attachment is a blocker, not a nicety: a reviewer must be able to diff the stored FR/TR/TEST set against the plan text. Export the requirements document (the wrap-up flow produces one) to `docs/` or paste query output into an appendix.

**Required fix**: All four items above, in plan text or attached artifact. This is mechanical work; nothing here needs design.

## 4. Round 2 majors

### R2-MAJ-1: The revised proto does not compile and the control-plane surface is still missing (Round 1 BLK-03 residual, Codex H1 residual).

**Location**: plan lines 180-284.

- `LocateProcessResponse` references `HwndNode` (line 231), which is **not defined anywhere** in the revised block; the old definition was replaced wholesale. `SetFocusByHwndRequest` is absent entirely; the service lists the RPC (line 192) with no request message. The `// ... similar for others` placeholder (line 221) is the tell: a proto that says "similar" is a sketch, and the plan elsewhere (line 133) promises "See updated proto below" as the definitive event contract. Complete every message; run `protoc` on the block as part of plan validation (the verification gates could include a `buf lint`/`protoc` dry run cheaply).
- **Pairing, hello/version negotiation, and heartbeat have no protocol surface.** Lines 148-151 and 284 promise discovery, pairing exchange, version negotiation on connect, and heartbeats; the service definition contains none of them (no Pair/Hello/Heartbeat RPCs or message types). Either they ride a separate protocol (say which: the UDP discovery payload format is also undefined) or they are RPCs; both need message definitions before Elaboration mocks can exist.
- **Weakly typed event payloads**: wheel delta, xbutton identity, and unicode text are all shoved into `bytes extraData` (line 243). Give them typed fields (oneof or per-kind fields); `bytes` here just moves the contract into undocumented C# code.
- **Shape**: unary RPCs plus app-level seq/ack (lines 151, 177) can be made correct, but a single bidirectional stream per pair still buys ordering for free, halves the firewall problem (only the listening side needs an inbound rule), and gives you connection liveness as a side effect. The plan should either adopt it or record the counter-decision with its reasoning ("both nodes listen; two inbound rules; unary + ack window because X"). What it cannot do is stay silent on **who listens and who dials**: that decision drives firewall rule count, `mkp service install` behavior on each node, and the pairing flow's directionality. Nothing in the plan currently states the connection topology.

### R2-MAJ-2: The hotkey path still relies on RegisterHotKey, which goes deaf exactly when the product is in remote mode (Round 1 MAJ-02, untouched).

**Location**: plan lines 51, 73, 112, 289-294, 327.

When remote mode is active the agent's LL keyboard hook swallows events before the system's hotkey processing runs, so a RegisterHotKey registration will not fire for the toggle-back chord; the toggle-back and the emergency-release chords must be recognized inside the hook's own state machine (RegisterHotKey is at most a convenience for the idle/local state). The failsafe section's "emergency local hotkey" (line 135, 171) inherits the same requirement: if it is registered via RegisterHotKey it will not work during the exact failure it exists for. TEST-MKP-001's green criterion should assert chord detection through the hook path with swallowing active, not only via RegisterHotKey.

Also still open from Round 1: "remote F2" remains ambiguous (bare F2 versus Ctrl-Alt-F2; bare F2 collides with Rename everywhere; state the full chord in FR-MKP-001), and stuck-modifier release fires only "on reconnect or explicit reset" (line 174), when the classic failure is mid-chord toggling: synthesize modifier key-ups on **every** toggle transition, both directions, and test it (the lost-key-up simulation at line 175 is the right harness; add the toggle case).

### R2-MAJ-3: Mouse capture and injection strategy is still unspecified (Round 1 MAJ-04, untouched).

**Location**: plan lines 51, 59, 80, 212-219.

Still undefined: (a) the relative-motion source while moves are swallowed (raw input WM_INPUT with RIDEV_INPUTSINK, recommended, versus deriving deltas from MSLLHOOKSTRUCT.pt against a pinned cursor); (b) the cursor-pinning mechanic while remote-active (the stale "ClipCursor to screen bounds" no-op is still the only ClipCursor semantics stated, line 80); (c) relative versus absolute-normalized injection and the double-acceleration question; (d) PerMonitorV2 DPI awareness as an explicit TR for the agent (without it every coordinate API is virtualized on mixed-DPI setups and `SetMousePosition` will be wrong); (e) `displayIndex int32` (line 215) is still unstable across reboots/hotplug; address displays by device name or a pinned stable mapping, and define the coordinate space (per-monitor logical vs physical). These decisions gate TEST design for FR-001/002/005; they cannot wait for Construction.

### R2-MAJ-4: Clipboard residuals: privacy formats ignored, file-drop semantics undecided, ordering rule still an "or" (Round 1 MAJ-05/BLK-05.4 residual, Codex H2 residual).

**Location**: plan lines 159-168.

- **Password-manager exclusion formats are still unhandled.** Clips stamped `ExcludeClipboardContentFromMonitorProcessing` or `CF_CLIPBOARD_VIEWER_IGNORE` must be neither synced nor persisted, with a test. Both reviews raised this; the data model has no mention. Given the plan persists clipboard history to disk, this is the difference between a convenience feature and a credential leak.
- **CF_HDROP "paths or content" (line 161) is not a decision.** Paths are meaningless on the peer machine; content means a file-transfer subsystem (size negotiation, progress, partial failure, target directory policy). Recommend: v1 = text (CF_UNICODETEXT), HTML, and image (DIB/PNG) only; file drop deferred to its own FR with its own tests. Also "clipboard-history.json (or db)" and "last-writer or merge by timestamp + id" (lines 165, 167) are each two designs; pick one each. For ordering, restate Round 1's warning: do not trust peer wall clocks; order by local receive time with sequence tiebreakers, and write down the concurrent-copy rule the tests will assert.

### R2-MAJ-5: Security section still contains either/or forks and unhardened defaults (Round 1 BLK-05 residual, Codex B5/M2 residual).

**Location**: plan lines 142, 147-157.

- "Either mTLS (preferred for machines) or server TLS + HMAC/Bearer" and "per-pair long-lived secret (or mTLS cert pair)" (lines 149-150): pick one. Recommendation: mTLS with per-machine self-signed certs generated at install, exchanged and thumbprint-pinned during the pairing-code confirmation, private keys in the store appropriate to the owning process from R2-BLK-2's identity table. Then "revocation = forget thumbprint" falls out naturally and the bearer/HMAC machinery disappears.
- **ProgramData payload needs an explicit ACL statement** (line 142). Default ProgramData ACLs let standard users create files; a payload directory not locked to admin-write is the same LPE Round 1 flagged, one directory over. `mkp service install` must create the directory with admin-only write ACLs and the plan must say so (and the uninstall test must verify no orphaned world-writable dir remains).
- **Bind policy still unstated**: which interfaces does each gRPC listener bind, is localhost-only an option for the IPC-over-loopback variant, and are the firewall rules scoped to the peer's address? One sentence each; they gate the negative tests already promised.
- **UPnP is still in** (line 148, "optional UPnP/SSDP"). SSDP-style LAN discovery is fine; UPnP IGD port mapping (WAN exposure of an input-injection service) should be explicitly out of scope. The current wording conflates the two; separate them and drop the port-mapping half.
- **LocateProcess "restricted, returns limited view" (line 154) is not defined.** Enumerate the restriction (e.g. only processes owned by the interactive user, only windows on the current virtual desktop, depth cap N, no window text beyond title) so the AC is testable.

### R2-MAJ-6: The hot path now crosses four hops with no latency budget, and reliability still has no soak gate.

**Location**: plan lines 52-53, 111-114, 341-353.

The ownership split routes every input event: host agent -> local IPC -> host service -> gRPC/TLS -> remote service -> local IPC -> remote agent -> SendInput. That is a defensible architecture, but at mouse-move rates (125-1000 Hz) it needs numbers before Construction: an end-to-end latency TR (suggest: p95 under 25 ms LAN for injected events, measured host-keydown to remote-SendInput), a per-hop measurement harness named in Elaboration (this is precisely the "risk prototype" Codex's approval recommendation asked for: measure IPC+gRPC+IPC round trip with mock endpoints before betting the design on it), a mouse-move coalescing policy (coalesce moves, never coalesce buttons/keys), and tray action feedback timeout numbers (line 145 promises "timeouts + error UI defined in tray" but defines neither). If the budget fails in Elaboration, the fallback (agent-hosted data plane, service keeps control plane only) should be recorded now as the contingency so it is a pivot, not a redesign. Finally, given MWB flakiness is this project's founding grievance, add the soak gate Round 1 asked for: a long-run stability criterion (e.g. 24 h continuous pairing with zero stuck-input incidents and zero unreleased-clip events) in the Transition verification list.

## 5. Round 2 minors

- **R2-MIN-1**: FR-MKP-001 AC still says "Test with edge hooks disabled" (line 293), a leftover from MWB framing; this product has no edge hooks. Restate as "assert no pointer transition occurs at screen edges while inactive/active".
- **R2-MIN-2**: "guaranteed ClipCursor release (use SetWindowsHook or finalizer + separate watcher if needed)" (line 172): finalizers are not a guarantee mechanism (not run on rude termination), and SetWindowsHook is unrelated to clip release. The honest mechanisms are the watchdog/watcher process plus the OS resetting clip on desktop switch; word it that way so the test asserts the watcher, not a finalizer.
- **R2-MIN-3**: "elevate pwsh 5.1" persists (lines 18, 139). `pwsh` is PowerShell 7+; Windows PowerShell 5.1 is `powershell.exe`. Name the exact binary and the rationale (5.1 ships in-box), and define UAC-decline behavior (fail command cleanly, no partial firewall state).
- **R2-MIN-4**: `global.json` still has `rollForward: latestMajor`, which permits .NET 11+ SDKs and defeats the net10 pin. Change to `latestFeature`.
- **R2-MIN-5**: LICENSE still missing from Critical Files; required before the repo is public (MIT fits the stated goal).
- **R2-MIN-6**: "Full contract includes ... compatibility rules. Version negotiation on connect" (line 284) names no rule. One sentence: exact-match on major version, reject with errorCode VERSION_MISMATCH and human-readable message naming both versions.
- **R2-MIN-7**: The successor plan for marketing/media ("separate Transition/Release plan", lines 25, 356) has no ID or path. Name it now (e.g. `docs/PLAN-MKP-002-Release.md`) so the deferral is trackable per the planning standard.
- **R2-MIN-8**: The Context section (line 15) now restates the user's original "proxy ALL keyboard input" requirement as the support matrix. The carve-outs (SAS, secure desktop, UIPI) are technically forced and correctly designed, but they modify a stated core requirement: when the user approves this plan revision, that approval should explicitly acknowledge the exclusion list, and the plan should mark the matrix as a user-approved deviation rather than silently rewriting the requirement's history.
- **R2-MIN-9**: `.gitignore` still globally ignores `*.exe`, `*.dll`, `*.pdb` (Round 1 MIN-04): fine if deliberate, but note that `bin/`/`obj/`/`artifacts/` already cover build output, so the globs only affect intentionally vendored binaries.
- **R2-MIN-10**: Round 1's appendix topology (remote-dials-host, single inbound rule) was not adopted and not counter-argued. Fine either way, but per R2-MAJ-1 the chosen topology must be stated; if both-listen is the choice, the appendix diagram in the archived Round 1 review below should not be treated as the design of record.

## 6. Conditions for approval (Round 2)

1. **[R2-BLK-1]** Stale duplicate sections deleted; wireframes/risks/phases content restored or re-homed; document reads single-voice end to end.
2. **[R2-BLK-2]** Identity table added (artifact, process, account, path, DPAPI scope, round-trip test); pairing-secret and settings ownership made consistent with it; no remaining "document the choice later" language.
3. **[R2-BLK-3]** Test projects in Critical Files with framework and mocking library named; red/green/command lines for TEST-MKP-002..008; inline TR/TEST mappings for FR-002..006; MCP requirements export or query transcript attached to `docs/`; the status-line/line-370 TODO contradiction resolved.
4. **[R2-MAJ-1]** Proto completed (HwndNode, SetFocusByHwndRequest, no "similar for others"), pairing/hello/heartbeat surface defined, typed event payloads, connection topology stated (who listens, who dials, rule count), streaming adopted or counter-decision recorded.
5. **[R2-MAJ-2]** Chord detection specified in the hook state machine (including the emergency chord); remote chord disambiguated; modifier release on every toggle with a named test.
6. **[R2-MAJ-3]** Mouse capture/injection decisions written (delta source, pinning, relative vs absolute, PerMonitorV2 TR, display addressing).
7. **[R2-MAJ-4]** Clipboard privacy formats honored with a test; file-drop deferred or fully specified; storage and ordering "or"s decided.
8. **[R2-MAJ-5]** mTLS-vs-token decided (one mechanism); ProgramData ACL hardening stated; bind policy stated; UPnP port mapping excluded; LocateProcess restrictions enumerated.
9. **[R2-MAJ-6]** Latency TR with numbers plus Elaboration measurement prototype; coalescing policy; tray timeout numbers; soak gate in Transition.
10. Minors R2-MIN-1..8 fixed in the same pass (each is a one-to-three line edit); R2-MIN-9/10 at author's discretion.

Round 3 re-review scope: the diff of the plan only, plus the attached MCP evidence, plus one grep pass for resurrected stale text. If conditions 1-9 land, I expect Round 3 to be an approval.

## 7. Note on review convergence

Codex's twelve-point revision checklist and Round 1's conditions are the same list in different clothes; the revision satisfied roughly eight of twelve. No finding in the Codex review is disputed here. The two reviews diverge on exactly one recommendation: Codex accepted unary RPCs with app-level sequencing; Round 1 recommended a remote-dials-host bidirectional stream. Both are buildable; R2-MAJ-1 only requires the plan to pick one and state the topology consequences. Everything else in this round is either mechanical completion (R2-BLK-1/3), a decision the plan already knows it owes (R2-BLK-2, the "or"s), or a Round 1 major that the revision skipped (R2-MAJ-2/3/4).

---
---

# ARCHIVED: Round 1 Review (2026-07-02, pre-Codex-revision plan)

The following is the Round 1 review of the original PLAN-MKP-001.md, preserved verbatim because the repository has no commits and this file is the only record. Line numbers herein refer to the ORIGINAL plan text, not the current revision. Dispositions are tracked in Section 2 above.

---

# Peer Review: PLAN-MKP-001 (MouseKeyProxy)

**Reviewer**: Claude (Fable 5, Claude Code session, max effort review)
**Review date**: 2026-07-02
**Artifact reviewed**: `docs/PLAN-MKP-001.md` (version present in working tree on 2026-07-02, no commits yet in repo)
**Requested by**: Payton Byrd
**Verdict**: **REVISE AND RE-REVIEW. Not approved to exit Inception or begin Construction.**

The plan is a solid skeleton with the right instincts: the risk list names the two hardest problems (session 0 isolation, DPAPI identity), the hotkey-only scope deliberately kills the edge-crossing complexity that makes MWB fragile, and the reuse targets are proven. However, five blocker-level findings invalidate the component layout, the protocol definition, and the security posture as written. These are plan-level decisions, not Elaboration discoveries: resolving them changes the Critical Files list, the protobuf contract, and the process topology, so they must be fixed in the plan text before any Byrd phase work proceeds.

Finding counts: 5 Blockers, 7 Majors, 11 Minors.

---

## 1. Repo state verification (claims audited)

The plan asserts several completed setup items. Verified against the working tree:

- `git init` done: **confirmed** (`.git` present, branch `master`, zero commits).
- GitHub remote added: **confirmed**, but as `origin` (`https://github.com/sharpninja/MouseKeyProxy.git`). See finding MIN-01: this contradicts the plan's own source-control note (line 32) and the global source-control rule.
- `director add-workspace` done: **corroborated** (`AGENTS-README-FIRST.yaml` present, 27 KB; `.grok/` present; `docs/todo.yaml` present).
- `global.json` (net10 SDK) present: **confirmed**, but see finding MIN-03 (`rollForward: latestMajor` undermines the pin).
- `.gitignore` present: **confirmed**, see finding MIN-04.
- "Requirements + protos surfaced" via MCP requirements store: **not verifiable from the repository**. `mcp.db` in the workspace root is 0 bytes. See finding MIN-07: the plan should attach evidence (a requirements export or REPL query transcript) so reviewers can audit the FR/AC/TEST set actually stored.

---

## 2. Blocker findings

### BLK-01: Session 0 isolation contradicts the component placement. The input stack cannot live in the Windows service.

**Location**: Recommended Approach (lines 35-46), Critical Files (lines 60-64), Risks (line 209).

The plan places `HotkeyService`, `InputHookService`, `SendInputProxy`, and `ClipboardManager` as hosted services inside `src/MouseKeyProxy/Program.cs` running under `UseWindowsService`. A Windows service runs in session 0 on a non-interactive window station. From there:

- `SetWindowsHookEx` with WH_KEYBOARD_LL / WH_MOUSE_LL will not observe the interactive user's input.
- `SendInput` injects into the service's own session, not the user's desktop.
- `RegisterHotKey` and `ClipCursor` operate on the wrong desktop.
- `AddClipboardFormatListener` requires a window on the interactive desktop; the service has none, and the clipboard is per-session anyway.

The risk list acknowledges "Service context (session 0) limits for input/clipboard/UI -> hybrid tray required," but the mitigation as designed does not match: the tray is specified as a UI shell that "invokes REPL for gRPC sends" (line 20, 42), not as the process that owns hooks and injection. As written, every core FR (001, 002, 003, 004) is unimplementable.

**Required change**: Add an explicit process topology section that assigns every desktop-interactive responsibility (hooks, injection, hotkeys, ClipCursor, clipboard listener/setter, tray UI) to a user-session agent process. Then decide the service's actual role. Two viable shapes:

1. **MWB pattern (matches the stated service requirement)**: LocalSystem service is a thin supervisor only. It launches/restarts the user-session agent via `WTSGetActiveConsoleSessionId` + `WTSQueryUserToken` + `CreateProcessAsUser`, and monitors it. All input, clipboard, and gRPC traffic lives in the agent.
2. **No service**: user-session agent auto-starts at logon (Run key or scheduled task). Simpler, avoids BLK-02 and BLK-05 entirely, but deviates from the stated "register/run as Windows service" requirement, so it needs an explicit user decision.

Either way the plan must state which process hosts the gRPC endpoints (it must be the user-session agent, or the service must relay to the agent over local IPC, which adds a hop and failure mode for no benefit). Also state what the service buys you: input across the logon/secure desktop is not achievable with this design regardless (see MAJ-01), so "starts before logon" delivers little for an input proxy.

### BLK-02: Identity mismatch: %LOCALAPPDATA% and user-scope DPAPI do not work across the service/user boundary.

**Location**: Context (lines 16-17), Recommended Approach (lines 39-41), Risks (line 211).

`%LOCALAPPDATA%` for LocalSystem resolves to `C:\Windows\System32\config\systemprofile\AppData\Local`, not the user's profile. `ProtectedData` with CurrentUser scope in a LocalSystem process encrypts under SYSTEM's master key: the tray/user session can never decrypt it, and vice versa. As written, the service and the tray would read different settings files and mutually unreadable secrets. The risk list names "DPAPI under LocalSystem" but offers no decision.

**Required change**: The plan must assign an identity to every persisted artifact. The clean resolution follows from BLK-01: if all clipboard, secret, and settings consumers live in the user-session agent, then user-profile `%LOCALAPPDATA%` plus DPAPI CurrentUser is correct and the problem disappears. If the supervisor service keeps any config of its own, put it in `%ProgramData%\MouseKeyProxy` with explicit ACLs and note that DPAPI LocalMachine scope is readable by any process on the machine (add secondary entropy, and never store pairing secrets there).

### BLK-03: The protobuf contract is missing half the product and has the wrong shape for the input path.

**Location**: Protobuf Definitions (lines 77-104), Recommended Approach (lines 38, 45).

Gaps against the plan's own FRs:

1. **No clipboard RPCs at all.** FR-MKP-004 (real-time sync, LIFO merge, history) is a core FR with zero protocol surface. Needed: clipboard-changed push, history sync/merge exchange, and a clear-history operation.
2. **No pairing/auth/handshake RPCs.** FR-MKP-006 requires REPL pairing and key negotiation, but the protocol defines no hello, no version negotiation, no pairing exchange, no session auth.
3. **No control channel.** Toggle coordination ("you are now active"), heartbeat/health, and modifier-state resync on toggle have no messages. FR-MKP-001 cannot be implemented host-side only; both ends must agree on active state.
4. **Unary `SendInput` is the wrong shape for the event path.** Mouse movement arrives at 125-1000 Hz. Independent unary calls give no cross-call ordering guarantee (sequencing exists only within one HTTP/2 stream), add per-call overhead, and, critically, under the host-central model (remotes register **to** the host, line 21/45) the host cannot initiate unary calls to a remote that has no listening server without every remote also running an inbound-reachable server plus firewall rule.
5. **`InputKind` is incomplete**: no MOUSE_WHEEL / MOUSE_HWHEEL, no XBUTTON, no unicode-text kind for the tray "Inject Text" feature (which wants KEYEVENTF_UNICODE semantics, i.e. a string payload, not per-VK events).
6. **No sequence numbers** despite "Reliable sequenced events" (line 38), and `InputEvent.time` has no defined clock source or epoch.

**Required change**: Redesign around one persistent bidirectional stream per remote, dialed **from the remote to the host**. All input events, clipboard pushes, control messages, and heartbeats multiplex over it with per-stream sequence numbers. This has a large operational payoff the plan should claim explicitly: only the host needs an inbound firewall rule; remotes are outbound-only, which shrinks the elevation/firewall surface FR-MKP-006 has to manage. Keep the unary management RPCs (SetMousePosition, LocateProcess, SetFocusByHwnd, InjectInput) for the REPL/tray plane, but they also need an answer for "host calls remote" (either route them over the same bidi stream as typed control messages, or accept servers on both ends and say so).

### BLK-04: Byrd Development Process compliance is asserted but not designed. Tests are structurally absent from the plan.

**Location**: Requirements section (lines 106-151), Byrd/RUP Breakdown (lines 153-166), Critical Files (lines 55-75).

Concrete non-compliances:

1. **The Critical Files list contains no test project.** For a process whose first rule is "tests first," the absence of `tests/MouseKeyProxy.Tests/`, a framework choice (xUnit vs MSTest), and a mocking approach from the file inventory is disqualifying. Test projects are not an implementation detail; under Byrd they are the first code written.
2. **No mockability architecture.** The core logic is P/Invoke-heavy. Byrd's "validate with mocks" phase is impossible unless the plan commits to seams now: interfaces like `IInputSource` (hook events), `IInputInjector` (SendInput), `IHotkeyMonitor`, `IClipboardAccessor`, `ICursorClip`, `ISystemClock`, with the Win32 implementations as thin untested adapters and all logic (toggle state machine, LIFO merge, event routing, chord detection) behind the seams. Without this, "mocks for Win32 (fake hooks/SendInput)" (line 157) is a phrase, not a design.
3. **Traceability is missing.** Two TEST IDs exist (TEST-MKP-001, TEST-MKP-004) for six FRs. There is no FR-to-TEST matrix, and four of six FRs have no named test at all. Deferring the full matrix to Inception is acceptable, but then the plan must state the matrix as an Inception **exit criterion** with a defined shape (every FR maps to at least one TEST with concrete, automatable assertions; every TEST maps back to an FR or TR).
4. **Several implemented features have no FR at all**, so no tests can be derived for them: Mirror Mode (appears only in the tray spec and Construction iteration 3), Inject Text, pairing/key negotiation and its security properties, discovery, reconnection/resilience, failsafe behavior, logging/diagnostics, uninstall/cleanup. Under "requirements drive tests," unrequirement'd features are untested features.
5. **ACs are not objectively verifiable as written.** Examples: "work on target as local" (FR-MKP-003) has no observable; "Test with edge-detection hooks disabled" (FR-MKP-001) references an edge-detection feature this product does not have (apparent MWB residue); "Reqs queryable post reg" (FR-MKP-006) is a process/tooling assertion, not a product behavior, and should move out of product FRs.

**Required change**: Add a Test Architecture section (framework, seams, mock strategy, what runs in CI vs. what requires two physical machines), add test projects to Critical Files, split FR-MKP-006 (see MIN-08), add the missing FRs, rewrite ACs to be measurable, and make the traceability matrix an explicit Inception exit gate.

### BLK-05: Security model is absent, and one default is an actual privilege escalation.

**Location**: Context (lines 17-18, 22), Recommended Approach (lines 38-39, 43, 46), Risks (lines 208-214).

1. **User-writable service binaries running as LocalSystem.** The REPL "carries the service bits on `dotnet tool install`" (line 46), and global tools install under `%USERPROFILE%\.dotnet\tools`. Registering a LocalSystem service whose image path points into a user-writable directory means any process running as that user can replace the binary and own the machine at next service start. If the service survives BLK-01, its binaries must be copied to a protected location (Program Files or ProgramData with admin-only write ACLs) during the elevated install step. If the service is dropped (BLK-01 option 2), this finding dissolves.
2. **Pairing trust bootstrap is unspecified.** "UPnP/broadcast discovery + key negotiation/persist" names transports, not a trust model. Without a human verification step, first pairing is MITM-able on a hostile LAN. Specify: discovery advertises, pairing performs a key exchange bound to a short authentication code displayed on both machines that the operator visually compares (Bluetooth numeric-comparison style), then both sides persist peer identity (certificate thumbprint pinning) via DPAPI. Also clarify what UPnP is for; for two machines on one LAN, UDP broadcast/mDNS discovery is sufficient and UPnP port mapping is out of scope (and undesirable: do not invite WAN exposure of an input-injection service).
3. **The RPC surface is remote code execution by design.** InjectInput, SetFocusByHwnd, and LocateProcess allow a caller to enumerate windows and type into any of them. The plan needs an explicit statement: mTLS or pinned-cert channel required for every call, only paired peers authorized, bind address configurable (default: LAN interface only, never 0.0.0.0 without a decision), firewall rule scoped to the peer's address where possible, and replay resistance (TLS plus per-stream sequence numbers covers this if BLK-03's redesign lands).
4. **Clipboard privacy.** Password managers mark sensitive clips with the `ExcludeClipboardContentFromMonitorProcessing` / `CF_CLIPBOARD_VIEWER_IGNORE` conventions. The sync and the persisted history MUST respect these formats (do not sync, do not persist). Persisted clipboard history is a credential honeypot: make persistence opt-in or at minimum document it and provide `clear-history` in the REPL (with a test).
5. **TLS material story.** "TLS + REPL-negotiated secrets" does not say what the server certificate is. gRPC in .NET has no TLS-PSK; the realistic design is self-signed per-machine certs generated at install/pairing with mutual pinning. Say so.

**Required change**: Add a Threat Model / Security Design section covering the five points above, each with at least one derived TEST.

---

## 3. Major findings

### MAJ-01: "Proxy ALL keyboard input" is not achievable in the absolute; the plan must enumerate the exceptions.

**Location**: Context (line 15), FR-MKP-003 (lines 121-122).

Hard limits on Windows that no user-mode design escapes:

- **Ctrl+Alt+Del (SAS)** cannot be synthesized with SendInput. Injecting it requires the SAS library (`SendSAS`) plus the "software SAS" group policy, from a service context. The AC's "Ctrl+Alt+Del subset where allowed" hand-waves this; either specify the SendSAS mechanism as a scoped feature or (recommended for v1) exclude SAS explicitly.
- **Secure desktop (UAC prompts, logon screen)**: LL hooks do not run there and SendInput cannot reach it. The remote's elevation prompts are unreachable; document that the operator must have another path (or auto-toggle back, see MAJ-03).
- **UIPI**: injection into a foreground window of higher integrity (elevated apps) is silently discarded when the agent runs at medium IL. Options: document the limitation (recommended v1), run the agent elevated (bad UX), or uiAccess=true (requires code signing and Program Files install). Note the irony worth recording in the plan: MWB's "UseService" mode exists precisely to cross these boundaries, and it is the part of MWB that burned the user. Choosing the documented-limitation path is choosing reliability over coverage, which matches this project's reason to exist.
- **Win+L** is only partially suppressible from an LL hook; and games/anti-cheat using raw input may ignore or flag injected events (LLMHF_INJECTED is visible to them).

**Required change**: Rewrite FR-MKP-003 with an explicit exclusion list and per-exclusion behavior (pass-through locally? swallow? notify?), and add ACs for what happens when injection is blocked (it must fail observably, not silently).

### MAJ-02: Hotkey mechanics have an internal conflict: RegisterHotKey does not fire once the LL hook swallows input.

**Location**: Recommended Approach (lines 37, 44), FR-MKP-001 (lines 109-114).

When remote mode is active, the design swallows all keyboard events in the LL hook and forwards them. Hotkey processing happens after the LL hook in the input pipeline, so a swallowed chord never reaches RegisterHotKey: the toggle-back hotkey would be dead exactly when it is most needed. The chord detection for "return to local" must be implemented inside the hook's own state machine. Recommendation: use hook-based chord detection in both directions for consistency and reserve RegisterHotKey for nothing, or use it only in local-idle mode. Related items the plan must pin down:

- **Ambiguity**: "Ctrl-Alt-F1 local / F2 remote" reads as either Ctrl-Alt-F2 or bare F2 for the remote toggle. Bare F2 is untenable (it is Rename everywhere). State the full chord.
- **Stuck modifiers**: toggling mid-chord (Ctrl-Alt held) is the classic stuck-modifier bug in every product of this class. On toggle, synthesize key-ups for all currently-down modifiers on the side losing focus, and resync modifier state on the side gaining it. This needs a control message (see BLK-03) and a dedicated TEST.
- **Multi-remote hotkey UX is undefined and inconsistent with scope.** The architecture is host-central multi-remote (lines 21, 45, FR-MKP-005), but Risks limits v1 to 2 machines (line 214) and the hotkey scheme only encodes two states. State: architecture is multi-ready, v1 UX is exactly two machines, and remote selection for N > 1 is deferred with the hotkey scheme to be extended (F2..Fn or a cycle key) in a later iteration.

### MAJ-03: Failsafe design is missing. For an input-swallowing product, this is a first-class requirement, not polish.

**Location**: Risks (lines 208-214), absent elsewhere.

Failure scenarios the plan does not address:

1. **Agent hangs while remote-active**: hooks stay installed, all input swallowed, machine unusable from the console. (Process crash is the benign case: hooks auto-uninstall when the owning process dies.) Mitigation: a watchdog (the supervisor service from BLK-01 option 1 earns its keep here) plus an in-hook dead-man check (if the forwarding pipeline has not drained in N ms, stop swallowing).
2. **Connection loss while remote-active**: keyboard goes nowhere. Mitigation: heartbeat over the bidi stream; on loss, auto-revert to local within a defined deadline (this deserves an AC with a number, e.g. under 2 seconds).
3. **Hook starvation removal**: Windows silently removes LL hooks whose callbacks exceed the timeout budget; on Win11 this can be silent and permanent for the process. Mitigations: hook callbacks do nothing but enqueue to a lock-free channel and return; no allocation, no logging, no network on the hook thread; a periodic self-check that re-installs hooks if they stop delivering.
4. **Panic escape**: a documented always-local key sequence that unconditionally reverts to local processing, handled entirely inside the hook state machine.

**Required change**: Add a Failsafe FR with ACs for each scenario, plus derived TESTs (the toggle state machine plus a mock transport makes 1, 2, and 4 unit-testable; 3 is an integration test). Given that MWB's flakiness is this project's stated motivation, reviewer opinion: this section is the soul of the product.

### MAJ-04: Mouse capture and injection strategy is unspecified, and the stated ClipCursor use is a no-op.

**Location**: Context (line 13), Recommended Approach (lines 37, 44), FR-MKP-005 (lines 131-135).

- "On active: ClipCursor to screen bounds" (line 44) does nothing: the cursor is already confined to the virtual desktop. ClipCursor is meaningful for pinning the cursor while remote-active (confine to a 1x1 rect or freeze point so the local cursor stops wandering while deltas stream out). State the actual mechanism.
- **Relative motion capture**: with moves swallowed, the plan needs a defined delta source. Options: WM_INPUT raw input with RIDEV_INPUTSINK (recommended: true hardware deltas, unaffected by clipping) versus deriving deltas from MSLLHOOKSTRUCT.pt against the pinned position (workable but fiddly at screen edges). Choose one in the plan.
- **Ballistics/acceleration**: raw deltas re-accelerated by the remote's pointer settings behave differently from host-accelerated absolute positions. Decide: send raw deltas and inject relative (remote's feel), or map to absolute normalized coordinates (MOUSEEVENTF_ABSOLUTE | VIRTUALDESK). Either is defensible; undefined is not.
- **DPI awareness**: the agent must manifest PerMonitorV2, or every coordinate API it touches is virtualized and SetMousePosition will be wrong on mixed-DPI setups. This is an easy-to-miss TR; write it down.
- **Display addressing**: `SetMousePositionRequest.display int32` has no defined numbering. Monitor enumeration order is unstable across reboots/hotplug. Use the display device name or a stable adapter/output identifier, and define the coordinate space (per-monitor logical vs physical pixels).

### MAJ-05: Clipboard algorithm gaps: loop prevention, ordering, and scope are undefined.

**Location**: Context (line 16), Recommended Approach (line 40), FR-MKP-004 (lines 124-129).

- **Echo loop**: A copies, syncs to B; B's listener fires on the set and echoes back to A. Standard fix: stamp synced clips with a private clipboard format and ignore listener events whose content carries the stamp (plus a short suppression window as belt-and-braces). This MUST be designed and tested; it is the first bug this feature will otherwise ship.
- **Merge ordering**: "LIFO merge (newest first)" across two machines requires a definition of "newest" under clock skew. Recommend: order by receive-time at each node with sequence tiebreakers, or a hybrid logical clock; do not trust raw wall clocks from peers. Concurrent copies on both machines within the skew window need a deterministic rule; write it.
- **Format scope and caps**: text only for v1, or images/files too? Per-item size cap? (A 200 MB screenshot in a 50-deep persisted encrypted history is a surprise nobody wants.) Define supported formats, per-item and total-history size limits, and truncation/skip behavior, each with ACs.
- Privacy formats and persistence opt-in: covered under BLK-05 item 4, referenced here for traceability to FR-MKP-004.

### MAJ-06: The tray-invokes-REPL management plane needs its channel and latency defined.

**Location**: Context (line 20), Recommended Approach (lines 42-43), Critical Files (line 64).

The user requirement makes the REPL the primary management surface, and the tray "calls into REPL to send gRPC commands." Consequences to design around, not discover:

- Spawning a framework-dependent dotnet global tool per menu click costs JIT/startup latency (order of a second) per action. If the tray shells out per action, that is the UX. Recommendation that preserves the requirement: factor the REPL's command layer into a shared library (`MouseKeyProxy.Client`); the REPL binary and the tray both consume it in-proc; the REPL remains the human/scripting surface. If shelling out is kept anyway, add an AC with a number (tray action feedback under 300 ms, or show async progress).
- Define the local management channel explicitly: how do REPL/tray reach the local agent? Recommend a named pipe (no firewall involvement, session-local ACL) or localhost-bound gRPC; state which, since firewall scope and the security review (BLK-05) depend on it.
- The end-to-end path for a tray action currently reads: tray -> spawn REPL -> local agent -> host/remote agent -> injection. Draw this chain (both machines, with session boundaries) in the plan's new topology section (BLK-01) so every hop has an owner and a failure behavior.

### MAJ-07: Resilience and observability requirements are absent, despite being the project's motivation.

**Location**: Context (line 28), Risks (lines 208-214), absent elsewhere.

The plan's origin story is MWB's "service flakiness, UseService flips, socket errors," yet no FR covers: automatic reconnect with backoff, heartbeat intervals and loss deadlines, behavior on event-queue overflow (coalesce mouse moves, never coalesce key events; define drop policy), a soak/stability acceptance test (e.g. 24h continuous session with zero stuck-input incidents), structured logging (EventLog for service lifecycle plus rolling file logs in LocalAppData), and REPL diagnostics (`status`, `logs`, connection state, last-error). Add an FR for resilience and an FR for observability, with derived TESTs. These are the requirements that make this product better than the thing it replaces.

---

## 4. Minor findings

**MIN-01: Source-control contradiction, in-plan and against global rules.** Line 32 states ADO origin is primary and GitHub is a downstream mirror added "per explicit user instruction," but line 178 (and the actual repo, verified) sets `origin` to the GitHub URL. Either document this project as an explicit, user-directed exception (origin = GitHub, no ADO) in the plan's source-control note, or conform: `origin` = Azure DevOps, secondary remote named `github`. Pick one; the current text contradicts itself.

**MIN-02: "elevate pwsh 5.1" is a terminology error.** `pwsh` is PowerShell 7+; Windows PowerShell 5.1 is `powershell.exe`. Lines 18, 43, 213 should name the exact binary the REPL elevates and why (5.1 is always present; 7+ may not be on a fresh box, which is presumably the rationale: say so). Also specify behavior when the operator declines the UAC prompt (fail the command with clear output, leave no partial firewall state).

**MIN-03: `global.json` pin is self-defeating.** `rollForward: latestMajor` permits a future .NET 11+ SDK to satisfy the pin, contradicting the intent of pinning 10.0.100. Use `latestFeature` (or `latestPatch`) for reproducible builds.

**MIN-04: `.gitignore` globally ignores `*.exe`, `*.dll`, `*.pdb`.** Any intentionally vendored binary or native test fixture will silently vanish from `git status`. Acceptable if deliberate; note it, or scope the patterns to output directories (already covered by `bin/`, `obj/`, `artifacts/`).

**MIN-05: LICENSE is missing from Critical Files.** The project is explicitly free and open source with planned social announcements; a license choice (MIT is the natural fit for the goal) is required before the repo is public and before any announcement drafts. Add `LICENSE` to the file list and the choice to Inception.

**MIN-06: Protobuf polish items** (beyond BLK-03): add `option csharp_namespace`; give `CommandResult` an error-code enum rather than success+string; rename `HwndNode.class` to `class_name` (protoc handles the keyword but the generated identifier mangling is avoidable); cap `LocateProcessResponse` tree depth/size or make it pageable (a full hwnd tree on a busy desktop is large); define `InputEvent.time` semantics (which clock, whose epoch, used for what) or drop it in favor of the sequence number.

**MIN-07: "Done" claims need attached evidence.** The requirements store contents (FRs, ACs, TESTs "surfaced via MCP") cannot be audited from the repo; `mcp.db` is 0 bytes. Attach a requirements export (the wrap-up flow's requirements document) or a REPL query transcript to `docs/` as review evidence. Reviewers should be able to confirm the stored requirement set matches the plan text.

**MIN-08: FR-MKP-006 is five requirements wearing one ID.** Tool install/service registration, pairing/discovery, firewall lifecycle, settings persistence, and "reqs queryable" (a process assertion, not a product behavior) are separately testable concerns with separate failure modes. Split them so tests trace cleanly; move the process assertion out of product FRs entirely.

**MIN-09: Wireframes are referenced but do not exist yet.** `docs/wireframes/` is absent. Fine at this stage, but the plan says they "Drive Tray impl + ACs," so their creation must be sequenced as an Inception deliverable that blocks tray TESTs, and the tray ACs should reference them by filename.

**MIN-10: Lifecycle gaps: upgrade, uninstall, version skew.** No story for: updating the tool while the service/agent is running (stop, swap, restart, and who orchestrates it); uninstall completeness AC (service gone, firewall rules gone, optional settings/history purge with a prompt); and protocol version mismatch between the two machines after upgrading only one (the hello/version handshake from BLK-03 feeds this: define reject-with-message behavior).

**MIN-11: No estimates or iteration sizing.** The Byrd/RUP breakdown orders work but does not size it. Optional per team norms, but even coarse T-shirt sizes per Construction iteration would make the review of scope-vs-risk more concrete. Take or leave.

---

## 5. Strengths (keep these)

1. The risk list names the two genuinely hard problems (session 0, DPAPI identity) before a reviewer had to. The findings above are mostly "the component layout does not yet obey your own risk list," which is a far better starting position than not knowing the risks.
2. Hotkey-only switching with no edge detection is the single best scope decision in the plan. It deletes the entire class of geometry/edge/multi-monitor-transition bugs that make MWB fragile, and it matches the stated user need exactly.
3. Host-central topology is the right call for pairing simplicity, and (once BLK-03's remote-dials-host streaming lands) it collapses the firewall problem to one inbound rule on one machine.
4. Reuse targets are proven in-house patterns (WindowsServiceHelper, Manage scripts, REPL install flow, Nuke), which lowers integration risk.
5. LIFO semantics with an explicit cap and explicit "No FIFO" AC shows the requirement was actually interrogated, not just transcribed.
6. The stop-for-peer-review gate was honored: repo contains setup only, no implementation ran ahead of review. Process discipline is visible.
7. Post-completion marketing work is cleanly separated from v1 scope instead of polluting it.

---

## 6. Conditions for approval

The plan is approvable for Inception exit when a revision addresses:

1. **[BLK-01]** Process topology section: every desktop-interactive component assigned to a user-session agent; service role decided (supervisor via CreateProcessAsUser, or dropped by explicit user decision); gRPC endpoint ownership stated; diagram included.
2. **[BLK-02]** Identity table: every persisted artifact (settings, secrets, clipboard history) with its owner identity, path, and DPAPI scope.
3. **[BLK-03]** Protocol v2: remote-dials-host persistent bidi stream carrying input/clipboard/control/heartbeat with sequence numbers and a version handshake; clipboard and pairing surfaces defined; InputKind completed; management RPC routing decided.
4. **[BLK-04]** Test architecture section (framework, seams/interfaces, mock strategy, CI vs two-machine split); test projects in Critical Files; missing FRs added (Mirror Mode, Inject Text, pairing/security, discovery, resilience, failsafe, observability, uninstall); ACs rewritten to be objectively verifiable; FR-to-TEST traceability matrix named as an Inception exit gate.
5. **[BLK-05]** Threat model section: protected binary location for anything running elevated/LocalSystem; pairing trust bootstrap with operator verification; RPC authN/authZ and bind policy; clipboard privacy-format handling; TLS material design.
6. **[MAJ-01..07]** Each resolved in plan text or explicitly deferred with a user-visible decision recorded (the MAJ-01 exclusion list and MAJ-03 failsafe FR should not be deferred).
7. Minors: fix MIN-01, MIN-02, MIN-03 in the same revision (they are one-line fixes); the rest may be tracked as TODOs against the plan.

Re-review scope for the next pass: the revised sections only, plus a spot-check that FR/TEST changes landed in the requirements store with evidence (MIN-07).

---

## 7. Appendix: recommended topology sketch (for the revision to adapt)

```
MACHINE A (host)                              MACHINE B (remote)
+--------------------------------+           +--------------------------------+
| Session 0                      |           | Session 0                      |
|  [optional] Supervisor service |           |  [optional] Supervisor service |
|  - launches/watchdogs agent    |           |  - launches/watchdogs agent    |
|  - NO input, NO clipboard,     |           |                                |
|    NO gRPC                     |           |                                |
+--------------------------------+           +--------------------------------+
| User session                   |           | User session                   |
|  Agent.exe (tray + engine)     |  bidi     |  Agent.exe (tray + engine)     |
|  - LL hooks + raw input        |  gRPC/TLS |  - SendInput injector          |
|  - chord/toggle state machine  |<=========>|  - clipboard listener/setter   |
|  - clipboard listener/setter   |  B dials A|  - LIFO store (DPAPI user)     |
|  - LIFO store (DPAPI user)     |           |                                |
|  - gRPC server (inbound: A     |           |  (outbound-only, no inbound    |
|    only; one firewall rule)    |           |   firewall rule needed)        |
|  - named pipe for local mgmt   |           |  - named pipe for local mgmt   |
+---------------^----------------+           +---------------^----------------+
                | named pipe                                 | named pipe
        REPL (dotnet tool)                           REPL (dotnet tool)
        Tray uses shared client lib                  (same)
```

One stream, remote-initiated, multiplexing: InputEventBatch (seq), ClipboardPush, ControlMessage (ToggleActive, ModifierResync, Heartbeat, VersionHello), Ack.

---

*Review produced under the Byrd Development Process review gate defined in PLAN-MKP-001 ("Stop pending reviews"). No implementation artifacts were created or modified; this document is the sole output.*
