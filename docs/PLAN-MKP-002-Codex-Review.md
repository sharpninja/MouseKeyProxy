# Codex Peer Review: PLAN-MKP-002

**Reviewed plan**: `docs/PLAN-MKP-002.md`  
**Review date**: 2026-07-03  
**Reviewer**: Codex  
**Verdict**: Revise and re-review. Round 2 is materially stronger than PLAN-MKP-001 and resolves several first-round blockers, but it is still not ready to approve for Construction. The remaining issues are mostly concrete: invalid/unfinished proto shape, unresolved ownership of TLS/data-plane secrets, unverifiable Byrd traceability, and a few still-deferred architectural choices.

## Scope Reviewed

- `docs/PLAN-MKP-002.md`
- Prior review: `docs/PLAN-MKP-001-Codex-Review.md`
- Prior Claude review summary: `docs/PLAN-MKP-001-Claude-Review.md`
- Traceability artifacts: `docs/requirements-matrix.yaml`, `docs/requirements-export.md`, `docs/todo.yaml`
- Workspace planning contract in `AGENTS-README-FIRST.yaml`
- Current repo state for `global.json` and remotes

## Round-1 Disposition

Resolved or largely resolved:

- Service vs user-session ownership is now explicitly separated for input, clipboard, focus, hooks, and tray.
- The impossible "all keyboard input" language was replaced with a realistic support matrix.
- `dotnet tool install` no longer claims to register the service automatically.
- GitHub-as-`origin` is now aligned with the current repo remote.
- v1 scope is now two-node, not hidden multi-remote.
- Tray no longer spawns the REPL per click; it shares command implementation.
- Marketing/media work was mostly moved to a release/transition plan.

Not yet resolved:

- Proto/data-plane contract.
- mTLS identity and key ownership across service/agent.
- FR/TR/TEST/TODO evidence and exact first-test gates.
- Mouse capture/injection decision.
- Clipboard/file-drop scope consistency.

## Blocking Findings

### B1. The proto block is still not implementable

**Evidence**: Plan lines 234-381.

The text says the connection topology is a persistent bidirectional stream (line 235), but the service definition exposes only unary RPCs plus `ClipboardSync` (lines 243-250). There is no `OpenSession`, `Connect`, or equivalent streaming RPC. `ControlMessage`, `Heartbeat`, and `Ack` are defined (lines 356-373) but are not carried by any RPC. `ClipboardFormat` is defined twice in the same proto package (lines 351-354 and 375-378), which makes the block invalid. Line 381 still says the "full contract includes" error codes, ack/retry, and compatibility rules, which is a deferral rather than a locked contract.

**Required fix**: Replace the proto sketch with a single compilable contract and validate it before approval. Pick one shape:

- Streaming: `rpc OpenSession(stream ClientEnvelope) returns (stream ServerEnvelope)` with input, clipboard, control, heartbeat, and ack messages inside envelopes.
- Unary: remove the bidi-stream topology claim and define every unary command, callback/routing path, ack/retry behavior, and firewall consequence.

Either way, remove duplicate messages, include compatibility/version rejection rules, and add a `protoc` or `buf lint` dry-run to the plan validation evidence.

### B2. mTLS/private-key ownership conflicts with the service data plane

**Evidence**: Service owns networking/gRPC (lines 58, 124). The plan says pairing/TLS material and user settings live in the user-session agent only (line 132), but also places a TLS server cert in ProgramData with service read access (line 138), then says the service only receives thumbprints and non-secret config (line 144). Pairing is agent-owned (line 188), while transport is gRPC over mTLS (line 189).

This is still not decision-complete. If the service terminates cross-machine gRPC, the service must own or at least read the TLS private key and enforce peer identity. If the agent owns all TLS secrets, then the agent, not the service, is the network data plane. The current text tries to keep both.

**Required fix**: Choose the data-plane owner:

- Service-terminated mTLS: service owns the machine identity/private key in a machine store or ProgramData ACL, pairing agent provisions it through IPC, and tests verify ACLs/revocation.
- Agent-terminated mTLS: agent owns gRPC and TLS, service becomes lifecycle/control plane only, and the hot path no longer routes through service networking.

The identity table, process ownership section, pairing flow, proto topology, and latency TR must all match the same choice.

### B3. Byrd traceability is still unverifiable from the repo artifacts

**Evidence**: Plan lines 383-428 claim MCP mappings and TODOs. `docs/requirements-matrix.yaml` contains a plugin response wrapper with a markdown table of tracked IDs only; it does not show FR-to-TR-to-TEST mappings, acceptance criteria, red/green states, or TODO IDs. `docs/requirements-export.md` contains a `schema_validation_failed` error. `docs/todo.yaml` is still only `# TODO items for this workspace`. Plan lines 424-428 name one concrete test and then fall back to placeholders such as `TEST-MKP-003,005,006,007,008: per TR above`, `dotnet test ...`, and "red states documented in test code" before any test code exists.

The workspace planning contract requires exact test names, expected red state, green criteria, validation scope, FR/TR/TEST mapping, and TODO/session-log traceability before implementation starts.

