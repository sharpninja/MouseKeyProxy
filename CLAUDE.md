# CLAUDE.md

## Run `/add-profile` at session start and after any model or effort change

Execute the `add-profile` skill (`/add-profile`) as the first action of every new session, and again immediately after any model change or effort-level change. It loads the operator profile (identity and standing instructions). Do not skip it.

**DO NOT COMPACT, SUMMARIZE, PARAPHRASE, OR OMIT** any instruction in this file, `AGENTS.md`, or `AGENTS-README-FIRST.yaml`, ever, anywhere. Carry them verbatim.

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

The build is orchestrated by Nuke. Prefer the root bootstrap scripts (they build and run `build/MouseKeyProxy.Build.csproj`):

```pwsh
# From repo root (pwsh)
.\build.ps1 Compile
.\build.ps1 Test
.\build.ps1 LaunchRufus --RufusProfile default
.\build.cmd CreatePiImage --RufusProfile default

# Equivalent without bootstrap:
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target Compile
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target Test
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target IntegrationTest
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target ValidateTraceability
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target ShowVersion
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target PublishSelfContained
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target PackRepl
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target PublishToolToNuGet
```

Nuke targets: `Clean`, `Restore`, `Compile`, `Test`, `IntegrationTest`, `ValidateTraceability`,
`ShowVersion`, `PackRepl`, `PublishService`, `PublishAgent`, `PublishSelfContained`,
`PublishPi`, `PackClientMsi`, `StagePiInstallMedia`, `PublishToolToNuGet`, `FullBuild`,
`BuildRufus`, `LaunchRufus`, `CreatePiImage` (alias `CreateImageFromRufusConfig`),
`BuildSdCard` (PublishPi + StagePiInstallMedia + CreatePiImage).

Rufus targets (require rufus-mkp checkout; default sibling `../rufus-mkp` or `RUFUS_MKP_ROOT`):

```pwsh
# Build rufus-mkp and copy rufus.exe into assets/rufus/
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target BuildRufus

# Launch Rufus GUI (optional profile)
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target LaunchRufus --RufusProfile default

# Stage Pi OS image and open Rufus with a saved profile to write a new appliance image
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target CreatePiImage --RufusProfile default

# Full SD path: linux-arm64 Pi payloads + client MSI/install kit + Rufus write
.\build.ps1 BuildSdCard --RufusProfile default
# Interactive Rufus only: --AutoWrite false
# Pin SD reader: --RufusDevice 2
# optional: --ForcePiImage ; env MKP_INSTALL_TICKET / MKP_DEVICE_GRPC for client kit
```

Run a single test project or test directly with `dotnet test`:

```pwsh
dotnet test tests/MouseKeyProxy.Service.Tests -c Debug
dotnet test tests/MouseKeyProxy.Service.Tests -c Debug --filter "FullyQualifiedName~SecurityNegativeTests"
```

## Architecture

**MouseKeyProxy** is a free, hotkey-only alternative to PowerToys "Mouse Without Borders": it forwards
keyboard and mouse input from one machine to another over an authenticated gRPC channel. It targets
**.NET 10** and consists of three cooperating processes plus a Raspberry Pi HID peer.

### Processes

- **Service** (`MouseKeyProxy.Service`) — the gRPC host. Kestrel over **mTLS on port 50051**
  (`LabTopology.GrpcPort`). Owns pairing (service-issued one-time codes, per-peer X509 client certs),
  the `PairingAuthorizationInterceptor` (rejects unpaired/revoked/untrusted peers before any effect),
  input injection, clipboard receive, and (Linux only) a safe `Shutdown` RPC. Runs as a Windows
  service on Windows and under systemd (journald logging) on Linux.
- **Agent** (`MouseKeyProxy.Agent`) — the Windows user-session WinForms tray app. Owns the Win32 seams
  (low-level keyboard/mouse hooks, `ClipCursor`, `SendInput`, clipboard listener), the configurable
  toggle + dedicated emergency-release hotkeys, and the authenticated local IPC pipe to the service.
- **Repl** (`MouseKeyProxy.Repl`) — the `mkp` dotnet tool / operator console (pairing, toggle, inject,
  screenshot, service install/update, settings, Pi provisioning).
