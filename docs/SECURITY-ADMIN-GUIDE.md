# MouseKeyProxy Security Administration Guide

MouseKeyProxy is a two-machine remote input product. Security administration should treat it as a privileged local-input bridge: a paired peer can receive keyboard, mouse, clipboard, focus, and selected control operations from the primary machine.

## Security Goals

MouseKeyProxy is designed to:

- Restrict operation to explicitly paired machines.
- Use a service boundary for privileged machine operations.
- Keep the user-session agent responsible for interactive desktop input and UI state.
- Make the CLI/REPL the canonical control surface for administration and testing.
- Restore local input quickly through emergency release.
- Write operational audit evidence to Windows Event Logs.

MouseKeyProxy is not designed to be a broad remote administration platform, a remote shell, or a file-transfer product.

## Trust Boundaries

User-session agent: runs in the signed-in user's desktop session. It owns tray/dashboard UI, hotkey registration, input capture, forwarding state, cursor clipping, and local emergency cleanup.

Windows service: runs as the installed service account and exposes the gRPC service endpoint. It manages service-side control operations and communication with the local agent pipe.

Paired peer: the only remote machine that should receive forwarded input after pairing.

CLI/REPL: the canonical administrative control surface. UI code should invoke the same shared command paths rather than inventing separate behavior.

Windows Event Log: the authoritative local audit and troubleshooting sink.

## Pairing Model

Pairing establishes the peer relationship. Until pairing succeeds, there is no remote endpoint for control operations. Administrators should verify that unpaired UI states do not expose reconnect, toggle, clipboard-to-peer, inject, or remote-control actions.

Recommended controls:

- Pair only machines owned by the same operator or approved administrative group.
- Re-pair after machine rebuilds, hostname changes, or suspected credential compromise.
- Document the expected pair: machine names, users, service endpoint, and owner.
- Remove stale pair state during decommissioning.

## Exclusive Input Control

When remote forwarding is active, local keyboard and mouse events should be consumed locally after they are queued to the remote agent. Local Windows applications must not receive normal forwarded input during remote control.

The only local exceptions should be explicit control operations such as the configured toggle chord and emergency release path. This model reduces accidental dual-entry and prevents the primary machine from receiving unintended keystrokes while the remote is active.

## Emergency Release Requirements

Emergency release is a security and safety control, not a convenience feature. It must be available from both sides of the pair and should perform local cleanup even if remote cleanup fails.

A valid emergency release should:

- Stop the local forwarder.
- Cancel pending input queues.
- Dispose the active gRPC client/channel.
- Send or synthesize modifier key-up cleanup where applicable.
- Release cursor clipping.
- Reset forwarding state.
- Update agent status so `forwardingActive=false`.
- Emit visible status or warning if remote cleanup fails.

Operators should be trained to use emergency release immediately if control state is unclear.

## Service Administration

Install, update, start, stop, and uninstall the service with the `mkp service` commands from an elevated shell.

```powershell
mkp service status
mkp service install
mkp service stop
mkp service start
mkp service uninstall
```

The service payload should be installed to the product-managed service location with hardened ACLs. Standard users should not be able to replace service binaries. Avoid running service binaries directly from a global .NET tool cache or a user-writable directory.

## Network Administration

MouseKeyProxy uses a gRPC endpoint for peer communication. Administrators should:

- Allow only the expected peer machines through host firewall policy.
- Avoid exposing the service endpoint beyond the trusted local network segment.
- Treat unexpected inbound connection attempts as suspicious.
- Confirm service endpoint and pair status during deployment validation.

If TLS or certificate configuration is deployed, certificate rotation and hostname validation should be documented with the deployment record.

## Clipboard Security

Clipboard sync can move sensitive text between machines. Administrators should decide whether clipboard sync is allowed for the environment.

Recommended policy:

- Disable or restrict clipboard sync on machines that handle credentials, regulated data, or production secrets.
- Keep clipboard history bounded.
- Skip unsupported, private, or non-text content.
- Clear clipboard state before transferring device ownership or decommissioning a machine.

Use:

```powershell
mkp clipboard clear
```

## Logging And Audit

MouseKeyProxy logs should go to Windows Event Logs. Administrators should collect logs from both paired machines when investigating pairing, forwarding, emergency release, or service failures.

Open logs with:

```powershell
mkp open-logs
```

Minimum audit evidence for incidents:

- Local hostname and peer hostname.
- Service status on both machines.
- Agent status on both machines.
- Pairing state.
- Forwarding state.
- Recent MouseKeyProxy Event Log entries.
- The exact command or UI action that triggered the issue.

## Administrative Hardening Checklist

- Install only signed or approved builds.
- Verify package version with `mkp --version` after install or update.
- Keep service binaries outside user-writable locations.
- Keep Windows service ACLs restricted to Administrators and the service identity.
- Restrict firewall access to the expected peer.
- Validate unpaired UI state disables remote-dependent actions.
- Validate emergency release from both sides during acceptance testing.
- Confirm Windows Event Log entries are produced for service and agent failures.
- Review clipboard policy before enabling sync.
- Remove stale pair state during device retirement.

## NuGet Package Administration

The .NET tool package is versioned from GitVersion. Release packages should be built from a tagged commit and published with the repository Nuke target using `NUGET_API_KEY` from the environment.

Administrators should reject packages whose version does not match the expected Git tag or whose package metadata does not declare the Apache-2.0 license.

## Incident Response

If a machine receives unexpected input:

1. Trigger emergency release.
2. Stop the service on both machines if needed.
3. Capture Event Logs and `mkp agent status --json` from both machines.
4. Verify pair state and firewall rules.
5. Re-pair only after the cause is understood.

If a service binary or package is suspected to be tampered with, uninstall the service, remove the package, rotate pairing state, and reinstall from a known-good package version.
