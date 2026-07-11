# Technical Requirements (MCP Server)

## TR-HOTKEY-CONTRACT-001

**Hotkey seam compile contracts** — The .NET 10 solution must compile with interface-only seams and behaviorless stubs for hotkey toggle state, hotkey events, cursor clipping, and input injection before behavior is implemented.
**Covered by:** FR: FR-HOTKEY-001; TEST: TEST-HOTKEY-001, TEST-MKP-001, TEST-MKP-008
**Status:** pending
Scope: layer-1+

## TR-MKP-AGENTCMD-001

**Codex and Claude invocation wrappers** — Product scripts must expose Invoke-Codex, Invoke-Claude, and Invoke-MouseKeyProxyDelegation. Wrappers must default to `F:\GitHub\MouseKeyProxy`, print parameter summaries, echo complete executable/argument call signatures, stream stdout/stderr through Tee-Object without suppression, log output, support dry-run, and throw on nonzero exits.
**Covered by:** FR: FR-MKP-009, FR-MKP-010; TEST: TEST-MKP-017, TEST-MKP-018, TEST-MKP-021
**Status:** completed
Scope: layer-1+

## TR-MKP-AGENTIPC-001

**Agent command surface** — The CLI/REPL is the canonical implementation of the control surface. REPL, tray, and dashboard actions must call shared command implementations. UI actions must not shell out per click for core operations and must not expose control actions that are unavailable through the CLI. Commands must expose structured status and error results suitable for UI rendering and test assertions.
**Covered by:** FR: FR-MKP-002, FR-MKP-006; TEST: TEST-MKP-003, TEST-MKP-008, TEST-MKP-012, TEST-MKP-023, TEST-MKP-024, TEST-MKP-007, TEST-MKP-015, TEST-MKP-021
**Status:** completed
Scope: layer-1+

## TR-MKP-ARCH-001

**Process ownership and local IPC** — The Windows service owns networking, pairing, persistence, watchdogs, and service lifecycle. The user-session agent/tray owns low-level hooks, hotkeys, clipboard listener, ClipCursor, SendInput, and focus operations. Agent/service communication must use authenticated local IPC with timeouts and reconnect behavior.
**Covered by:** FR: FR-OWNERSHIP-001; TEST: TEST-MKP-002, TEST-MKP-005, TEST-OWNERSHIP-001
**Status:** pending
Scope: layer-1+

## TR-MKP-BRAND-001

**Brand asset pipeline** — Maintain source and rendered logo/brand assets for the hacker mouse at keyboard/desk/monitor scene. Assets must be usable at tray-icon, window-header, documentation, and release/package scales. The repository must document asset provenance and generated variants.
**Covered by:** FR: FR-MKP-008; TEST: TEST-MKP-015, TEST-MKP-016
**Status:** pending
Scope: layer-1+

## TR-MKP-CFG-001

**LiteDB at /etc/mkp** — IApplianceConfigStore backed by LiteDB under /etc/mkp; seed.json import once when empty.
**Covered by:** FR: FR-MKP-022; TEST: TEST-MKP-044
**Status:** pending
Scope: layer-1+

## TR-MKP-CLIP-001

**Clipboard LIFO model and persistence** — Define entry schema, format support, dedupe, loop prevention, receive-time and sequence ordering, max 50 entries, per-item size limits, total storage cap, and CurrentUser DPAPI persistence under LocalAppData. Supported formats include Unicode text, CF_HTML, and image formats agreed by implementation; file drop is deferred unless explicitly added later.
**Covered by:** FR: FR-MKP-004; TEST: TEST-MKP-004, TEST-MKP-006, TEST-MKP-012
**Status:** completed
Scope: layer-1+

## TR-MKP-HID-001

