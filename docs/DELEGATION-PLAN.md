# Delegation Plan: Fresh MouseKeyProxy Implementation via Codex + Claude CLIs

## Context and Goal
The previous implementation of MouseKeyProxy (detailed in historical PLAN-MKP-00x and post-mortems) suffered from:
- Incomplete visibility to harness (F: vs C: worktree, missing git diff src/ headers, scratch hygiene violations).
- Plan vs actual gaps (missing wireframes, assets, Nuke, full tests, MCP artifacts, tray UI fidelity).
- Trimming of details instead of preserving every requirement.
- Placeholder code in REPL instead of real self-contained payload + scheduled task logic.
- Violation of Byrd v4 (tests not always first/red-green, incomplete receipts).
- One-iteration assumption without front-loading Visibility Gate and artifact contracts.

User now wants a **delegation plan** for a completely new, clean implementation:
- Create a **new folder under f:\github\**
- Delegate **test writing and execution to Codex CLI with xhigh effort**
- Delegate **code implementation to Claude CLI using Opus model at ultracode effort**
- Goal: succeed on first pass by using specialized high/max-effort agents for their strengths (Codex for exhaustive tests + execution; Claude Opus for high-quality, thorough code).

This is a fresh start. The delegation plan describes the process, invocation, coordination, and enforcement of all standing rules (Byrd, profile, accuracy, receipts, no em-dashes, etc.).

**Project to implement**: MouseKeyProxy (free hotkey-only alternative to PowerToys Mouse Without Borders for two Win11 systems), using the core requirements from prior planning but executed via delegation.

New folder: `f:\github\MouseKeyProxy-Fresh` (clean; previous f: work if any will be left as-is or user-managed; no reliance on C: worktree unless explicitly required for harness in this plan).

