# Requirements Matrix (MouseKeyProxy)

Traceability is validated in CI by the Nuke `ValidateTraceability` target, which parses this matrix and
the requirement docs for malformed rows, orphan matrix entries, and dangling trace links.

| Requirement | Type | Status | Source File | Traceability |
| --- | --- | --- | --- | --- |
| FR-HOTKEY-001 | Functional | Tracked | Functional-Requirements.md | TR-HOTKEY-CONTRACT-001; TEST-HOTKEY-001 |
| FR-OWNERSHIP-001 | Functional | Tracked | Functional-Requirements.md | TR-OWNERSHIP-CONTRACT-001; TEST-OWNERSHIP-001 |
| FR-MKP-001 | Functional | Tracked | Functional-Requirements.md | TR-MKP-INPUT-001, TR-MKP-RELI-001, TR-MKP-REPL-001; TEST-MKP-001, TEST-MKP-008, TEST-MKP-012 |
| FR-MKP-002 | Functional | Tracked | Functional-Requirements.md | TR-MKP-INPUT-001, TR-MKP-RELI-001, TR-MKP-AGENTIPC-001; TEST-MKP-003, TEST-MKP-008, TEST-MKP-012 |
| FR-MKP-003 | Functional | Tracked | Functional-Requirements.md | TR-MKP-INPUT-001, TR-MKP-SEC-001, TR-MKP-RELI-001; TEST-MKP-003, TEST-MKP-005, TEST-MKP-009 |
| FR-MKP-004 | Functional | Tracked | Functional-Requirements.md | TR-MKP-CLIP-001, TR-MKP-SEC-001, TR-MKP-RELI-001; TEST-MKP-004, TEST-MKP-006, TEST-MKP-012 |
| FR-MKP-005 | Functional | Tracked | Functional-Requirements.md | TR-MKP-ORCH-001, TR-MKP-INPUT-001, TR-MKP-SEC-001, TR-MKP-RELI-001; TEST-MKP-009, TEST-MKP-010, TEST-MKP-011, TEST-MKP-014, TEST-MKP-020 |
| FR-MKP-006 | Functional | Tracked | Functional-Requirements.md | TR-MKP-REPL-001, TR-MKP-UI-001, TR-MKP-AGENTIPC-001, TR-MKP-ORCH-001; TEST-MKP-007, TEST-MKP-012, TEST-MKP-015, TEST-MKP-021 |
| FR-MKP-007 | Functional | Tracked | Functional-Requirements.md | TR-MKP-LOG-001, TR-MKP-REPL-001; TEST-MKP-019, TEST-MKP-021 |
| FR-MKP-008 | Functional | Tracked | Functional-Requirements.md | TR-MKP-BRAND-001, TR-MKP-UI-001; TEST-MKP-016, TEST-MKP-015 |
| FR-MKP-009 | Functional | Tracked | Functional-Requirements.md | TR-MKP-AGENTCMD-001, TR-MKP-ORCH-001; TEST-MKP-017, TEST-MKP-018, TEST-MKP-021 |
| FR-MKP-010 | Functional | Tracked | Functional-Requirements.md | TR-MKP-AGENTCMD-001; TEST-MKP-017, TEST-MKP-018 |
| FR-MKP-011 | Functional | Tracked | Functional-Requirements.md | TR-MKP-TESTDOUBLE-001; TEST-MKP-022 |
| TR-HOTKEY-CONTRACT-001 | Technical | Tracked | Technical-Requirements.md | FR-HOTKEY-001; TEST-HOTKEY-001 |
| TR-OWNERSHIP-CONTRACT-001 | Technical | Tracked | Technical-Requirements.md | FR-OWNERSHIP-001; TEST-OWNERSHIP-001 |
| TR-MKP-ARCH-001 | Technical | Tracked | Technical-Requirements.md | FR-OWNERSHIP-001; TEST-MKP-002 |
| TR-MKP-AGENTIPC-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-002, FR-MKP-006; TEST-MKP-012, TEST-MKP-015 |
| TR-MKP-BRAND-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-008; TEST-MKP-016 |
| TR-MKP-CLIP-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-004; TEST-MKP-004, TEST-MKP-006 |
| TR-MKP-INPUT-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-001, FR-MKP-002, FR-MKP-003, FR-MKP-005; TEST-MKP-001, TEST-MKP-003, TEST-MKP-009, TEST-MKP-010, TEST-MKP-011 |
| TR-MKP-LOG-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-007; TEST-MKP-019 |
| TR-MKP-ORCH-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-005, FR-MKP-006, FR-MKP-009; TEST-MKP-014, TEST-MKP-020, TEST-MKP-021 |
| TR-MKP-RELI-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-001, FR-MKP-002, FR-MKP-003, FR-MKP-004, FR-MKP-005; TEST-MKP-008 |
| TR-MKP-REPL-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-001, FR-MKP-006, FR-MKP-007; TEST-MKP-007, TEST-MKP-012, TEST-MKP-021 |
| TR-MKP-SEC-001 | Technical | Tracked | Technical-Requirements.md | FR-OWNERSHIP-001, FR-MKP-003, FR-MKP-004, FR-MKP-005; TEST-MKP-005 |
| TR-MKP-UI-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-006, FR-MKP-008; TEST-MKP-015, TEST-MKP-016 |
| TR-MKP-AGENTCMD-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-009, FR-MKP-010; TEST-MKP-017, TEST-MKP-018 |
| TR-MKP-TESTDOUBLE-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-011; TEST-MKP-022 |
| TEST-HOTKEY-001 | Test | Tracked | Testing-Requirements.md | FR-HOTKEY-001; TR-HOTKEY-CONTRACT-001 |
| TEST-OWNERSHIP-001 | Test | Tracked | Testing-Requirements.md | FR-OWNERSHIP-001; TR-OWNERSHIP-CONTRACT-001 |
| TEST-MKP-001 | Test | Tracked | Testing-Requirements.md | FR-MKP-001; TR-MKP-INPUT-001 |
| TEST-MKP-002 | Test | Tracked | Testing-Requirements.md | FR-OWNERSHIP-001; TR-MKP-ARCH-001 |
| TEST-MKP-003 | Test | Tracked | Testing-Requirements.md | FR-MKP-002, FR-MKP-003; TR-MKP-INPUT-001 |
| TEST-MKP-004 | Test | Tracked | Testing-Requirements.md | FR-MKP-004; TR-MKP-CLIP-001 |
| TEST-MKP-005 | Test | Tracked | Testing-Requirements.md | FR-OWNERSHIP-001, FR-MKP-003, FR-MKP-004, FR-MKP-005; TR-MKP-SEC-001 |
| TEST-MKP-006 | Test | Tracked | Testing-Requirements.md | FR-MKP-004; TR-MKP-CLIP-001 |
| TEST-MKP-007 | Test | Tracked | Testing-Requirements.md | FR-MKP-006; TR-MKP-REPL-001 |
| TEST-MKP-008 | Test | Tracked | Testing-Requirements.md | FR-MKP-001, FR-MKP-002, FR-MKP-004; TR-MKP-RELI-001 |
| TEST-MKP-009 | Test | Tracked | Testing-Requirements.md | FR-MKP-003, FR-MKP-005; TR-MKP-INPUT-001, TR-MKP-SEC-001 |
| TEST-MKP-010 | Test | Tracked | Testing-Requirements.md | FR-MKP-005; TR-MKP-INPUT-001 |
| TEST-MKP-011 | Test | Tracked | Testing-Requirements.md | FR-MKP-005; TR-MKP-INPUT-001, TR-MKP-SEC-001 |
| TEST-MKP-012 | Test | Tracked | Testing-Requirements.md | FR-MKP-001, FR-MKP-002, FR-MKP-004, FR-MKP-006; TR-MKP-REPL-001, TR-MKP-AGENTIPC-001 |
| TEST-MKP-013 | Test | Tracked | Testing-Requirements.md | FR-MKP-005, FR-MKP-006; release provenance |
| TEST-MKP-014 | Test | Tracked | Testing-Requirements.md | FR-MKP-005; TR-MKP-ORCH-001 |
| TEST-MKP-015 | Test | Tracked | Testing-Requirements.md | FR-MKP-006, FR-MKP-008; TR-MKP-UI-001 |
| TEST-MKP-016 | Test | Tracked | Testing-Requirements.md | FR-MKP-008; TR-MKP-BRAND-001 |
| TEST-MKP-017 | Test | Tracked | Testing-Requirements.md | FR-MKP-009, FR-MKP-010; TR-MKP-AGENTCMD-001 |
| TEST-MKP-018 | Test | Tracked | Testing-Requirements.md | FR-MKP-009, FR-MKP-010; TR-MKP-AGENTCMD-001 |
| TEST-MKP-019 | Test | Tracked | Testing-Requirements.md | FR-MKP-007; TR-MKP-LOG-001 |
| TEST-MKP-020 | Test | Tracked | Testing-Requirements.md | FR-MKP-005; TR-MKP-ORCH-001 |
| TEST-MKP-021 | Test | Tracked | Testing-Requirements.md | FR-MKP-006, FR-MKP-009; TR-MKP-REPL-001, TR-MKP-ORCH-001 |
| TEST-MKP-022 | Test | Tracked | Testing-Requirements.md | FR-MKP-011; TR-MKP-TESTDOUBLE-001 |
| FR-MKP-012 | Functional | Tracked | Functional-Requirements.md | TR-MKP-HID-001; TEST-MKP-023, TEST-MKP-024, TEST-MKP-025, TEST-MKP-026, TEST-MKP-027, TEST-MKP-028, TEST-MKP-029 |
| TR-MKP-HID-001 | Technical | Tracked | Technical-Requirements.md | FR-MKP-012; TEST-MKP-023, TEST-MKP-024, TEST-MKP-025, TEST-MKP-026, TEST-MKP-027, TEST-MKP-028, TEST-MKP-029 |
| TEST-MKP-023 | Test | Tracked | Testing-Requirements.md | FR-MKP-012; TR-MKP-HID-001 |
| TEST-MKP-024 | Test | Tracked | Testing-Requirements.md | FR-MKP-012; TR-MKP-HID-001 |
| TEST-MKP-025 | Test | Tracked | Testing-Requirements.md | FR-MKP-012; TR-MKP-HID-001 |
| TEST-MKP-026 | Test | Tracked | Testing-Requirements.md | FR-MKP-012; TR-MKP-HID-001 |
| TEST-MKP-027 | Test | Tracked | Testing-Requirements.md | FR-MKP-012; TR-MKP-HID-001 |
| TEST-MKP-028 | Test | Tracked | Testing-Requirements.md | FR-MKP-012; TR-MKP-HID-001 |
| TEST-MKP-029 | Test | Tracked | Testing-Requirements.md | FR-MKP-012; TR-MKP-HID-001 |
