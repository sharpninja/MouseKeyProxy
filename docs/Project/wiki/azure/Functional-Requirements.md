# Functional Requirements (MCP Server)

## FR-HOTKEY-001 Hotkey toggle contracts

The system must expose hotkey monitor, cursor clip, ownership, toggle state, and input injection contracts before behavior is implemented.

Acceptance Criteria:
- Compile-time contracts exist for hotkey events, toggle state, cursor clipping, and input injection seams.
- Red tests assert desired hotkey toggle behavior before production logic satisfies them.
- Contracts are usable by both service and user-session agent tests without requiring live global hooks.
Scope: layer-1+

## FR-MKP-001 Hotkey toggle only

Support configurable hotkeys to switch the active machine and proxy direction. Defaults are local Ctrl+Alt+F1 and remote Ctrl+Alt+F2.

Acceptance Criteria:
- Hotkey switches focus and proxy direction on both paired hosts.
- No automatic edge crossing or edge mouse detection exists in this product.
- Configuration is persisted and can be read back by the REPL and tray UI.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Hotkey switches focus and proxy direction on both paired hosts.
- [x] No automatic edge crossing or edge mouse detection exists in this product. (evidence: F:\GitHub\MouseKeyProxy\docs\Project\Functional-Requirements.md; AGENTS.md Byrd scope)
- [ ] Configuration is persisted and can be read back by the REPL and tray UI.
- [x] Toggle and release paths clear left/right Shift, Ctrl, Alt, and Win on both peers. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=InputRegression; dotnet test tests\MouseKeyProxy.Common.Tests -c Release --filter Category=ModifierCleanup)

## FR-MKP-002 Keyboard focus follows

When the active machine changes through a hotkey or explicit command, keyboard input and mouse input, when applicable, are proxied to the focused machine.

Acceptance Criteria:
- Focus state is observable through REPL/tray status.
- Toggle transitions synthesize safe modifier key-up events.
- Unsupported focus transitions fail observably and leave local control available.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Focus state is observable through REPL/tray status.
- [x] Toggle transitions synthesize safe modifier key-up events locally and remotely. (evidence: dotnet test tests\MouseKeyProxy.Common.Tests -c Release --filter Category=ModifierCleanup)
- [ ] Unsupported focus transitions fail observably and leave local control available.
- [x] Mouse capture release restores local mouse control without requiring Alt-Tab. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=InputRegression)

## FR-MKP-003 Full proxy input

Proxy input according to the supported matrix: ordinary keys, modifiers, pointer move/click/wheel, media keys, and permitted Windows key combinations. Secure Attention Sequence, secure desktop, lock/login screens, and UIPI-blocked scenarios are explicitly excluded.

Acceptance Criteria:
- Supported input events work against a paired remote session.
- Unsupported inputs fail observably and never hang or claim success.
- Input behavior is covered by unit tests and at least one paired-machine smoke receipt.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Supported key, modifier, pointer, wheel, media, and permitted Windows key combinations work against a paired remote session.
- [ ] Unsupported inputs fail observably and never hang or claim success.
- [ ] Input behavior is covered by unit tests and at least one paired-machine smoke receipt.
- [ ] Physical HID backend uses a standard USB HID keyboard/mouse appliance path and requires no custom Windows driver.

## FR-MKP-004 Real-time clipboard LIFO sync

Sync clipboard history between paired systems in real time. Merge entries as a newest-first LIFO stack, with local encrypted persistence and bounded storage.

Acceptance Criteria:
- Copy on either machine appears on the peer as the top clipboard entry.
- At least text, HTML, and image formats are handled per the technical support matrix.
- Local persistence uses CurrentUser DPAPI under LocalAppData and can be cleared by the user.
Scope: layer-1+

## FR-MKP-005 gRPC advanced controls and real paired control

Support host gRPC calls for InjectInput, SetMousePosition, LocateProcess, and SetFocusByHwnd. Completion is not accepted until payton-legion2 and payton-desktop are paired and payton-legion2 is visibly controlling payton-desktop.

Acceptance Criteria:
- InjectInput can target a paired remote by peer id.
- SetMousePosition accepts display plus x/y without changing focus unless requested.
- LocateProcess can return process and window handle tree data by name or PID.
- SetFocusByHwnd can focus and optionally bring a target window forward.
- The lab topology uses payton-legion2 plus payton-desktop on the agreed gRPC service port, currently 50051 unless changed by configuration.
- Final proof includes real paired control evidence: cursor movement and a sentinel text/input action on payton-desktop initiated from payton-legion2.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] InjectInput can target a paired remote by peer id.
- [ ] SetMousePosition accepts display plus x/y without changing focus unless requested.
- [ ] LocateProcess can return process and window handle tree data by name or PID.
- [ ] SetFocusByHwnd can focus and optionally bring a target window forward.
- [ ] Lab topology uses payton-legion2 plus payton-desktop on the configured gRPC service port.
- [ ] Final proof includes real paired control evidence including cursor movement and a sentinel input action on payton-desktop initiated from payton-legion2.
- [x] CaptureScreenshot can return timestamped metadata and image bytes for WindowProbe E2E proof. (evidence: dotnet test tests\MouseKeyProxy.Service.Tests -c Release --filter Category=WindowProbeE2E)

