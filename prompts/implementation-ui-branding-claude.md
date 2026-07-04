Process `AGENTS-README-FIRST.yaml` and follow all procedures defined within.

# MouseKeyProxy implementation slice: tray dashboard UI and hacker mouse branding

You are Claude. Per project delegation rules, Claude owns product implementation code. Codex has added the UI/branding compliance tests and will validate your output.

## Hard constraints

- Use PowerShell.MCP for shell/file commands if that surface is available to you. Do not use ad hoc shell bypasses.
- Do not use Moq. Moq is banned.
- Do not edit `docs/todo.yaml`.
- Do not revert unrelated local changes. The worktree is intentionally dirty from migration/test work.
- Keep scope focused on passing `AgentUiBrandingComplianceTests` without weakening those tests.
- Do not claim two-machine paired control is complete. This slice is UI/branding only.

## Current red tests

Codex added:

`tests/MouseKeyProxy.Compliance.Tests/AgentUiBrandingComplianceTests.cs`

Expected current failures:

- `assets/logo.png` is too small and generic for the required hacker mouse workstation brand.
- `src/MouseKeyProxy.Agent/Program.cs` still contains placeholder UI text/actions such as `peer-via-repl`, `Remote A`, `Connected: (pair via REPL)`, and tray actions that only write `[TRAY]` console messages.

## Branding requirements

Use `assets/logo.branding.md` as the active brand contract. Replace `assets/logo.png` with an inspectable raster render at least 512x512 pixels, showing:

- Hacker mouse character
- Keyboard under the mouse's paws
- Desk surface
- Multiple surrounding monitors
- Active typing posture
- Technical/hacker workstation mood

The asset must remain readable at tray icon scale and documentation scale. Generic mouse-only, keyboard-only, monitor-only, abstract dot, or network-node logos are not acceptable.

## UI requirements

Update the agent UI so `Program.cs` no longer reads as a placeholder tray menu. It should expose a compact dashboard surface containing the user-facing concepts required by the tests:

- `MouseKeyProxy dashboard`
- `Pairing`
- `Active peer`
- `Service`
- `Clipboard`
- `Recent errors`
- `Emergency release`
- `Reconnect`
- `Open logs`

Remove placeholder text/actions:

- `peer-via-repl`
- `Remote A`
- `Connected: (pair via REPL)`
- `Console.WriteLine("[TRAY]...` as the implementation of tray actions

Actions may call shared command seams or explicit not-yet-connected command methods that fail observably in the UI, but they must not be inert console-only placeholders.

## Validation commands

Run these before stopping:

```powershell
dotnet test tests\MouseKeyProxy.Compliance.Tests\MouseKeyProxy.Compliance.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~AgentUiBrandingComplianceTests"
dotnet test tests\MouseKeyProxy.Compliance.Tests\MouseKeyProxy.Compliance.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~TestDoubleComplianceTests"
git diff --check
```

Do not stage, commit, or push.
