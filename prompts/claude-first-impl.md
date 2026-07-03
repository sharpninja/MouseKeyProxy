ultracode effort (highest quality). Read docs/DELEGATION-PLAN.md and docs/KEY-SPECS.md.

Codex has written red tests (see the files it created in tests/ from xhigh run):

- tests/MouseKeyProxy.Common.Tests/HotkeyToggleStateMachineTests.cs
- tests/MouseKeyProxy.Common.Tests/OwnershipPolicyTests.cs
- tests/MouseKeyProxy.Agent.Tests/HotkeyToggleControllerTests.cs
- tests/MouseKeyProxy.Agent.Tests/AgentOwnershipBoundaryTests.cs

The tests currently fail to compile because the production types (seams, state machine, ownership policy, controller, etc.) do not exist.

Task:
- Implement the minimal production code in src/ to make the tests compile and run as red (failing assertions as per the test names and cases).
- Define seams in Common (IHotkeyMonitor, IInputInjector, ICursorClip, etc.).
- Implement HotkeyToggleStateMachine in Common.
- Implement HotkeyToggleController and ownership logic in Agent.
- Follow the plan: Agent owns all input, Service does not attempt.
- Hotkey only, no auto edge.
- After changes, run dotnet test on the two test projects and show the red output.
- Provide full receipts: files created, diffs, compile output, test output.
- Do not over-implement; this is the first slice only.
- Write all prompts as files (this is already a prompt file).

Be thorough. Preserve every detail. Red state only for this step.