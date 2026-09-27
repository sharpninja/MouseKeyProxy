# Tray Agent pairing repair, 2026-09-27

The log identified a real certificate lifetime regression: reloading the saved peer credential disposed a certificate still used by an authenticated channel. Codex gave each channel independent client/private-key and CA certificate lifetimes, with owned handler disposal and unchanged paired-CA validation. No Claude implementation was used after the explicit instruction to stop.

The user authorized repair of existing blockers before publication. Custom token paths now remain isolated from the live machine mirror, normalized default paths retain service mirror/fallback support, and service payload validation precedes service stop or filesystem changes. The stale Compliance test now checks current canonical Agent toggle errors and nonzero behavior. The Markdown traceability projection uses live MCP IDs and mappings.

Validation before publication:
- Supported command: .\build.ps1 Test --configuration Release. Exit0;404passed,0failed,0skipped across8projects. The supported default filter excludes Category=TwoMachineE2E.
- Supported command: .\build.ps1 ValidateTraceability. Exit0;91definedIDs and91matrixrows.
- Live authenticated empty InjectInput at https://192.168.0.172:50051 succeeded with both original source certificates disposed BEFORE first RPC; credential unchanged, events0.
- Default/machine control-token hashes still match after the full test suite; neither token nor hash was printed.
- All immediate final code-verification builds pass; git diff --check clean. An earlier incremental Common build failed because its helper had not yet been inserted; the next edit fixed it before other source work.

Full build/test/live logs are in docs/receipts/certificate-tests. Causal red tests and independent phase receipts are retained, including the initial setup failures before the valid certificate red test.

Publication and final code review are pending at creation of this receipt. The running pre-fix tray is PID117124 in interactive session1, Connected to192.168.0.172 with forwarding inactive. This is not a completion claim.

Final publication and operational verification, 2026-09-27T21:46:13Z:

- Nuke PublishAgent Release exited0. The fixed published executable is running as PID113768 in interactive session1.
- SHA256: 3643841D681CC14E87C170AB5E0102A66524FF508D7A6F414615C8F44F8F281F. Binary: F:\GitHub\MouseKeyProxy\output\payloads\agent\MouseKeyProxy.Agent.exe.
- New startup self-heal log reports mTLS OK on preferred endpoint https://192.168.0.172:50051 and device channel OK. Public agent status remains Connected with forwardingActive=false.
- Fresh real authenticated InjectInput succeeded with0events after both source certificates were disposed before the first RPC. Saved pairing credential bytes are identical across publication/restart (compared without disclosing hashes). No trust reset.
- Operational receipts: certificate-tests/certificate-lifetime-publish-fixed.log, certificate-lifetime-published-agent.json, certificate-lifetime-live-pi-published.log, certificate-lifetime-published-self-heal.log.
- Independent CODE GREEN/PREPUBLICATION: OverallVerdict AGREE; PASS4,FAIL0,UNKNOWN0; accuracy99%,completeness99%. Durable request/response: hv/20260927T212502Z-certificate-final-green.request.jsonl and matching.response.jsonl. Entire8425-character verdict/JSON verified exactly in MCP session Codex-20260927T201344Z-plugin-session,request req-20260927T212458Z-prompt-3f6e,completed turn85922.
- The prior review's full-suite/traceability FAILs are repaired, and the earlier receipt-preparation UNKNOWN was remediated by a clean captured inception run. The final review has no FAIL or UNKNOWN. Direct operational verification follows the user's explicit waiver of separate non-code review.
- Authenticated paired identity and known SSH host fingerprint support the Orange Pi endpoint; physical board model and whether the Pi itself uses Wi-Fi were not queried because SSH login was unavailable. No claim is made that those hardware details were verified.

Final audit reconciliation: supported native MCP completed parent turn85745 in session Codex-20260927T210740Z-plugin-session,request req-20260927T205321Z-prompt-5a56. Live native query verified full response,18actions,11decisions and13files. Plugin wrapper returned empty output and left a stale local in_progress cache; incidental defect was submitted successfully as triage-report-df63b6729bf14fd9b89a35b2b4bfee0d. After server completion was proved, only the local plugin cache was reconciled with the object-first YAML helper and exact session/request guards. Stop-gate then returned its passing empty object. No authoritative log storage was hand-edited.

Live requirement query verified TR-MKP-SEC-001 and SEC-002 completed with3/3AC each, TEST-MKP-050 completed with3/3AC, and the scoped repair AC satisfied on TEST-MKP-007/012 while their broader historical statuses remainpending. Technical/testing projections were exported through MCP; final completed-state matrix revalidated91/91 (certificate-tests/certificate-final-completed-traceability.log). The documented docType names technical/testing were rejected; live server enums tr/test succeeded. All discovered audit/tool issues were recovered or triaged; no product pairing blocker remains.
