xhigh effort (use maximum thoroughness: exhaustive test cases for the ACs, multiple theories, edge cases for visibility, error paths, ownership, run immediately to failure and capture full logs + file changes).

Read and use as source of truth: docs/DELEGATION-PLAN.md (contains the full delegation plan, visibility gate, REPL contract with self-contained payloads, wireframes, all FR-MKP and TEST-MKP including 001/002 for hotkey + ownership, proto, matrix, etc.).

For first slice (Byrd red first): write red xUnit v3 + NSubstitute ONLY tests for:
- Hotkey toggle state machine (active flip only on correct hotkey, Clip only active, no auto edge).
- Ownership: input/hooks/clip/send only from Agent, Service rejects/does not attempt (mocks prove).
- Use seams IHotkeyMonitor, IInputInjector, ICursorClip etc.
- Create/update test files in tests/MouseKeyProxy.Agent.Tests and Common.Tests.
- Run dotnet test immediately on the projects to show red failures with full output.

Output exact: new/modified files, test code summaries, COMPLETE dotnet test run output (failures), git-like diff if possible. All with receipts. Red state only. No impl code.