**Pi Zero 2 .NET USB gadget HID backend** — The hardware backend must use Raspberry Pi OS Lite plus Linux configfs/libcomposite to expose keyboard and relative mouse HID gadget functions at /dev/hidg0 and /dev/hidg1, controlled by the mTLS MouseKeyProxy.Service on linux-arm64 (and/or the authenticated PiHid HTTP appliance). HID report descriptors written at provision time MUST be binary bytes (not shell-escaped ASCII text). Raspberry Pi OS /bin/sh is dash and does not interpret printf \xHH; provisioning must use bash or base64-decoded binary payloads and verify descriptor lengths (keyboard 63, mouse 52, first byte 0x05). On Pi Zero 2 W, config.txt must enable only dtoverlay=dwc2,dr_mode=peripheral (stock otg_mode and dr_mode=host must be disabled).
**Covered by:** FR: FR-MKP-003, FR-MKP-012; TEST: TEST-MKP-003, TEST-MKP-005, TEST-MKP-009, TEST-MKP-023, TEST-MKP-024, TEST-MKP-026, TEST-MKP-028, TEST-MKP-029, TEST-MKP-027, TEST-MKP-030
**Status:** in_progress
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Provisioning creates a configfs composite gadget with keyboard and relative mouse HID functions and binds it to an available UDC. (evidence: scripts\pi\setup-configfs-gadget.sh added but not executed on discoverable Pi hardware.)
- [x] The Pi service is a C#/.NET 10 HTTP service exposing GET /status and POST /keyboard/report, /mouse/report, /clear-modifiers, and /reset. (evidence: src\MouseKeyProxy.PiHid; tests\MouseKeyProxy.PiHid.Tests)
- [x] Requests missing or using the wrong bearer token receive an authorization failure and do not write HID reports. (evidence: dotnet test tests\MouseKeyProxy.PiHid.Tests -c Release --filter Category=HardwareHID)
- [x] clear-modifiers sends all-up keyboard reports and zero-button mouse reports. (evidence: dotnet test tests\MouseKeyProxy.PiHid.Tests -c Release --filter Category=HardwareHID)
- [x] HID appliance source, docs, and provisioning assets do not depend on Python. (evidence: tests\MouseKeyProxy.Compliance.Tests\HardwareHidComplianceTests.cs)

## TR-MKP-HID-002

**Device event bus and configfs controller** — Local bus fan-out; coordinator edges; ConfigureDevice/GetDeviceConfiguration; Linux configfs controller; Windows in-memory.
**Covered by:** FR: FR-MKP-013; TEST: TEST-MKP-040
**Status:** pending
Scope: layer-1+

## TR-MKP-HID-003

**Multi-LUN mass storage** — mass_storage.0 lun.0 disk, lun.1 CD cdrom=1 RO, lun.2 floppy; provisioned by setup script and rufus firstrun.
**Covered by:** FR: FR-MKP-013; TEST: TEST-MKP-040
**Status:** pending
Scope: layer-1+

## TR-MKP-HID-007

**FAT32 deploy partition layout** — Image includes FAT32 MKP-DEPLOY with media/, share/, install/; mount /mnt/mkp-deploy; operators copy with Explorer.
**Covered by:** FR: FR-MKP-020; TEST: TEST-MKP-047
**Status:** pending
Scope: layer-1+

## TR-MKP-HID-008

**pwsh default shell** — First-boot installs PowerShell 7+ arm64; appliance user shell set to pwsh.
**Covered by:** FR: FR-MKP-021; TEST: TEST-MKP-040
**Status:** pending
Scope: layer-1+

## TR-MKP-INPUT-001

**Input support matrix and limits** — Document and enforce supported keyboard, mouse, focus, and window-control inputs. Exclude SAS/Ctrl+Alt+Del, secure desktop, lock/login screens, and UIPI-blocked scenarios. Unsupported operations must return observable failures and release any captured local state.
**Covered by:** FR: FR-HOTKEY-001, FR-MKP-001, FR-MKP-002, FR-MKP-003, FR-MKP-005; TEST: TEST-HOTKEY-001, TEST-MKP-001, TEST-MKP-008, TEST-MKP-012, TEST-MKP-023, TEST-MKP-024, TEST-MKP-003, TEST-MKP-005, TEST-MKP-009, TEST-MKP-026, TEST-MKP-028, TEST-MKP-029, TEST-MKP-010, TEST-MKP-011, TEST-MKP-014, TEST-MKP-020, TEST-MKP-025
**Status:** in_progress
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Keyboard, modifier, pointer, wheel, media, permitted Windows chords, and explicit exclusions are documented and enforced.
- [x] Alt-Space and Win-Arrow preserve modifier ordering, key-up, scan, and extended-key semantics. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=InputRegression)
- [x] Mouse forwarding uses raw relative deltas rather than clipped cursor screen coordinates. (evidence: dotnet test tests\MouseKeyProxy.Agent.Tests -c Release --filter Category=RawMouseCapture)
- [ ] The optional pi-hid backend maps MouseKeyProxy input batches into fixed-size keyboard and mouse HID reports.

## TR-MKP-LOG-001

