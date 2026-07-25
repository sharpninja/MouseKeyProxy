# Orange Pi Zero 2 / Zero 2W HID Appliance

MouseKeyProxy supports physical **Orange Pi Zero 2** (Allwinner H616) and **Orange Pi Zero 2W**
(H618 family, aarch64) HID appliance paths. The appliance is the same cross-platform
`MouseKeyProxy.Service` used on Raspberry Pi peers; only the base OS image and USB OTG bring-up differ.

**Critical:** Zero 2 and Zero 2W are **different boards**. Flashing a Zero 2 image onto a Zero 2W
(or the reverse) typically yields solid red power LED, no green activity, and no HDMI.

## Topology

- Control host: Windows lab machine (`mkp` / Agent)
- Target host: receives HID keyboard/mouse over USB from the Orange Pi OTG port
- Appliance hostname default: `mkp-hid-opi`
- Control channel: Wi-Fi or Ethernet gRPC (mTLS)
- Input channel: micro-USB OTG enumerating as HID keyboard + relative mouse (`/dev/hidg0`, `/dev/hidg1`)

## Operating System

Recommended base images (Armbian community / rolling):

- **Orange Pi Zero 2 (non-W):** `https://dl.armbian.com/orangepizero2/Bookworm_current_minimal` (or current minimal for that board)
- **Orange Pi Zero 2W 1G (lab board):** `https://dl.armbian.com/orangepizero2w/Trixie_current_minimal`
- Architecture: **aarch64** (publish RID `linux-arm64`)
- Kernel must expose a UDC under `/sys/class/udc` for gadget mode

Do **not** flash Raspberry Pi OS. Broadcom firmware (`start.elf`, `bcm*.dtb`) will not boot these boards.
Do **not** interchange Zero 2 vs Zero 2W images.

## HDMI (applied by custom Rufus / rufus-mkp)

**Do not hand-patch the image with ad-hoc loop mounts.** HDMI and the MKP rootfs overlay are applied by the customized Rufus write path (`F:\GitHub\rufus-mkp`):

- After the raw image write, if no FAT bootfs remounts (Armbian Orange Pi Zero 2 has a single `armbi_root` ext4 partition), Rufus still opens the ext4 rootfs with libext2fs.
- `MkpPiHidStageRootFs` patches `/boot/armbianEnv.txt` (and `orangepiEnv.txt` when present):
  - `console=both` (serial + HDMI)
  - `disp_mode=1080p60`
  - `display_hdmi=yes`
  - `extraargs=consoleblank=0 video=HDMI-A-1:1920x1080@60`
- Writes `/boot/mkp-hdmi-enabled.txt` as a proof file.
- Raspberry Pi images still use FAT `config.txt` HDMI force-hotplug via `MkpPiHidPatchHdmiConfig`.

Optional staged binaries (`MKP_PI_STAGE_DIR`) land under `/opt/mousekeyproxy` in the same rootfs pass.

## USB gadget notes

- Orange Pi Zero 2 / Zero 2W does **not** use Raspberry Pi `dtoverlay=dwc2`.
- Gadget setup is configfs + `libcomposite` via `scripts/pi/setup-configfs-gadget.sh` (staged as `/usr/local/sbin/mkp-hid-gadget-setup.sh`).
- The script probes both `dwc2` (no-op if absent) and Allwinner-friendly modules, then binds the first UDC under `/sys/class/udc`.
- **Zero 2W ports:** outer Type-C = power; inner Type-C = USB data/OTG; mini-HDMI in the middle. Use the **data** Type-C for HID/gadget to a host PC. Power from the outer Type-C.
- Default composite: HID keyboard + HID mouse + optional mass-storage thumb LUN + **lab USB Ethernet** (RNDIS for Windows, ECM for Linux hosts).
- Lab USB net defaults: board `192.168.7.2/24` (`MKP_USB_NET_ADDR`); set the host RNDIS/ECM adapter to `192.168.7.1/24`, then `ssh mkp@192.168.7.2`.
- Env flags: `MKP_ENABLE_USB_NET=1` (default), `MKP_ENABLE_DISK=1`. Disable net with `MKP_ENABLE_USB_NET=0` on production targets if desired.
- **UDC bind fallback:** if full composite bind fails (musb often returns err -19 / EBUSY on RNDIS), the setup script drops RNDIS, then remaining USB net, and retries so HID + thumb still come up.
- **Firstboot ordering:** `mkp-firstboot.service` is `Before=` gadget/service and only `enable`s or `start --no-block`s them (never blocking `start`/`restart`, which deadlocks multi-user).

## Build / publish

```powershell
pwsh -ExecutionPolicy Bypass -File scripts/pi/publish-pi-hid.ps1 -Rid linux-arm64 -Service
```

Outputs:

- `output/pi-hid/linux-arm64` (optional HTTP PiHid diagnostic)
- `output/service/linux-arm64` (gRPC service binary tree)

## Prepare SD card (Windows)

With the SD reader mounted (example: drive `G:` is the old Pi bootfs; the script targets the **whole disk**):

```powershell
pwsh -ExecutionPolicy Bypass -File scripts/pi/prepare-orange-pi-zero-2-sd.ps1 `
  -DiskNumber 2 `
  -SshPublicKey 'ssh-ed25519 AAAA... mousekeyproxy-pi-hid' `
  -WifiSsid 'BYRD3.1' `
  -WifiPsk '<psk-if-known>'
```

The script:

1. Downloads Armbian Bookworm minimal for Orange Pi Zero 2 (cached under `%LOCALAPPDATA%\MouseKeyProxy\pi-stage`).
2. Writes the image to the selected physical disk (destructive).
3. Mounts boot + root via WSL and injects MKP firstboot, gadget setup, systemd units, board profile, and optional published `linux-arm64` service bits.

## First boot checklist

```bash
cat /proc/device-tree/model
uname -m                    # aarch64
ls /sys/class/udc
sudo systemctl status mkp-firstboot.service mkp-hid-gadget.service mousekeyproxy.service --no-pager
ls -l /dev/hidg0 /dev/hidg1
journalctl -u mousekeyproxy -b --no-pager | tail
```

Pair from the control host with `mkp pair discover` (ToFU) or code-based pairing.

## Validation

Same CLI path as the Raspberry appliance (`hid provision-check`, `hid test-key`, `hid test-mouse`) once the host can reach the board and HID is bound.
