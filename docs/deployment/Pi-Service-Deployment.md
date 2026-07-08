# Raspberry Pi HID-gadget service deployment (FR-MKP-012, TR-MKP-HID-001)

The Pi runs the same cross-platform `MouseKeyProxy.Service` as a Windows peer. The only difference is
the input backend: on Linux the service injects through the USB HID gadget (`HidGadgetInputInjector` ->
`/dev/hidg0` keyboard, `/dev/hidg1` mouse) instead of the Windows agent pipe. The Pi pairs over the
same Phase-1 mTLS + one-time-code flow as any other peer.

## Prerequisites

- Raspberry Pi OS Lite (64-bit) on a Pi Zero 2 W (or any dwc2-capable board).
- USB HID gadget configured (dwc2 overlay + libcomposite `configfs`) exposing `/dev/hidg0` (boot
  keyboard) and `/dev/hidg1` (boot mouse). The rufus-mkp provisioning path sets this up.

## Publish and copy

```pwsh
# From the repo root, publish the linux-arm64 self-contained service:
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target PublishService
# Copy the published output to the Pi:
scp -r artifacts/service/linux-arm64/* pi@<pi-host>:/opt/mousekeyproxy/
scp assets/systemd/mousekeyproxy.service pi@<pi-host>:/tmp/
```

## Install the unit

```bash
sudo mv /tmp/mousekeyproxy.service /etc/systemd/system/mousekeyproxy.service
sudo systemctl daemon-reload
sudo systemctl enable mousekeyproxy
sudo systemctl start mousekeyproxy
journalctl -u mousekeyproxy -f      # confirm "MouseKeyProxy service starting (logging via journald)"
```

The device paths are overridable via `MKP_HID_KEYBOARD_DEVICE` / `MKP_HID_MOUSE_DEVICE` in the unit's
`Environment=` lines.

## Pair

From a control host REPL: `mkp pair mint` on the Pi's service host (or over the network to it) prints a
one-time code; run `mkp pair <code>` on the peer to complete the mTLS handshake. Thereafter effect RPCs
(InjectInput, OpenSession) are authorized by the pairing interceptor and injected through the HID gadget.

## Verify (on-hardware, env-gated)

On-hardware HID compliance tests are gated behind `MKP_HARDWARE_E2E=1` and are not part of the CI green
gate (the lab machines are Windows-only). See the encoder/injector unit tests in
`MouseKeyProxy.PiHid.Tests` for deterministic coverage of report encoding.