**ILogger and EventLog integration** — Service logging must use Microsoft.Extensions.Logging and EventLog provider configuration. Event source creation belongs to elevated install. Tests must validate ILogger calls through substitutes or test sinks without requiring real Event Log writes.
**Covered by:** FR: FR-MKP-007; TEST: TEST-MKP-019, TEST-MKP-021
**Status:** pending
Scope: layer-1+

## TR-MKP-ORCH-001

**Paired lab orchestration** — Provide repeatable scripts/commands and receipts for pairing payton-legion2 with payton-desktop, starting services, verifying port reachability, running gRPC calls, and proving visible remote control from Legion2 to Desktop.
**Covered by:** FR: FR-MKP-005, FR-MKP-006, FR-MKP-009, FR-MKP-012; TEST: TEST-MKP-009, TEST-MKP-010, TEST-MKP-011, TEST-MKP-014, TEST-MKP-020, TEST-MKP-025, TEST-MKP-007, TEST-MKP-012, TEST-MKP-015, TEST-MKP-021, TEST-MKP-017, TEST-MKP-018, TEST-MKP-027, TEST-MKP-028, TEST-MKP-029, TEST-MKP-030
**Status:** in_progress
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Scripts or CLI commands can pair hosts, start services, check ports, run gRPC calls, and prove visible remote control.
- [x] WindowProbe JSON and timestamped screenshot metadata are collected for key-effect proof. (evidence: dotnet test tests\MouseKeyProxy.Service.Tests -c Release --filter Category=WindowProbeE2E)
- [ ] Orchestration can check Pi network reachability, token auth, and target-host HID evidence without mutating target state.
- [ ] Transition hardware runs write receipts with source host, target host, Pi host, correlation id, and UTC timestamps.

## TR-MKP-PROV-001

**Rufus headless first-boot provisioning (Raspberry Pi OS trixie)** — Rufus MKP Pi HID provisioning must produce a headless-bootable, network-reachable Raspberry Pi OS card with no keyboard or console interaction. Current stock Raspberry Pi OS (trixie, e.g. 2026-06-18-raspios-trixie-arm64-lite) provisions via cloud-init (NoCloud datasource), not custom.toml. From the saved profile Rufus writes: (1) a cloud-init seed - user-data creating the appliance user with password, groups, passwordless sudo, and authorized ssh key; meta-data with a fresh instance_id to force cloud-init to re-run; network-config with "config: disabled" so firstrun owns wifi; (2) a custom.toml fallback honored only by non-cloud-init images; (3) a firstrun.sh that writes a NetworkManager wifi keyfile including the regulatory country, enables ssh, and disables the Raspberry Pi first-run user wizard (cancel-rename plus systemctl disable/mask userconfig plus getty@tty1 restart) so the keyboard-less console never blocks. The post-write FAT boot-partition rescan and remount MUST retry (a transient "Access is denied" on the physical drive is expected immediately after a raw image write); a single-shot remount that fails must NOT abort provisioning with ERROR_CANT_PATCH. The GUI and the headless --mkp-auto-write path share the same provisioning code.
**Covered by:** FR: FR-MKP-019; TEST: TEST-MKP-048
**Status:** pending
Scope: layer-1+

## TR-MKP-RELI-001

**Event reliability and failsafes** — Input events require sequence/ack handling, stuck-key cleanup, disconnect cleanup, emergency unclip hotkey, auto-release on exit, and local-only fallback. Safety deadlines: ClipCursor release within 2 seconds and reconnect give-up within 5 seconds unless configuration says otherwise.
**Covered by:** FR: FR-HOTKEY-001, FR-MKP-001, FR-MKP-002, FR-MKP-003, FR-MKP-004, FR-MKP-005, FR-MKP-012; TEST: TEST-HOTKEY-001, TEST-MKP-001, TEST-MKP-008, TEST-MKP-012, TEST-MKP-023, TEST-MKP-024, TEST-MKP-003, TEST-MKP-005, TEST-MKP-009, TEST-MKP-026, TEST-MKP-028, TEST-MKP-029, TEST-MKP-004, TEST-MKP-006, TEST-MKP-010, TEST-MKP-011, TEST-MKP-014, TEST-MKP-020, TEST-MKP-025, TEST-MKP-027, TEST-MKP-030
**Status:** completed
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Input events use sequence or ack handling where remote delivery can fail.
- [x] Toggle, disconnect, emergency release, and local regain clear left/right Shift, Ctrl, Alt, and Win locally and remotely. (evidence: dotnet test tests\MouseKeyProxy.Common.Tests -c Release --filter Category=ModifierCleanup)
- [ ] Captured local mouse state and ClipCursor are released within the configured safety deadline.
- [ ] Pi HID reset and clear-modifiers leave keyboard and mouse reports in neutral state.

