# MouseKeyProxy - Session Handoff

Last updated: 2026-07-09 (GrokCode: HID descriptor root cause + overlay fix; physical re-enum still open)

## TL;DR

Pi HID appliance is provisioned, paired (mTLS ToFU), and running. The previous
`/dev/hidg0` EAGAIN blocker had a **software root cause** that is now fixed on
the live Pi and in durable scripts:

- **Root cause:** `mkp-hid-gadget-setup.sh` used `#!/bin/sh` (dash). Dash
  `printf '\x05...'` writes the **literal ASCII** `\x05` (4 chars), not byte
  `0x05`. Keyboard report_desc was 252 bytes of text instead of 63 binary bytes.
  Host configured USB but never polled the HID interrupt endpoint -> EAGAIN.
- **Live fix applied:** setup script is now `#!/bin/bash` with binary descriptors
  (63 / 52 bytes, starts `05 01 09 06`). Verified on disk after reboot.
- **Overlay fix applied:** Pi Zero 2 W `config.txt` had conflicting
  `otg_mode=1` + `dtoverlay=dwc2,dr_mode=host` + `dr_mode=peripheral`. Host mode
  and `otg_mode` commented out; only `dtoverlay=dwc2,dr_mode=peripheral` remains.
  DT confirms `dr_mode=peripheral`.

**Still open (physical / host):** after reboot, dwc2 is **still re-enumerating**
(addresses climbing ~every 1-2s: 71..106+ at 48s uptime) and UDC stays
`not attached` (never settles at `configured`). That needs a data-capable cable
re-plug into PAYTON-DESKTOP and Device Manager check. Software path is ready once
the host holds a stable enumeration.

## Environment / Facts

- Pi: hostname `mkp-hid-pi`, IP `192.168.1.200`, gRPC mTLS `:50051`, discovery UDP `:50052`.
- Board: **Raspberry Pi Zero 2 W Rev 1.0** (verified).
- SSH: `ssh -i C:\Users\kingd\mkp_pi_hid_ed25519.key mkp@192.168.1.200`
  (user `mkp`, passwordless sudo). `/etc/hosts` now has `127.0.1.1 mkp-hid-pi`
  (sudo hostname warnings fixed).
- Service: `/opt/mousekeyproxy/MouseKeyProxy.Service` under `mousekeyproxy.service`
  (Env: `MKP_TOFU=1`, `MKP_STATE_DIR=/var/lib/mousekeyproxy`, HID paths). `mkp` at `/usr/local/bin/mkp`.
- Gadget unit: `mkp-hid-gadget.service` -> `/usr/local/sbin/mkp-hid-gadget-setup.sh` (**bash**).
- Pairing state on Pi: `/var/lib/mousekeyproxy/pairing-state.bin`.
- Operator credential (LEGION2): `%LOCALAPPDATA%\MouseKeyProxy\peer-credential.bin`
  (paired thumbprint `818B0E5290D1C732ECB8CEA96A57A11DBA985A9F`).
- HID gadget: UDC `3f980000.usb`, idVendor `0x1d6b` / idProduct `0x0104`.
  Descriptors: keyboard **63 binary**, mouse **52 binary** (verified).
- Operator-box firewall: inbound UDP 50052 rule for discovery (LEGION2).

## Done + committed (both LOCAL ONLY - not pushed)

1. **rufus-mkp ext4 staging** - `F:\GitHub\rufus-mkp`, commit `e364eab1` (GitHub fork; local only).
2. **Pairing key-export fix** - this repo, commit `269587f` (NOT pushed).
   - `X509KeyStorageFlags.Exportable` in PairingClient + PeerCredentialStore.Load.
   - Regression: `PairedPeer_CredentialSurvivesSaveLoad_AndAuthenticates`.
3. **Live Pi (not yet in git commit on Pi image):**
   - Fixed gadget setup script shebang + binary report_desc.
   - Fixed `config.txt` dwc2 conflict; rebooted.
   - Durable repo script: `scripts/pi/setup-configfs-gadget.sh` rewritten to
     base64-decode descriptors + verify lengths (dash-safe).