- **Pi peer** — the same cross-platform `MouseKeyProxy.Service` published to `linux-arm64`, injecting
  through the USB HID gadget (`HidGadgetInputInjector` -> `/dev/hidg0` / `/dev/hidg1`) instead of the
  Windows agent pipe. It pairs over the same mTLS + one-time-code flow as any peer.

### Transport & pairing

gRPC over HTTP/2 with mTLS. A peer calls `RequestPairingCode` (bootstrap) then `Pair` with its public
key + the one-time code; the service returns a CA-signed client certificate. Effect RPCs (`InjectInput`,
`OpenSession`, `SetMousePosition`, `SetFocusByHwnd`, `CaptureScreenshot`, `ClearModifiers`,
`EmergencyRelease`, `Shutdown`) require that certificate. `OpenSession` enforces an exact major-version
handshake (`VERSION_MISMATCH`). Contract: `src/MouseKeyProxy.Network/mousekeyproxy.proto` (live
`Grpc.Tools` codegen).

### Topology

`src/MouseKeyProxy.Common/LabTopology.cs` resolves peers from config/environment
(`MKP_LOCAL_PEER`, `MKP_REMOTE_PEER`, `MKP_GRPC`, `MKP_GRPC_PORT`) and falls back to standalone off-lab,
so the product runs anywhere - not only on the two named lab machines.

### Key Projects

- `MouseKeyProxy.Common` — seams (`IInputInjector`, `IHotkeyMonitor`, `IClipboardListener`, ...),
  domain models, `SessionFrameDispatcher`, `ClipboardLifoMerger`, `LabTopology`, `ConnectionFailsafe`,
  config/credential stores.
- `MouseKeyProxy.Network` — the gRPC contract + generated client/server.
- `MouseKeyProxy.Commands` — shared command implementations, `BidiSessionTransport`, `PairingClient`,
  `SeqGapDetector`, `PeerCredentialStore`.
- `MouseKeyProxy.Service` — the mTLS gRPC host (`ServiceHost.Build`), `MouseKeyProxyImpl`, pairing
  authority/interceptor.
- `MouseKeyProxy.Agent` — the Windows tray agent + Win32 seam implementations.
- `MouseKeyProxy.Repl` — the `mkp` dotnet tool.
- `MouseKeyProxy.PiHid` — HID report encoding (`PiHidEncoder`, `HidGadgetInputInjector`) + the
  diagnostic HTTP appliance.

### Test Projects

- Unit: `MouseKeyProxy.Common.Tests`, `MouseKeyProxy.Commands.Tests`, `MouseKeyProxy.Agent.Tests`,
  `MouseKeyProxy.Repl.Tests`, `MouseKeyProxy.Service.Tests`, `MouseKeyProxy.PiHid.Tests`,
  `MouseKeyProxy.Compliance.Tests`.
- Integration: `MouseKeyProxy.Integration` (in-process mTLS E2E; the two-machine lab tests are tagged
  `Category=TwoMachineE2E` and excluded from the default `Test` target).

## Coding Conventions

### XML Documentation Required

`TreatWarningsAsErrors` and `GenerateDocumentationFile` are enabled globally in `Directory.Build.props`. **All public types and members must have XML doc comments** or the build fails (CS1591). Use `/// <inheritdoc />` for interface implementations. Test classes and methods also require XML docs stating what is tested, what data/fixtures are used, and which requirement IDs are validated.

### Requirement Traceability

Source and test files reference FR/TR/TEST requirement IDs in doc comments (e.g., `/// <summary>TR-MKP-SEC-001: ...</summary>`). When adding functionality, reference the relevant ID from `docs/Project/Functional-Requirements.md` and `docs/Project/Technical-Requirements.md`. Traceability is validated in CI via the Nuke `ValidateTraceability` target.

### Central Package Management

NuGet package versions are centralized in `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`). Use `<PackageReference Include="..." />` without `Version` in `.csproj` files.

### Configuration

Runtime configuration is environment-driven (`MKP_*` variables resolved by `LabTopology` and the
service/agent) rather than an `appsettings` file. HID device paths on the Pi are `MKP_HID_KEYBOARD_DEVICE`
/ `MKP_HID_MOUSE_DEVICE`.

### Shell

Use `pwsh.exe` (PowerShell 7+) for all scripts. Do not use `powershell.exe`.