## Delegation Roles (Strict Split)
- **Codex CLI (xhigh effort)**: 
  - Primary responsibility: Write tests (xUnit v3 + NSubstitute only), execute tests, verify red/green states, produce exhaustive test cases, edge cases, receipts.
  - Effort: "xhigh" – use --effort xhigh (or equivalent -c config) + prompts emphasizing "maximum thoroughness, many cases, full receipts, run to completion, report exact output and file changes".
  - Never implement production code logic unless explicitly for test harness.
  - Output: failing tests first (red), then after impl handoff, passing (green) with full run logs.
  - **WSMan remote deployment for cross-machine pairing tests (PAYTON-DESKTOP <-> PAYTON-LAPTOP)**: When writing integration/E2E tests or verification harnesses for pairing, service install, or tray, Codex must include and test WSMan remoting to deploy to PAYTON-DESKTOP from PAYTON-LAPTOP (or vice versa). 
    - Credentials: stored as secure XML in `$env:USERPROFILE\.creds\` (e.g. `payton-desktop.xml` created via `Get-Credential | Export-Clixml`). Load with `Import-Clixml "$env:USERPROFILE\.creds\payton-desktop.xml"`.
    - Setup: `Set-Item WSMan:\localhost\Client\TrustedHosts -Value "PAYTON-DESKTOP" -Force`; use `-SessionOption (New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck)` if needed for self-signed.
    - Deployment flow to test: **Must delete artifacts from first attempt on both machines before proceeding.** On PAYTON-DESKTOP and PAYTON-LAPTOP: uninstall the mkp tool if present (`dotnet tool uninstall --global MouseKeyProxy.Repl`), `sc delete MouseKeyProxy` if exists, `schtasks /delete /tn MouseKeyProxyTray /f`, remove directories (`Remove-Item -Recurse -Force $env:ProgramData\MouseKeyProxy`, `%LOCALAPPDATA%\MouseKeyProxy`, etc.), clean any leftover nupkgs/payloads. Then: `New-PSSession -ComputerName PAYTON-DESKTOP -Credential $cred`, then `Copy-Item -ToSession $session -Path <local nupkg or publish dir> -Destination <remote path>`, `Invoke-Command -Session $session -ScriptBlock { dotnet tool install --global ... or sc create / scheduled task setup }`, then run REPL pairing commands and assert success.
    - Include tests that perform full deploy + pair + verify (e.g. clipboard or input proxy works after deploy). Always capture full remoting output as receipts. Use existing patterns from prior work ( ~/.creds + TrustedHosts + PSSession for service/tray deploy).

- **Claude CLI (Opus model + ultracode effort)**:
  - Primary responsibility: Implement the code (following all specs, seams, Byrd, full details from plan).
  - Model: --model opus (or full alias for latest Opus-class)
  - Effort: ultracode ( --effort ultracode ). Orchestrator uses `grok-build` with highest effort for coordination, planning, and review tasks where applicable (instead of direct invocation).
  - Prompts must reference the full plan, wireframes, proto, ACs, and "preserve every detail, correct only mistakes, no trimming".
  - Use for implementation slices only after tests are written and red by Codex.

- **Orchestrator (Grok / this session)**: 
  - Maintains this plan and high-level Byrd phases.
  - Breaks work into small gated slices.
  - Invokes the CLIs via terminal (full paths or linked codex/claude).
  - Verifies receipts, runs visibility/verify if needed, coordinates handoffs (e.g. "tests written by codex, now hand to claude").
  - Enforces all rules: accuracy (verify from source files), bring receipts (command output + on-disk), approve before execute (this plan + sub-plans), no em-dashes.
  - Uses PowerShell (pwsh) for all local commands.
  - MCP for TODOs/sessions/reqs (never direct yaml).
  - Azure DevOps origin if pushing, but github as per prior.

High/Max effort means: agents are instructed to spend maximum reasoning, produce verbose receipts, iterate internally until solid, surface observations vs inferences, concede issues immediately.

## New Folder Setup (First Step - Non-Delegated or lightly delegated)
1. Create and init (use a clean "new" folder such as f:\github\MouseKeyProxy-Fresh to ensure no prior state pollution):
   ```
   mkdir -p f:\github\MouseKeyProxy-Fresh
   cd f:\github\MouseKeyProxy-Fresh
   git init
   git remote add origin https://github.com/sharpninja/MouseKeyProxy.git   # or appropriate
   ```
   (Update all prompts and this plan references to the actual folder used. In practice, the -Fresh suffix was used for a truly clean start while keeping the logical name MouseKeyProxy for the project.)
2. MCP workspace (director / grok plugin):
   - Use director add-workspace or equivalent full_bootstrap + client.Workspace.InitAsync
   - Scaffold AGENTS-README-FIRST.yaml , docs/todo.yaml (via MCP only)
   - Ingest requirements (FRs + ACs + tests) via mcpserver tools or grok-plugin if available.
3. Skeleton (or delegate lightly):
   - dotnet new globaljson --sdk-version 10.0.x
   - Other basics per prior plan (Directory.Build.props, slnx, etc.)
   - Copy/adapt critical non-code from historical plan only as reference; do not execute yet.
4. Create `prompts/` directory for all agent prompts (as files):
   - `mkdir prompts`
   - All Codex and Claude prompts must be written here as .md files and submitted via file redirection (see CLI patterns).
5. WSMan credentials preparation (for later Codex remote deploy tests):
   - Ensure creds exist at `$env:USERPROFILE\.creds\payton-desktop.xml` (and laptop equivalent if bidirectional): created once via `Get-Credential | Export-Clixml "$env:USERPROFILE\.creds\payton-desktop.xml"`.
   - Test connectivity manually once: load with Import-Clixml, set TrustedHosts, New-PSSession to PAYTON-DESKTOP.
   - Include in all relevant Codex prompts and tests the exact load + session creation code.

All setup changes must produce visible git status/diff if harness requires (document exact commands for receipts).

## Byrd Process Enforcement in Delegation
Follow Byrd v4 strictly, mapped to delegation:

- **Inception**: This plan + requirements (full ACs with test outlines). Codex produces initial red test skeletons for key FRs as "documented red". All "green" as planned.

- **Elaboration**: Codex writes full red tests for core risks (hooks, gRPC, LIFO, visibility harness, REPL contract). Validates mocks/fakes. Claude prototypes impls only on approved slices.

- **Construction (slices)**: 
  - Per slice: Codex first: write failing tests (xhigh effort, exhaustive, run to show red + receipts).
  - Handoff (via files, git, or explicit prompt): "Tests in [paths] are red. Implement to green using ultracode effort."
  - Claude: implement (ultracode effort Opus, full fidelity to specs, seams, no shortcuts).
  - Codex: re-execute tests, confirm green, produce coverage/receipts.
  - Only after 100% green + receipts + Visibility Gate (if applicable) move to next slice.
  - Small slices: e.g. 1: hotkey + basic toggle + ownership. 2: input matrix + Send/Inject. 3: LIFO clipboard + persist. 4: gRPC advanced + Locate/SetFocus. 5: REPL full contract + self-contained payloads + scheduled task. 6: Tray UI matching wireframes + logo. 7: Nuke + Integration + full gates. 8: Service + end-to-end.

  **Nuke local tool publish target (create + test by Codex, implement by Claude)**: In slice 7 (or dedicated), Codex must write red tests (xhigh) that exercise a Nuke target `PublishToolLocally` (or similar name in build/Build.cs). The target must:
  - Build/pack the Repl project as a local nuget package (using dotnet pack with --output to a local feed dir).
  - Support local install test: `dotnet tool install --global --add-source <local-dir> MouseKeyProxy.Repl --version <local>`.
  - Verify the installed `mkp` runs and basic commands work (e.g. `mkp --help`).
  - Tests must actually run the Nuke target (via `build.exe` or `dotnet run --project build/Build.cs -- PublishToolLocally`) and assert the nupkg exists locally and tool installs/runs.
  Claude implements the target in Nuke (using Nuke's DotNetPack, etc., with proper configuration for the Repl project). Full receipts required (nupkg hash, install output, tool version).

  **Final release (after all green)**: 
  - Orchestrator (or delegated): `git add -A && git commit -m "Release v0.5.0 - initial delegation success with full tests and impl"` (or appropriate).
  - `git tag v0.5.0`
  - `git push origin master --tags`
  - Publish the tool: use the Nuke target or direct `dotnet nuget push <nupkg-from-pack> -k $env:NUGET_API_KEY -s https://api.nuget.org/v3/index.json --skip-duplicate`.
  - Codex writes tests that validate the publish flow locally (e.g. pack, then local feed install as above) before the real publish. Write the Codex prompt for this to `prompts/codex-nuke-publish-test.md` and submit as file.
  - Claude ensures the Nuke target supports a `PublishToNuGet` that uses the env var key safely (never hardcode key). Write the Claude prompt to `prompts/claude-nuke-publish-impl.md` and submit as file.