**Required fix**: Attach a reviewer-verifiable traceability appendix or regenerated artifact that includes:

- Every FR-MKP-001..006 mapped to specific TR-MKP and TEST-MKP IDs.
- Every TEST-MKP-001..008 with test project/file/name, expected red assertion, green criteria, and exact command.
- TODO IDs for each gate, or a clear statement that TODO creation is intentionally deferred and therefore Construction is still blocked.
- A successful requirements export or query transcript, not an error payload.

### B4. Mouse capture and injection still contain "or" decisions

**Evidence**: Lines 168-170 say `WM_INPUT with RIDEV_INPUTSINK` or `MSLLHOOKSTRUCT` deltas, and remote applies acceleration or absolute normalized coordinates. Line 170 says the decision will be documented in code and tests.

This is a plan-level decision because it changes the input model, proto semantics, display/DPI tests, and user feel. Deferring it to code contradicts the stated decision-complete standard.

**Required fix**: Choose the v1 behavior in the plan. My recommendation is:

- Capture raw relative mouse deltas from `WM_INPUT` while remote-active.
- Pin local cursor with `ClipCursor`.
- Transmit relative deltas.
- Let the remote apply its own acceleration.
- Keep absolute positioning only for explicit `SetMousePosition`.

If a different choice is intended, lock it and update tests/proto accordingly.

## Major Findings

### M1. Clipboard file-drop scope contradicts the verification gate

**Evidence**: Line 204 defers file drop (`CF_HDROP`) to a later FR, but the two-machine smoke checklist at line 444 requires copying `text/image/file` and seeing it on the remote LIFO history.

**Required fix**: Remove file copy from v1 verification, or fully specify file-transfer semantics, security prompts, size limits, path handling, and tests.

### M2. Discovery wording still mixes UPnP with LAN discovery

**Evidence**: Line 18 says "UPnP/broadcast discovery"; lines 187 and 195 exclude UPnP IGD port mapping and describe UDP broadcast/mDNS.

**Required fix**: Use precise wording everywhere: "UDP broadcast and/or mDNS LAN discovery; no UPnP IGD/NAT port mapping."

### M3. Wireframes are named as blocking tray-test input but are not present

**Evidence**: Line 46 says wireframes are re-homed as a deliverable blocking tray tests. Lines 452-453 say wireframes are largely unchanged. The repo currently has no `docs/wireframes/` directory or referenced SVG files.

**Required fix**: Either add the wireframe file list and acceptance links directly to PLAN-MKP-002, or create the wireframe artifacts before claiming the tray tests have their visual contract.

### M4. Release-plan numbering is inconsistent

**Evidence**: Line 46 names successor plan `docs/PLAN-MKP-003-Release.md`, but the repo contains `docs/PLAN-MKP-002-Release.md`, and that stub says it is linked from PLAN-MKP-001.

**Required fix**: Pick one release-plan path and update both files. For this plan, `docs/PLAN-MKP-003-Release.md` is cleaner because PLAN-MKP-002 is the active development plan.

### M5. Duplicate heading suggests stale inserted text remains

**Evidence**: `## Failsafes and Reliability Policy` appears twice at lines 214 and 216.

This is minor mechanically, but it undercuts the line-3 claim that all prior duplicate/stale text was fully incorporated.

**Required fix**: Delete the duplicate heading and do one pass for stale "incorporated" claims that are not yet true.

## Minor Findings

- The critical files list puts `mousekeyproxy.proto` under `src/MouseKeyProxy/Network` (line 78), but the ownership-aware project list does not define `src/MouseKeyProxy/`. Put the proto under `Common`, `Service`, or a dedicated `Network` project.
- Line 98 gives subagent TODO rules inside "Project rules"; useful, but it is process policy rather than test architecture. Move it to a workflow/process section.
- Line 227 sets a `<25 ms p95` latency target for host keydown to remote `SendInput`; if the service remains in the data path, add per-hop measurement fields to the proto/log plan so the team can diagnose missed budgets.
- Line 443 uses `Win+R` as a supported smoke example. That is fine only if the hook swallows the local shell invocation reliably; add a specific test because this is a high-risk shortcut.

## Approval Conditions

Approve PLAN-MKP-002 for Elaboration only after these are fixed:

1. A compilable proto with the chosen streaming/unary topology and validation evidence.
2. One consistent data-plane and TLS/private-key ownership model.
3. Verifiable FR/TR/TEST/TODO mapping artifacts with exact test names, red states, green criteria, and commands.
4. A chosen mouse capture/injection strategy.
5. Clipboard verification aligned with the v1 supported formats.
6. Cleanup of stale plan text: UPnP wording, duplicate heading, release-plan path, wireframe deliverables.

## Recommendation

Do not start Construction. A narrow plan-cleanup pass should be enough; most foundational choices are now close. After the six approval conditions are met, I would be comfortable approving an Elaboration slice that builds mocks/prototypes for the process boundary, the chosen data plane, input capture, pairing rejection paths, clipboard merge, and install/uninstall lifecycle.
