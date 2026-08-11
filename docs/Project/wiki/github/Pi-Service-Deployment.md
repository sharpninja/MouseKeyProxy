# Linux HID-gadget service deployment (FR-MKP-012, TR-MKP-HID-001)

The appliance runs the same cross-platform `MouseKeyProxy.Service` as a Windows peer. The only difference is
the input backend: on Linux the service injects through the USB HID gadget (`HidGadgetInputInjector` ->
`/dev/hidg0` keyboard, `/dev/hidg1` mouse) instead of the Windows agent pipe. Peers pair over the
same mTLS + one-time-code (or ToFU discovery) flow.

## Supported boards

- **Raspberry Pi Zero 2 W** - Raspberry Pi OS Lite; `dtoverlay=dwc2,dr_mode=peripheral`; publish RID **`linux-arm`** (or arm64 OS + `linux-arm64`). See `docs/hardware/pi-zero-2-hid.md`.
- **Orange Pi Zero 2 (Allwinner H616)** - Armbian Bookworm minimal; SoC UDC + configfs (no Pi `dwc2` overlay); publish RID **`linux-arm64`**. See `docs/hardware/orange-pi-zero-2-hid.md`.

Board profiles live under `scripts/pi/boards/*.env`.

## Orange Pi Zero 2 (recommended path)

```pwsh
# Optional: publish service into the image
pwsh -ExecutionPolicy Bypass -File scripts/pi/publish-pi-hid.ps1 -Rid linux-arm64 -Service

# Flash SD (example: disk 2 / former G: bootfs). DESTRUCTIVE to the whole card.
pwsh -ExecutionPolicy Bypass -File scripts/pi/prepare-orange-pi-zero-2-sd.ps1 `
  -DiskNumber 2 `
  -PublishService `
  -WifiSsid 'BYRD3.1' `
  -WifiPsk '<your-psk>'
```

The prepare script downloads Armbian, writes the disk via WSL `dd`, and injects:

- `mkp-hid-gadget-setup.sh` + `mkp-hid-gadget.service`
- `mkp-firstboot-linux-appliance.sh` + `mkp-firstboot.service`
- `mousekeyproxy.service`
- board profile, SSH pubkey, optional Wi-Fi env, optional `/opt/mousekeyproxy` publish tree

## Raspberry Pi (legacy Rufus path)

```pwsh
mkp pi provision
# or stage image + rufus-mkp profile (see User Guide / CreatePiImage Nuke target)
```

## Manual publish and copy

```pwsh
# Orange Pi Zero 2 / aarch64
dotnet publish src/MouseKeyProxy.Service/MouseKeyProxy.Service.csproj -c Release -r linux-arm64 --self-contained true -o output/service/linux-arm64
scp -r output/service/linux-arm64/* mkp@<host>:/opt/mousekeyproxy/
scp assets/systemd/mousekeyproxy.service assets/systemd/mkp-hid-gadget.service mkp@<host>:/tmp/
scp scripts/pi/setup-configfs-gadget.sh mkp@<host>:/tmp/mkp-hid-gadget-setup.sh
```

```bash
sudo install -m 0755 /tmp/mkp-hid-gadget-setup.sh /usr/local/sbin/mkp-hid-gadget-setup.sh
sudo mv /tmp/mousekeyproxy.service /tmp/mkp-hid-gadget.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now mkp-hid-gadget.service mousekeyproxy.service
journalctl -u mousekeyproxy -f
```

Device paths are overridable via `MKP_HID_KEYBOARD_DEVICE` / `MKP_HID_MOUSE_DEVICE`.

## Pair

- **Plug-n-play (default, `MKP_TOFU=1`):** unpaired appliance advertises UDP `50052`; `mkp pair discover` pairs without a code. ToFU is only for an empty peer store (first USB host bootstrap).
- **Code-based (re-pair / later peers):** `mkp pair mint [ttlSeconds]` against the appliance, then `mkp pair <code>` on the control host (`MKP_GRPC=https://HOST:50051` if not using settings).
- **Empty discover:** if no unpaired beacons appear, `mkp pair discover` probes live gRPC endpoints on the LAN and prints a one-line mint/pair hint. Use `mkp pair reset-device` only when you still hold a valid client cert and need to re-open ToFU.
- **Control toggle:** on the control host, `mkp toggle` (or Ctrl+Win+F1) starts Agent capture; `mkp pair status` must show `Forwarding: active=True` for live keyboard/mouse.
- **Agent startup self-heal:** the tray Agent reloads credentials and probes the device channel on start (settings URL preferred). Check `%LOCALAPPDATA%\MouseKeyProxy\logs\self-heal.log` if Connected state looks wrong after reboot or re-pair.

## Input encoding and reliability

- Keyboard mapping (`HidKeyboardUsage`) covers letters, digits, F-keys, arrows, editing keys, **US OEM punctuation** (comma, period, slash, brackets, quotes, etc.), and common numpad keys.
- Before writing HID reports, the injector checks UDC host-link state (`HidLinkHealth`). When the gadget is not attached, inject fails with `DEVICE_HID_DISCONNECTED` so the control-host Agent can restore local control immediately.
- Pairing state on appliance lives under `/var/lib/mousekeyproxy/` (preserve across binary updates).

## Verify (on-hardware, env-gated)

On-hardware HID compliance tests are gated behind `MKP_HARDWARE_E2E=1` and are not part of the CI green
gate. Unit coverage: `MouseKeyProxy.PiHid.Tests` (encoder, injector, `HidKeyboardUsage` punctuation, `HidLinkHealth`).