## Proven end-to-end

ext4 staging (byte-verified) -> Pi boots + service runs -> discovery -> mTLS ToFU
pairing -> authenticated effect RPC past `PairingAuthorizationInterceptor`
(ClearModifiers HTTP 200; body was `AGENT_IPC_UNAVAILABLE` for Pi routing gap).

## OPEN BLOCKER: host USB re-enumeration never settles

### Software (resolved)

| Check | Before | After |
|-------|--------|-------|
| keyboard report_desc size | 252 (ASCII `\x05...`) | **63 binary** |
| first byte | `5c` (`\`) | **`05`** |
| mouse report_desc size | 208 text | **52 binary** |
| setup shebang | `#!/bin/sh` (dash) | `#!/bin/bash` |
| DT `dr_mode` | host+peripheral conflict | **peripheral** |

### Physical / host (still open)

Post-reboot sample (uptime ~48s): address climb still active
(`new address 98..106`), UDC state `not attached`, writes fail with transport
shutdown / 0 bytes. This is **not** the old settled-configured+EAGAIN pattern;
the host is thrashing the bus.

### Next steps (operator)

1. On **PAYTON-DESKTOP**: unplug Pi USB data cable, wait 3s, re-plug into a
   rear/native USB port (avoid flaky hubs). Use a **data-capable** cable into the
   Pi Zero 2 W **OTG/data** micro-USB (not the power-only port).
2. Device Manager: look for VID_1D6B / PID_0104 (Linux Foundation gadget /
   composite HID keyboard+mouse). If Code 43 / unknown, uninstall device and re-plug.
3. On Pi, confirm settle:
   ```
   cat /sys/class/udc/*/state   # want: configured
   sudo dmesg | grep 'new address' | tail -3   # should stop climbing
   printf '\x00\x00\x00\x00\x00\x00\x00\x00' | sudo dd of=/dev/hidg0 bs=8 count=1
   ```
4. Full path test (focus Notepad on PAYTON-DESKTOP first):
   ```
   $env:MKP_GRPC = "https://192.168.1.200:50051"
   dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Debug -- inject-text "MouseKeyProxy Pi HID OK"
   ```

## Follow-ups / tech debt (not yet filed as requirements/TODOs)

1. **Half-paired orphan (robustness).** Service persists peer during `Pair` before
   client confirms success; client crypto failure leaves ToFU disarmed. Recovery:
   delete `pairing-state.bin` + restart. Prefer provisional registration / ack /
   re-arm ToFU when no peer has ever connected.
2. **ClearModifiers/EmergencyRelease routing on Pi.** Still hit Windows agent IPC
   (`AGENT_IPC_UNAVAILABLE`). On Pi these should go to HID injector.
3. **Discovery firewall dependency.** Document/automate inbound UDP 50052.
4. **firstrun host resolution + overlay + gadget writer.** Baked into rufus-mkp
   `MkpPiHidWriteFirstBootScript` (`src/rufus.c`): `/etc/hosts` self-entry, comment
   stock `otg_mode`/`dr_mode=host`, bash+base64 binary report_desc with length verify.
   Scratchpad `firstrun-b.sh` mirrored. **Rebuild rufus.exe** before next SD write.

## Push status

Nothing pushed. When approved: push MouseKeyProxy gadget-script commit to
**Azure DevOps** (`azure` remote, primary). rufus-mkp writer fix is local
(`src/rufus.c` dirty); do not push that GitHub fork unless explicitly asked.

## Key working files

- Repo: `scripts/pi/setup-configfs-gadget.sh` (durable, base64 descriptors).
- Writer: `F:\GitHub\rufus-mkp\src\rufus.c` (`MkpPiHidWriteFirstBootScript`).
- Live Pi: `/usr/local/sbin/mkp-hid-gadget-setup.sh`, `/boot/firmware/config.txt`.
- Scratchpad: `%LOCALAPPDATA%\Temp\claude\F--GitHub-MouseKeyProxy\3411c2f1-...\scratchpad`
  (`firstrun-b.sh` fixed, `flash-and-stage.ps1`, etc.).
