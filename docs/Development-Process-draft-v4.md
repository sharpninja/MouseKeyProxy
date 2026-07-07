# Byrd Development Process V4

MouseKeyProxy uses Byrd Development Process V4 with Rational Unified Process terminology.

## RUP Iterations

- Inception: define the problem, requirements, acceptance criteria, and first red tests.
- Elaboration: reduce technical risk with mocks, seams, prototypes, and environment discovery.
- Construction: implement one small behavior slice at a time using red, green, refactor.
- Transition: prove the behavior in the real deployment topology with receipts.

## Byrd Gate

A Byrd gate exits only when the executed validation scope has zero failed tests and zero skipped tests. Deferred work belongs in MCP requirements and TODO state, not in skipped test placeholders.

Every implementation slice must have:

- FR/TR/TEST requirements recorded before construction.
- Structured acceptance criteria for the changed requirements.
- Traceability mappings from FR to TR and TEST coverage.
- A named red state, green criteria, and exact validation command.
- Evidence receipts for hardware or lab claims.

The canonical full process text remains `F:\GitHub\McpServer\docs\Development-Process-draft-v4.md`; this local file pins the MouseKeyProxy enforcement subset used by `AGENTS.md` and compliance tests.