## FR-MKP-006 Setup, REPL, service lifecycle, and agent UI

Provide setup tooling, a management REPL/CLI, service lifecycle commands, and a usable agent UI. The UI must not be a throwaway or diagnostic-only surface.

Acceptance Criteria:
- REPL/CLI is the canonical implementation of the control surface. It manages pairing, pair status, agent status, service install/uninstall/update/status, clipboard operations, toggle state, emergency release, logs, and remote-control commands.
- Service install is explicit (`mkp service install`) and includes rollback for partial failures.
- User-session agent UI provides tray status and a compact dashboard for pairing state, active peer, service state, clipboard sync, and recent errors.
- Tray/dashboard actions use shared command implementation instead of per-click process spawning, and must not expose UI-only controls that are missing from the CLI.
- UI can initiate and display pairing, toggle, reconnect, emergency release, service status, and logs/receipt location.
- UI visual design is validated by Codex through screenshots or equivalent visual receipts.
Scope: layer-1+

## FR-MKP-007 Full logging through ILogger to Windows Event Viewer

All service components and relevant agent operations must log through Microsoft.Extensions.Logging.ILogger<T> or ILoggerFactory. The service host writes to Windows Event Viewer through the EventLog provider with source MouseKeyProxy and log Application.

Acceptance Criteria:
- Startup, shutdown, pairing, gRPC session open/close, input batch processing, failures, and failsafe triggers are logged at appropriate levels.
- Event Viewer entries are visible under Windows Logs > Application with Source = MouseKeyProxy.
- Structured properties such as PeerId, Seq, and ErrorCode are preserved where supported.
- Production service code does not use Console.WriteLine or direct EventLog.WriteEntry.
- EventLog source creation occurs during elevated service install when missing.
- Unit/component tests verify logging calls without writing to the real Event Log.
Scope: layer-1+

## FR-MKP-008 Hacker mouse branding

Logo and branding must center on a hacker mouse typing on a keyboard at a desk surrounded by monitors.

Acceptance Criteria:
- Primary logo/brand asset depicts the required hacker mouse, keyboard, desk, and monitor-surrounded scene.
- Branding appears consistently in the tray/dashboard, package metadata where applicable, docs, and release artifacts.
- Generic mouse, keyboard-only, monitor-only, or abstract network branding is not sufficient.
- Visual receipts demonstrate the branding at app-size and documentation-size scales.
Scope: layer-1+

## FR-MKP-009 Delegated Codex/Claude implementation workflow

Project work must use Codex for design, testing, review, and receipts, and Claude for implementation code.

Acceptance Criteria:
- Codex-authored plans identify implementation slices, acceptance criteria, and validation receipts.
- Claude-authored prompts are used for product implementation code changes when code changes are required.
- Codex validates Claude output through tests, review, and pairing evidence before a slice is accepted.
- Handoffs record which agent produced implementation and which agent validated it.
Scope: layer-1+

## FR-MKP-010 Agent invocation observability

Agent cmdlets must make Codex and Claude invocations auditable and visible to the host.

Acceptance Criteria:
- Invoke-Codex and Invoke-Claude print a summary of parameters passed to the agent before launch.
- Cmdlets echo the complete executable and argument call signature before launch.
- Agent stdout/stderr flows freely to the host and is also captured to a log file.
- Cmdlets return or throw on nonzero exit codes and do not suppress output through Out-Null in the agent output pipeline.
- Dry-run validation is available without starting the agent.
Scope: layer-1+

## FR-MKP-011 NSubstitute-only test doubles

Moq is banned and must never be used in MouseKeyProxy. All .NET test doubles, mocks, substitutes, and call verifications must use NSubstitute or purpose-built fakes/stubs.

Acceptance Criteria:
- Active source, project, package, and test files contain no Moq package references or `using Moq` usage.
- New or refactored tests use NSubstitute for substitute creation and received-call verification.
- CI or local validation includes a scan that fails if Moq is introduced.
- Existing Fresh documentation that already says "never Moq" is preserved only as historical context; Product requirements are the active rule.
Scope: layer-1+

## FR-MKP-012 Physical Raspberry Pi Zero 2 HID appliance backend