- **Transition**: Full 2-machine verification with receipts, docs, release.

Every exit gate requires entire current+prior suite green + receipts. No skipping.

## Exact CLI Invocation Patterns (with effort)
Use full paths or PATH-linked for reliability (from inspection: codex via WinGet Links, claude via package).

Example for non-interactive (preferred for delegation):

**Codex for tests (xhigh effort)**:
Write the full detailed prompt to a markdown file (e.g. `prompts/codex-tests-slice-N.md`), then submit as file:
```
codex exec --effort xhigh -c model="..." < prompts/codex-tests-slice-N.md
```
- Or use `codex exec [PROMPT]` with config override for effort.
- Follow with similar for re-run.
- Always write prompts to .md files in the `prompts/` directory of the new folder and submit the file (never inline long strings in orchestrator commands).

**Claude for implementation (opus + ultracode effort)**:
Write the full detailed prompt to a markdown file (e.g. `prompts/claude-impl-slice-N.md`), then submit as file:
```
claude --model opus --effort ultracode -p < prompts/claude-impl-slice-N.md
```
- Add --append-system-prompt for extra rules (no em-dashes, receipts, etc.).
- For background/long: --background if supported.
- Always write prompts to .md files in the `prompts/` directory of the new folder and submit the file.

Orchestrator runs these via pwsh `& "full\path\to\codex.exe" ...` or `& codex ...` and captures full output + exit code as receipts.

Use --allowed-tools or sandbox as needed for safety.

After each delegation, orchestrator:
- Verifies receipts (grep files, run tests locally if needed, git status).
- Updates plan/todo with conclusions.
- Only proceeds on explicit "approve" logic (in this case, user or internal gate).