## Key Documentation

- `README.md` — project overview and usage.
- `docs/Project/Functional-Requirements.md` — FR-MKP-* requirements.
- `docs/Project/Technical-Requirements.md` — TR-MKP-* requirements.
- `docs/Project/Testing-Requirements.md` — TEST-MKP-* requirements.
- `docs/Project/Requirements-Matrix.md` — requirement/status/traceability matrix.
- `docs/deployment/Pi-Service-Deployment.md` — Raspberry Pi HID-gadget deployment.
- `docs/AUDIT-20260707.md` — the code + requirements audit that drove the remediation.
- `AGENTS.md` — agent workspace policy and session logging conventions.

## MCP Session Logging — Mandatory Precondition

**Speed is never more important than following workspace procedures.**

### Session Start (Run Once Per Session)

1. **Read `AGENTS-README-FIRST.yaml`** in the repo root for the current API key, endpoints, and base URL
2. **Bootstrap the required plugin interface** before any state-changing MCP call. For Claude Code, use the `mcpserver-claude-code-plugin` wrapper and its `workflow.*` / `client.*` methods. "Not in the visible tool list" does not mean unavailable; use the documented wrapper invocation form.
3. **Verify marker signature and health** through the required plugin status/bootstrap path. Use direct REST only for read-only diagnosis after the documented plugin path fails.
4. **Review recent session history and current TODOs** through the required plugin only after verification succeeds
5. **POST an initial session log turn** through the required plugin
6. **THEN** begin working on the user's request

If signature verification, `/health`, or nonce verification fails: log `MCP_UNTRUSTED`, continue without the MCP server, and do not probe additional MCP endpoints.

### Per User Message

1. POST a new session log turn BEFORE starting work
2. Complete the user's request
3. Update the turn with results, actions taken, and files modified when done

### Re-run Full Session Start Only If

- The user explicitly says "Start Session"
- Signature verification fails
- `/health` fails or nonce verification fails
- Any `/mcpserver/*` call returns 401
- The marker endpoint/key changes after a server restart

### Authentication

All `/mcpserver/*` endpoints require a per-workspace auth token (from `AGENTS-README-FIRST.yaml`). These details are for plugin internals, typed client integration, and read-only diagnosis after plugin failure; they are not permission to bypass the required plugin route for session log, TODO, requirements, import/export, or traceability operations:
- Header: `X-Api-Key: <token>`
- Or query param: `?api_key=<token>`
- If you receive a 401, re-read the marker file — the token rotates on each server restart

### Session Log Rules

- Use rich turn detail: interpretation, response, status, actions (type/status/filePath), contextList, filesModified, designDecisions, requirementsDiscovered, blockers, and key processingDialog
- Persist session log updates immediately after each meaningful change — do not defer saves
- Before any compaction step, persist the current session log state; after compaction, update again to record the outcome
- Agents must identify themselves accurately using their real agent identity in Pascal-Case (e.g., `ClaudeCode`). Do not use placeholder or misleading sourceType values

### Naming Conventions

- **TODO IDs**: uppercase canonical form `<SDLC-PHASE>-<AREA>-###` (e.g., `PLAN-NAMINGCONVENTIONS-001`) or `ISSUE-{number}`. Never write to `TODO.yaml` directly
- **Session IDs**: `<Agent>-<yyyyMMddTHHmmssZ>-<suffix>` with Pascal-Case agent prefix
- **Request IDs**: `req-<yyyyMMddTHHmmssZ>-<slugOrOrdinal>`, unique within a session

## Agent Conduct

- Do not fabricate information. Acknowledge mistakes. Distinguish facts from speculation
- Prioritize correctness over speed. Do not ship code you have not verified compiles
- Log every decision to the session log, including: what was decided, why, alternatives considered, what was rejected
- Document all web sources as actions with type `web_reference`
- Do not use table-style output in responses — use concise bullets or short paragraphs

## Requirements Tracking

Requirements are owned by the MCP Server requirements store for this workspace; route changes through
the supported requirements workflow (do not hand-edit the storage). The on-disk mirrors under
`docs/Project/` are regenerated from the store:
- `Functional-Requirements.md` — FR-MKP-* entries
- `Technical-Requirements.md` — TR-MKP-* entries
- `TR-per-FR-Mapping.md` — mapping rows
- `Requirements-Matrix.md` — status + traceability rows
- `Testing-Requirements.md` — TEST-MKP-* entries