MouseKeyProxy must support an optional physical Raspberry Pi Zero 2 appliance backend implemented in C#/.NET 10. The appliance presents standard USB HID keyboard and mouse interfaces to the target Windows host while receiving authenticated control commands over the lab network.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] The Pi Zero 2 is provisioned as mkp-hid-pi and is reachable from payton-legion2 over the lab network. (evidence: docs\receipts-hid-provision-20260707T103933Z.txt shows DNS unresolved for mkp-hid-pi.)
- [ ] The controlled Windows host enumerates the Pi as standard HID keyboard and relative mouse devices without vendor Windows drivers.
- [x] The Pi HID appliance service is implemented in C# targeting .NET 10 and published self-contained for linux-arm64. (evidence: src\MouseKeyProxy.PiHid; dotnet publish src\MouseKeyProxy.PiHid\MouseKeyProxy.PiHid.csproj -c Release -r linux-arm64 --self-contained true)
- [x] The HID appliance implementation and provisioning path contain no Python code, Python scripts, or Python runtime dependency. (evidence: dotnet test tests\MouseKeyProxy.Compliance.Tests -c Release --filter Category=HardwareHID)
- [ ] Alt-Space, Win-Arrow, and relative mouse movement through the Pi are proven by WindowProbe JSON and timestamped screenshot evidence.

## FR-MKP-013 Device lifecycle and function control

Appliance exposes connect/disconnect for keyboard, mouse, disk FS, CD-ROM, floppy; boot complete; FS content change events; configure independently; FS RO/RW; CD/floppy media device or host; events processed locally in C# and mirrored to paired host.
Scope: layer-1+

## FR-MKP-014 Device folder share with IP allowlist

Device shares a sandboxed folder over paired gRPC; UDP discovery advertises share; only paired peers whose connection IP is allowlisted (paired host and USB-connected PC, identified by pairing) may use share RPCs after client pairing.
Scope: layer-1+

## FR-MKP-015 USB optical and floppy media

Pi presents CD-ROM and virtual floppy LUNs with selectable media (device path or host inbox); independently enable/disable via ConfigureDevice.
Scope: layer-1+

## FR-MKP-016 Paired-peer SMB folder share

Pi hosts SMB of the share root; only IPs of the paired host and the USB-connected PC (both identified via pairing metadata) may access; deny all other LAN.
Scope: layer-1+

## FR-MKP-017 Install media autorun

CD-ROM and/or FAT32 install folder may include autorun.inf that launches install guidance for Windows; content lives on FAT32 deploy partition and/or CD image.
Scope: layer-1+

## FR-MKP-018 Agent manages complete paired-device config

Agent is primary runtime UI for the entire device config surface (HID, media, share, SMB, events, pairing assist).
Scope: layer-1+

## FR-MKP-019 Rufus image setup form for virtual devices

Rufus Configure Pi HID enables each virtual device and sets default CD/floppy media paths; profile Save/Load. Media files managed via FAT32 deploy partition. The form's provisioning must produce a headless-bootable card that needs no keyboard: on current Raspberry Pi OS trixie (cloud-init based) it writes a cloud-init seed from the profile to create the user, enable ssh, and configure wifi with the regulatory country, writes a custom.toml fallback for non-cloud-init images, disables the RPi first-run user wizard, and survives the post-write FAT remount race that previously aborted with ERROR_CANT_PATCH. See TR-MKP-PROV-001 and TEST-MKP-048.
Scope: layer-1+

## FR-MKP-020 FAT32 deploy partition

Appliance image includes a FAT32 partition MKP-DEPLOY for operator-managed files. Operators use Explorer when volume is mounted; avoids custom Rufus file-staging for day-to-day deploy content.
Scope: layer-1+

## FR-MKP-021 PowerShell default shell on device

Device installs PowerShell 7+ (pwsh) for the appliance user and makes pwsh the default login shell.
Scope: layer-1+

## FR-MKP-022 LiteDB config under /etc/mkp

Durable appliance configuration is stored in LiteDB under /etc/mkp (e.g. /etc/mkp/config.db). Service reads/writes this store; env vars are bootstrap overrides only.
Scope: layer-1+

## FR-MKP-023 Agent client-pairing with typed code

Device allows client pairing initiated from the Agent. Pairing completes only when a device-generated one-time code is typed on the connecting machine. Wrong/expired code fails with no cert issued.
Scope: layer-1+

## FR-OWNERSHIP-001 Ownership boundary contracts

The system must define ownership policy boundaries that separate agent rights from service rights.

Acceptance Criteria:
- Policy seams compile for agent, service, and none ownership states.
- Red tests assert service rejection and agent access rules using NSubstitute seams.
- Service code must not directly own low-level hooks, ClipCursor, SendInput, or user-session focus changes.
Scope: layer-1+

