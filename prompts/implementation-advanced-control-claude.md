Process `AGENTS-README-FIRST.yaml` and follow all procedures defined within.

# MouseKeyProxy implementation slice: advanced control service seam

You are Claude. Per project delegation rules, Claude owns product implementation code. Codex has already added the red tests and will validate your output.

## Hard constraints

- Use PowerShell.MCP for shell/file commands if that surface is available to you. Do not use ad hoc shell bypasses.
- Do not use Moq. Moq is banned. Use NSubstitute only in tests if you need test edits, but this slice should not require changing Codex's new tests.
- Do not edit `docs/todo.yaml`.
- Do not revert unrelated local changes. The worktree is intentionally dirty from the Fresh sunset migration.
- Keep scope focused on compiling and passing the new advanced-control tests plus existing service tests.
- Do not claim the two-machine lab is complete. This slice is the service/control seam required before live paired control can be proven.

## Current red test

Codex added:

`tests/MouseKeyProxy.Service.Tests/AdvancedControlServiceTests.cs`

The current failure is expected:

```text
AdvancedControlServiceTests.cs(88,49): error CS0246: The type or namespace name 'IRemoteDesktopController' could not be found
```

## Required implementation

Add production code so the new tests compile and pass:

1. Add common advanced-control seam types, preferably in `src/MouseKeyProxy.Common/Seams.cs` or a small adjacent file:
   - `IRemoteDesktopController`
   - `RemoteControlResult`
   - `RemoteWindowNode`

2. Expected API from the tests:

```csharp
public interface IRemoteDesktopController
{
    RemoteControlResult SetMousePosition(string displayId, int x, int y);
    IReadOnlyList<RemoteWindowNode> LocateProcess(string processName, uint pid);
    RemoteControlResult SetFocusByHwnd(ulong hwnd, bool bringToFront);
}

public readonly record struct RemoteControlResult(bool Ok, string ErrorCode, string Message)
{
    public static RemoteControlResult Success(string message = "ok") => new(true, "0", message);
    public static RemoteControlResult Failure(string errorCode, string message) => new(false, errorCode, message);
}

public sealed record RemoteWindowNode(ulong Hwnd, string Title, string ClassName, uint ProcessId, IReadOnlyList<RemoteWindowNode> Children);
```

You may adjust exact implementation details only if `AdvancedControlServiceTests` still passes without weakening the test.

3. Update `src/MouseKeyProxy.Service/MouseKeyProxyImpl.cs`:
   - Add optional constructor parameter `IRemoteDesktopController? desktopController = null` after `SessionFrameDispatcher? dispatcher = null` so existing tests continue to compile.
   - `SetMousePosition` must call `_desktopController.SetMousePosition(request.DisplayId, request.X, request.Y)` when a controller exists.
   - `LocateProcess` must call `_desktopController.LocateProcess(request.ProcessName, request.Pid)`, convert each `RemoteWindowNode` recursively to `HwndNode`, and set `ErrorCode = "0"` on success.
   - `SetFocusByHwnd` must call `_desktopController.SetFocusByHwnd(request.Hwnd, request.BringToFront)` when a controller exists.
   - Map `RemoteControlResult` to `CommandResult`: `Ok`, `Err = ErrorCode`, `Msg = Message`.
   - If no controller is configured, fail observably with `Ok = false`, `Err = "AGENT_IPC_UNAVAILABLE"`, and a clear message. Do not return success without an effect.
   - Keep existing `InjectInput` dispatcher behavior intact unless needed for compilation.
   - Keep ILogger logging with structured properties.

4. Register a default controller in `src/MouseKeyProxy.Service/Program.cs` if needed. If there is no real agent IPC yet, register a controller that fails observably with `AGENT_IPC_UNAVAILABLE` rather than pretending success. Name it clearly, for example `UnavailableRemoteDesktopController`.

5. Add implementation files where appropriate. Keep ownership boundaries clear: this service slice should introduce a seam for user-session control, not bake low-level global hooks into the service as the final design.

## Validation commands

Run these before stopping:

```powershell
dotnet test tests\MouseKeyProxy.Service.Tests\MouseKeyProxy.Service.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~AdvancedControlServiceTests"
dotnet test tests\MouseKeyProxy.Service.Tests\MouseKeyProxy.Service.Tests.csproj --no-restore -v minimal
```

Also run a source/package scan for Moq if you touch tests or project files.

## Output

Leave a concise implementation summary and exact validation results in your final response. Do not stage, commit, or push.

## Paired-control receipt contract

The Codex validation harness now rejects a lab receipt that only proves gRPC reachability. The implementation/orchestration path must produce these lines in `docs/receipts-transition-e2e.txt` before `SMOKE: PASS` can be accepted:

```text
PAIRING: PASS local=payton-legion2 remote=payton-desktop
CURSOR_CONTROL: PASS from=payton-legion2 to=payton-desktop display=<display> x=<x> y=<y>
SENTINEL_INPUT: PASS from=payton-legion2 to=payton-desktop text=MKP-CONTROL-PROOF target=<target>
SMOKE: PASS
```

`REMOTE: SKIPPED`, `SMOKE: PARTIAL`, `CURSOR_CONTROL: SKIPPED`, and `SENTINEL_INPUT: SKIPPED` are forbidden.

Codex added `scripts/assert-paired-control-proof.ps1` and wired it into `scripts/run-transition-e2e.ps1` and `scripts/verify-goal.ps1`. Your implementation should make those gates pass through real paired control, not by printing fake proof lines.