## New Folder + Visibility / Harness Handling
- Folder: f:\github\MouseKeyProxy-Fresh (clean start).
- Git: init + remote. After changes: always capture `git status --porcelain 'src/'` and `git diff --name-only 'src/'` + header strings for receipts.
- If harness requires C: visibility (from prior lessons): document and perform worktree/de-nest + copy skeleton to C: visibility tree when running verify-goal.ps1 or final claims. Do not assume F: is visible.
- Scratch hygiene: after any verify run, exactly the 4 files.
- Use scripts/verify-goal.ps1 (adapt from prior) as sole entry for full test+pack runs.

## Key Specs to Feed Agents (Preserve Full Detail)
Use the full historical plan content (proto, FR-MKP-001 to 006 with ACs, wireframes 01-04 exact, REPL contract with self-contained + schtasks, Identity table, support matrix, etc.) as the source of truth. Feed excerpts or full paths to agents.

Critical "first time success" elements (must be in every relevant prompt):
- Visibility Gate: de-nested worktree (handle F:), dirty src/, explicit diff headers, exactly 4 scratch files.
- REPL: bundles self-contained payloads (Nuke publish), DoService* copies + ACLs + sc + netsh + scheduled task "MouseKeyProxyTray" (ONLOGON user) for visible tray.
- Tray: exact wireframe menu + forms + custom logo icon, invokes shared lib.
- Nuke: build.cs for payloads, pack, publish, verify.
- Tests: all original + new (009 Nuke/payloads, 010 wireframes/UI, 011 MCP, 012 visibility/harness).
- Error paths: real attempt then null-client Bidi fallback for shipped emission/SentFrames.
- MCP: only via interfaces for TODOs/sessions.
- Byrd + receipts always.

Orchestrator maintains a clean, detailed master spec file in the new folder (e.g. docs/DELEGATION-SPEC.md containing full requirements without slimming).

## Phase Breakdown with Delegation Points
1. **Setup (orchestrator + light delegation)**: folder, git, MCP workspace, skeleton, ingest reqs. Codex can help write initial test skeletons for setup ACs.

2. **Elaboration Risks (Codex heavy first)**: Codex writes red tests for interop, gRPC, LIFO, visibility, REPL contract. Claude lightly prototypes mocks if approved.

3. **Construction Slices (strict Codex -> Claude handoff)**:
   - For each slice: Codex (xhigh) writes + runs red tests + receipts.
   - User/orchestrator approves handoff.
   - Claude (opus ultracode) implements.
   - Codex re-runs to green + receipts.
   - Orchestrator verifies against plan, updates git, runs any global verify.

4. **Full Gates + Transition**: Codex executes full verify, Claude polishes based on failures. All with max receipts (transcripts, git diffs, sc outputs, tray visibility proof, 2-machine).

## Coordination and Tooling
- Shared workspace: f:\github\MouseKeyProxy-Fresh (agents get --add-dir or appropriate).
- Handoffs via explicit files or prompts containing paths + "red tests here, implement now".
- **Prompts as files**: All prompts for Codex and Claude (including those covering WSMan deploy/pairing, Nuke targets, cleanup, publish) must be written to markdown files in a `prompts/` directory of the new folder (e.g. `prompts/codex-wsman-deploy-pairing-tests.md`, `prompts/claude-impl-repl-payloads.md`). Submit the file to the CLI (e.g. `codex exec ... < prompts/xxx.md` or `claude ... -p < prompts/xxx.md`). This ensures full reviewability, version control, and exact reproduction.
- Receipts: every CLI output captured, plus on-disk checks (ls, cat, grep, dotnet test output).
- MCP: use for tracking delegation tasks (e.g. "CODEX-TEST-001", "CLAUDE-IMPL-001").
- PowerShell wrapper scripts for repeatable invocations with effort flags.
- Git for versioning between delegations (commit after each green).
- If long running: use claude --background or codex equivalents.

