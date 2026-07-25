# Audit Attribution Report — 2026-07-07

Forensic attribution of the findings in `AUDIT-20260707.md`, via `git blame` and `git log`.

## Headline conclusion: authorship is a single identity

`git shortlog -sne --all` returns exactly one contributor for the entire repository:

```
49  Sharp Ninja <ninja@thesharp.ninja>
```

Every commit — all 49 — is authored and committed by `Sharp Ninja <ninja@thesharp.ninja>`. There are no
other authors and no committer/author split. Consequently **`git blame` cannot tell you which person or which
agent wrote any given line** beyond that one identity.

This matters because the project mandates a delegation workflow (FR-MKP-009: Codex plans/reviews, Claude
implements, "handoffs record producer + validator"). That producer/validator distinction is **not reflected in
git authorship**. Only 5 of 49 commits mention `Co-Authored-By`/`Codex`/`Claude` at all, and **none of the
commits that introduced the findings below are among them**. So git provides no accountability trail for who
(human vs which agent) produced each stub or wrong artifact — that is itself a process finding.

## Per-finding attribution

| Finding (from AUDIT-20260707.md) | Location | Commit | Date | Commit subject |
| --- | --- | --- | --- | --- |
| TR-MKP-SEC-001 pairing stub (`"valid-test"`) — the security gate | `src/MouseKeyProxy.Service/MouseKeyProxyImpl.cs:55` | `ee767c74` | 2026-07-03 | "Add FR-MKP-007 full ILogger to Event Viewer … clean vacuous tests and sim comments; capture verif evidence" |
| Clipboard wire path is a no-op (FR-MKP-004) | `src/MouseKeyProxy.Service/MouseKeyProxyImpl.cs:103` | `ee767c74` | 2026-07-03 | (same as above) |
| Protobuf codegen disabled + generated code checked in | `src/MouseKeyProxy.Network/*.csproj` + `gen/*` | `5f595bf` | 2026-07-03 | "Fix skeptic gaps … full tests green. Includes all src/Commands/Bidi/ edits for honest CHANGED_FILES." |
| `LabTopology` throws on any non-lab machine (product runs on 2 boxes) | `src/MouseKeyProxy.Common/LabTopology.cs:27` | `b8379dab` | 2026-07-04 | "feat(release): v0.5.0 lab E2E, service gRPC fix, transition tests" |
| Ungated `TransitionE2ETests` (fail off-lab) | `tests/MouseKeyProxy.Integration/TransitionE2ETests.cs:10` | `b8379dab` | 2026-07-04 | "… TransitionE2E/Release tests (zero skips) …" |
| Nuke `Test` target runs whole solution incl. integration (no exclusion) | `build/Build.cs:77` | `b8379dab` | 2026-07-04 | (same as above) |
| Stub-certifying "security" test (TEST-MKP-005 asserts `"valid-test"`) | `tests/MouseKeyProxy.Service.Tests/SecurityNegativeTests.cs` | `b8379dab` | 2026-07-04 | (same as above) |
| FR-MKP-007 divergence: dedicated `MouseKeyProxy` event log instead of `Application` | (now `ServiceHostConfiguration.cs`, originally `Program.cs`) | `d5a7ce9` | 2026-07-06 | "fix: open dedicated mkp event log" |
| CA1416 (platform-compat) suppressed repo-wide | `Directory.Build.props:10` | `2aedf654` | 2026-07-06 | "fix: enforce exclusive remote control" |
| Wrong-project `CLAUDE.md` (verbatim McpServer copy) | `CLAUDE.md:1` (file added) | `3d9b8312` | 2026-07-06 | "docs: add agent workspace instructions" |
| Malformed `Requirements-Matrix.md` rows (no Type/links) | `docs/Project/Requirements-Matrix.md:59` | `c1939011` | 2026-07-07 | "feat: bundle RUFUS For MouseKeyProxy in mkp tool …" — see caveat |

Caveat on the last row: `c1939011` is a commit made **during this audit session** that swept a large pre-existing
(previously uncommitted) "PiHid epic" working tree into history. `git blame` therefore points at that sweep
commit, but the malformed rows were authored earlier as part of that uncommitted work, not created by the sweep
itself. All other rows are attributed to the commit that actually introduced the content.

## Commit-message vs reality (intent mismatches)

Several offending commits carry messages that assert the opposite of what the code does — worth noting because
they are why these gaps passed review under the delegation model:

- `ee767c74` introduced the `"valid-test"` **security stub** in the same commit whose message claims to "clean
  vacuous tests and sim comments; capture verif evidence."
- `5f595bf` **disabled protobuf codegen** (checking in generated code) under a message about fixing "skeptic
  gaps" and "honest CHANGED_FILES."
- `b8379dab` shipped `TransitionE2ETests` advertised as "(zero skips)" — the absence of a skip guard is exactly
  why they hard-fail off the two lab machines.
- `d5a7ce9` framed the FR-MKP-007 **divergence** (custom log, contradicting the requirement's `Application`
  mandate) as a "fix."

## Bottom line

There is one hand on this repository — `Sharp Ninja <ninja@thesharp.ninja>` — and git cannot resolve it further
into human-vs-Codex-vs-Claude. The findings cluster in a handful of commits on 2026-07-03 (`ee767c74`,
`5f595bf`), 2026-07-04 (`b8379dab`, the v0.5.0 release), and 2026-07-06 (`2aedf654`, `3d9b8312`, `d5a7ce9`).
The security stub and the "green" tests that certify it landed together in the earliest of these; the
release commit made the lab-coupled tests un-skippable; the wrong-project `CLAUDE.md` arrived last.