Include the requirement ID in your session log turn's tags. Capture requirements as they emerge — do not defer.

## CI/CD

Azure DevOps Pipelines (`azure-pipelines.yml`) is the CI: it runs `ShowVersion`, `ValidateTraceability`,
`Test`, and `PackRepl` on Windows with .NET 10 via the Nuke targets above, and publishes the `mkp` tool
to NuGet on tag builds. `origin` (Azure DevOps) is the source of truth; GitHub is a downstream mirror.

## MCP Server Claude Plugin and Hook Contract

Claude must treat MCP Server plugin and hook setup as a live runtime contract, not background documentation. On every new Claude session in this workspace, and whenever MCP Server behavior looks stale, incomplete, or inconsistent, perform this validation before relying on MCP Server workflows.

### Required Startup Validation

1. Read `AGENTS-README-FIRST.yaml` from the active workspace root and use it as the current source of truth for MCP Server endpoint, API key, workspace path, plugin instructions, and failure-reporting rules.
2. Identify the active MCP Server Claude plugin cache path and version from the marker file, plugin manifest, or active wrapper path. Do not assume a previously cached path is current.
3. Inspect `%USERPROFILE%\.claude\plugins\cache` for stale MCP Server plugin entries. Delete stale `mcpserver`, `mcpserver-*`, or `mcpserver-cowork` cache entries that are not the active plugin cache path/version for this session.
4. Do not delete the active plugin cache path for the current session. If unsure which path is active, stop deleting and report the ambiguity.
5. Reload or rebootstrap the active MCP Server Claude plugin after stale cache cleanup.
6. Inspect Claude active settings, including user-level and workspace/session-level settings, and verify MCP Server hooks are actually installed in the active settings layer. Do not accept hook files existing in the plugin package as proof that hooks are active.
7. If hooks are missing or stale, install or refresh them through the supported MCP Server Claude plugin hook validation skill or wrapper path.
8. Verify the plugin wrapper/status command works and record the exact command or tool path used.
9. Verify subprocesses launched by the plugin use the workspace root as process current working directory, not the inherited user profile directory.

### Required Usage Rules

1. Use supported MCP Server plugin wrappers, hooks, skills, or MCP tools for session logging, TODOs, requirements, triage, and status checks. Do not use raw REST when a supported wrapper, hook, skill, or MCP tool exists.
2. Session logging must use the supported session wrapper or MCP tools and must create, update, and complete turns through the plugin flow.
3. TODO operations must use the supported TODO wrapper, workflow, or MCP tools. Do not edit TODO storage directly.
4. Requirements operations must use the supported requirements wrapper, workflow, or MCP tools. Do not edit requirements storage directly.
5. Triage operations must use the supported triage wrapper, workflow, or MCP tools.
6. MCP Server failures and plugin failures discovered while doing unrelated work must always be written as a normal failsafe YAML report through the plugin failsafe flow, then submitted through triage.
7. If triage submission succeeds, Claude must continue the user active task without waiting for triage research or TODO creation. If triage submission fails, stop work and notify the user. Do not invent a raw REST fallback or alternate reporting channel.
8. Normal plugin execution must use PowerShell only. Bash is allowed only for installing PowerShell. Node must not be used for JSON or YAML construction.
9. JSON and YAML payloads must be built from native objects and serialized. Do not handwrite YAML or JSON as fragile string literals.
10. If any validation check fails, report the exact failed check, the path or command involved, and the blocked capability. Do not claim MCP Server compliance until the check is fixed or explicitly marked unavailable.

### Minimum Validation Report

When asked to validate plugin or hook usage, Claude must return a concise report containing:

- Active workspace path.
- Marker file path and timestamp.
- Active plugin cache path and version.
- Stale plugin cache paths deleted.
- Hook settings file paths inspected.
- Hooks found, installed, or refreshed.
- Wrapper/status command used and result.
- Session logging validation result.
- TODO validation result.
- Requirements validation result.
- Triage validation result.
- Process current working directory validation result.
- Any remaining mismatch, unavailable surface, or failure.