## Enforcement of Rules (All Agents + Orchestrator)
- Accuracy: verify from source files in f:\github\MouseKeyProxy-Fresh , never summaries.
- Receipts: every claim backed by output + verification command.
- Approve before: no code changes without red tests + approval.
- No em/en-dashes in any generated code/comments/docs.
- Source control: git in f:; Azure if configured.
- MCP only.
- Full details preserved.
- Brutal honesty in all comms; mark obs vs inference.

## Risks Specific to Delegation
- Agent capability limits: Codex may need explicit "use xUnit v3 NSubstitute only, no Moq".
- Effort interpretation: prompts must spell out "xhigh = exhaustive cases + multiple runs + full logs"; "ultracode = multiple self-reviews + edge coverage + receipts".
- Context size: feed focused slices + full plan path.
- Visibility/harness: explicit in every prompt if relevant.
- Coordination overhead: orchestrator must be strict on handoff quality.
- Model drift: pin models/effort in commands.

## Verification of Delegation Success
- Codex produces and runs red tests first (receipts).
- Claude produces impl that makes them green (receipts).
- Full plan ACs met with machine evidence.
- Visibility gate passed on any claim (git + scratch).
- End-to-end on 2 Win11: hotkey toggle, boundaries, full proxy, LIFO clip, REPL install (payloads visible, service, scheduled tray visible), tray matches wireframes, Nuke produces artifacts.
- **WSMan deploy + pairing test (PAYTON-DESKTOP <-> PAYTON-LAPTOP)**: Codex must produce and run tests that use WSMan (as detailed in Codex role) to deploy the built/published tool + service to PAYTON-DESKTOP from the dev machine (PAYTON-LAPTOP), install/register the service/tray via the deployed REPL, then execute pairing (REPL pair/discover or equivalent) and verify successful pairing (e.g. clipboard sync or input proxy works across the machines post-deploy). **Must delete artifacts from first attempt on both machines before proceeding** (uninstall tool, sc delete, schtasks delete, remove ProgramData/LocalAppData dirs for MouseKeyProxy). Include full PSSession output, credential load from $env:USERPROFILE\.creds\, TrustedHosts setup, deploy commands, and pairing success assertions as receipts. Test both directions if applicable.
- **Nuke local publish + final NuGet publish**: Codex tests the Nuke `PublishToolLocally` target (runs it, verifies local nupkg, local tool install from feed succeeds and `mkp` runs). After full green, final steps: commit, `git tag v0.5.0`, push tags, then publish to nuget.org using `NUGET_API_KEY` env var (via Nuke target or `dotnet nuget push ... -k $env:NUGET_API_KEY ...`). Receipts: tag output, push, nuget publish log (or dry-run verification).
- All via captured transcripts + git + on-disk.

## Next Immediate Steps (After This Plan Approved)
1. Orchestrator: create the folder (e.g. MouseKeyProxy-Fresh) + basic git/MCP. Prepare WSMan creds at $env:USERPROFILE\.creds\ if not present.
2. Write/ingest detailed requirements + this delegation plan into the folder (as docs/DELEGATION-PLAN-FULL.md and supporting specs). Create `prompts/` dir.
3. Nuke target: Write prompt as `prompts/codex-nuke-local-publish-tests.md` and submit as file to Codex (xhigh) to write red tests for the local tool publish target (pack + local feed install verification). Claude implements the target in build/Build.cs (write prompt as md file).
4. First functional slice: Write prompt as `prompts/codex-first-tests.md` (include WSMan deploy + pair + delete-artifacts-first) and submit as file to Codex (xhigh) for red tests on core toggle/ownership + pairing. Must delete artifacts from first attempt on both machines before any deploy/test.
5. Handoff + Claude impl (ultracode effort, opus) – write prompt to md file first.
6. Codex re-verify green + receipts (using file-submitted prompts where applicable).
7. After all slices green: final commit + git tag v0.5.0, push tags, then NuGet publish using NUGET_API_KEY (Codex has tested the flow locally via its prompt file).
8. Verify, receipt, repeat for subsequent slices. Always enforce Visibility Gate (git + scratch) and full receipts before claims.

This delegation plan ensures specialization, high effort, preservation of detail, and strict process to achieve first-try success.

(Full original project specs from prior planning are to be referenced/preserved in the new folder's docs/ as the source of truth for what to implement.)