## TR-MKP-REPL-001

**REPL install and payload publish** — REPL commands must support explicit service install/uninstall/update/status, pairing, pair status, agent status, emergency release, logs, settings, clipboard, remote-control operations, and toggle operations. Install bundles self-contained service payloads, configures Kestrel HTTP/2 on the configured gRPC port, uses Windows PowerShell 5.1 where required for firewall/service operations, and separates service payload location from global tool installation.
**Covered by:** FR: FR-MKP-001, FR-MKP-006, FR-MKP-007; TEST: TEST-MKP-001, TEST-MKP-008, TEST-MKP-012, TEST-MKP-023, TEST-MKP-024, TEST-MKP-007, TEST-MKP-015, TEST-MKP-021, TEST-MKP-019
**Status:** pending
Scope: layer-1+

## TR-MKP-SEC-001

**Pairing auth and RPC authorization** — Discovery may use UDP broadcast and/or mDNS on LAN. Pairing must use a pairing code or ToFU flow and persist secret/certificate material securely. RPC authorization must reject unpaired, revoked, or bad-secret peers before any input/clipboard/focus effect. UPnP IGD/NAT port mapping is excluded.
**Covered by:** FR: FR-MKP-003, FR-MKP-004, FR-MKP-005, FR-OWNERSHIP-001; TEST: TEST-MKP-003, TEST-MKP-005, TEST-MKP-009, TEST-MKP-023, TEST-MKP-024, TEST-MKP-026, TEST-MKP-028, TEST-MKP-029, TEST-MKP-004, TEST-MKP-006, TEST-MKP-012, TEST-MKP-010, TEST-MKP-011, TEST-MKP-014, TEST-MKP-020, TEST-MKP-025, TEST-MKP-002, TEST-OWNERSHIP-001
**Status:** completed
Scope: layer-1+

## TR-MKP-SEC-PAIR-001

**Client pairing with typed code** — Agent client-pair request generates OTP; code typed on connecting machine; expire; rate-limit; no cert without match.
**Covered by:** FR: FR-MKP-023; TEST: TEST-MKP-046
**Status:** pending
Scope: layer-1+

## TR-MKP-TESTDOUBLE-001

**NSubstitute-only .NET test doubles** — Test projects must not reference Moq. Mock/substitute behavior must use NSubstitute APIs such as `Substitute.For<T>()`, `Returns`, `Received`, and `DidNotReceive`, or explicit hand-written fakes where a substitute is inappropriate. Repository validation must scan package and source files for Moq before accepting implementation slices.
**Covered by:** FR: FR-MKP-011; TEST: TEST-MKP-022
**Status:** pending
Scope: layer-1+

## TR-MKP-UI-001

**Tray and dashboard UX** — Implement a WinForms tray agent and compact dashboard or equivalent native Windows UI. It must show pairing state, active peer, service state, clipboard sync state, recent errors, and controls for pair/toggle/reconnect/emergency release/service status/logs. The design must be dense, legible, and visually consistent with MouseKeyProxy branding.
**Covered by:** FR: FR-MKP-006, FR-MKP-008, FR-MKP-018; TEST: TEST-MKP-007, TEST-MKP-012, TEST-MKP-015, TEST-MKP-021, TEST-MKP-016, TEST-MKP-044
**Status:** pending
Scope: layer-1+

## TR-MKP-XFER-004

**Samba and gRPC IP gate two peers** — Allowlist IPs of PairedHost and UsbConnectedPc only; refresh on pair/revoke; deny world/guest; apply to SMB and folder-share RPC.
**Covered by:** FR: FR-MKP-014, FR-MKP-016; TEST: TEST-MKP-038, TEST-MKP-045
**Status:** pending
Scope: layer-1+

## TR-OWNERSHIP-CONTRACT-001

**Ownership seam compile contracts** — The .NET 10 solution must compile ownership and input boundary contracts without giving the service direct user-session authority.
**Covered by:** FR: FR-OWNERSHIP-001; TEST: TEST-MKP-002, TEST-MKP-005, TEST-OWNERSHIP-001
**Status:** pending
Scope: layer-1+

