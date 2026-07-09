# MouseKeyProxy - Session Handoff

Last updated: 2026-07-09 (GrokCode wrap-up → handoff to Claude)

## TL;DR (current blocker)

**Goal:** Pi SD image boots and DESKTOP sees **one** USB thumb LUN (`MKP-SHARE`) with client MSI.

**Where we are:**
- Image pipeline can write SD unattended (BuildSdCard / CreatePiImage).
- Latest write (2026-07-09 ~18:18 local) claimed exit 0 with firstrun + share MSI staged.
- **Pi still:** ICMP up at `192.168.1.190` (RPi MAC `B8-16-5F-BA-7E-CC`), **SSH :22 closed, telnet :23 closed**.
- **DESKTOP still:** **0** present `VID_1D6B` / USBSTOR (no drive letter). No MKP-SHARE volume.

**Why (evidence, not cable speculation):**
1. User established earlier: motherboard USB-A + 4-wire Micro-USB + Windows beep = data path is live. Prior sessions **did** enumerate `VID_1D6B` / `MKP-SHARE` on DESKTOP. Do **not** re-litigate the cable.
2. First-boot used `systemd.unit=kernel-command-line.target`, which **isolates** firstrun so multi-user (and stock `/boot/ssh` → sshd) never start. Hung firstrun = Wi-Fi only (ICMP), zero TCP services, gadget never bound.
3. That isolation was **removed in rufus-mkp** and rebuilt into `assets/rufus/rufus.exe`. Last successful write should have used the fixed cmdline. **Verify on next access** by reading `/proc/cmdline` or `G:\cmdline.txt` before eject: must **NOT** contain `kernel-command-line.target`.

## Environment / Facts

| Item | Value |
|------|--------|
| Pi IP (current DHCP) | `192.168.1.190` (not the old static `.200`) |
| Pi MAC | `B8-16-5F-BA-7E-CC` |
| Hostname target | `mkp-hid-pi` (profile default) |
| SSH user | `mkp` (lab password set in firstrun: `mkp` / `root:mkp`) |
| SSH key (when working) | `C:\Users\kingd\mkp_pi_hid_ed25519.key` |
| DESKTOP | PAYTON-DESKTOP via WSMan + `~/.creds/paytondesktop.cred.xml` |
| SD writer host | LEGION2 / this machine; SDXC = **Disk 2**, often `G:\` bootfs |
| Rufus source | `F:\GitHub\rufus-mkp` (fork; dirty working tree) |
| Rufus binary | `F:\GitHub\MouseKeyProxy\assets\rufus\rufus.exe` (also `F:\GitHub\rufus-mkp\src\rufus.exe`) |
| Stage dir | `output/pi-stage` (`service/`, `repl/`, `share/` MSI, `install/`) |
| Nuke write | `gsudo pwsh -File output\write-sd-now.ps1` or CreatePiImage `--AutoWrite true --RufusDevice 2` |

## Done this Grok session (uncommitted / partial)

### Image / SD pipeline
- `CreatePiImage` **DependsOn** `PublishPi` + `StagePiInstallMedia` (ordering race fixed).
- Stage copies MSI into `pi-stage/share` for thumb LUN seed (~102711296 bytes).
- Unattended Rufus write + eject works when it does not hit exit **5** (cancel dialog race; retry usually succeeds).
- `install/` rootfs stage failure is **non-fatal** (share/ is the thumb seed).

### Firstrun (`F:\GitHub\rufus-mkp\src\rufus.c` - dirty)
- Single-thumb gadget only (`share` → `/var/lib/mousekeyproxy/thumb.img`, no empty lun.1/2).
- Early recovery attempt: telnetd/python on :23, watchdog, non-blocking systemctl (still insufficient while isolation was on).
- **Critical fix:** drop `systemd.unit=kernel-command-line.target` from `first_boot_args` so multi-user can start alongside `systemd.run=firstrun.sh`.
- Cleanup trap still strips systemd.run* and reboots after firstrun.

### Durable scripts
- `scripts/pi/setup-configfs-gadget.sh`: single LUN, folder-backed VFAT thumb, base64 HID descriptors.

### DESKTOP evidence (historical, this session)
- Ghost/stale PnP: `MKP-SHARE`, `Linux File-Stor Gadget`, `VID_1D6B` (Present=false after disconnect).
- Live present count after last boots: **0** (gadget not bound on Pi).

## What Claude should do next (ordered)

1. **Confirm cmdline on card or live Pi**
   - No `kernel-command-line.target`.
   - Has `systemd.run=/boot/firmware/firstrun.sh` until firstrun cleanup.
2. **If Pi still has no SSH after 5–10 min with fixed cmdline**
   - Pull card; inspect `G:\cmdline.txt`, `G:\firstrun.sh`, `G:\mkp-firstboot.log` (if any), `G:\ssh`.
   - Optionally patch boot partition only (faster than full image rewrite) then re-insert.
3. **When SSH works** (`ssh -i ... mkp@192.168.1.190`)
   - `systemctl status mkp-hid-gadget mousekeyproxy ssh`
   - `cat /sys/kernel/config/usb_gadget/*/UDC`; force `/usr/local/sbin/mkp-hid-gadget-setup.sh` if empty.
   - `ls -la /var/lib/mousekeyproxy/thumb.img`; ensure MSI path seed under `/mnt/mkp-deploy/share` if needed.
4. **On DESKTOP (WSMan)**
   - `Get-PnpDevice -PresentOnly | ? { $_.InstanceId -match 'VID_1D6B|USBSTOR' }`
   - Expect one mass-storage LUN / volume label `MKP-SHARE` with MSI.
5. **If firstrun still bricks remote access**
   - Prefer rootfs-staged systemd units for gadget + recovery (via `MkpPiHidStageRootFs` in `format_ext.c`) so multi-user enables them without waiting on firstrun body.
   - Keep firstrun for wifi/user/pwsh only; never block recovery on apt/pwsh.

## Commands

```pwsh
# Write SD (elevated)
gsudo pwsh -NoProfile -ExecutionPolicy Bypass -File F:\GitHub\MouseKeyProxy\output\write-sd-now.ps1

# Or Nuke only image write
$env:MKP_PI_STAGE_DIR = "F:\GitHub\MouseKeyProxy\output\pi-stage"
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target CreatePiImage --AutoWrite true --RufusDevice 2 --RufusProfile default

# DESKTOP check
$cred = Import-Clixml "$HOME\.creds\paytondesktop.cred.xml"
Invoke-Command -ComputerName PAYTON-DESKTOP -Credential $cred -ScriptBlock {
  Get-PnpDevice -PresentOnly | ? { $_.InstanceId -match 'VID_1D6B|USBSTOR' }
  Get-Volume | ? { $_.FileSystemLabel -match 'MKP|SHARE' }
}

# Pi (when SSH up)
ssh -i C:\Users\kingd\mkp_pi_hid_ed25519.key -o StrictHostKeyChecking=no mkp@192.168.1.190
```

## Rufus auto-write notes
- Exit codes: `0=ok`, `1=write fail`, `2=need --device`, `3=no device`, `4=image scan fail`, **`5=cancelled`**.
- Exit 5 seen when cancel confirmation dialog was auto-accepted mid-write; **retry** usually works. Log: `F:\GitHub\rufus-mkp\src\rufus-mkp-autowrite.log`.
- Card left unpartitioned after cancel once; full rewrite recovered.

## Git state (at handoff)

### MouseKeyProxy (`master` vs origin/master)
- **Many** dirty/untracked files (device management, packaging, bootstrap, tests, etc.) - **not** all from this SD task.
- SD-related dirty highlights: `assets/rufus/rufus.exe`, `build/Build.cs`, `scripts/pi/setup-configfs-gadget.sh`, `HANDOFF.md`, `build.cmd`/`build.sh`/`build.ps1`.
- Do **not** dump-commit the whole tree without review.

### rufus-mkp (`master` vs sharpninja/master)
- Dirty: `src/rufus.c` (firstrun + cmdline isolation fix + recovery), `format_ext.c` (share stage non-fatal install), `format.c`, etc.
- Recent local commits exist for HID/base64 and rootfs staging; firstrun isolation fix **needs commit**.

## Design decisions (log for Claude)

| Decision | Why | Rejected |
|----------|-----|----------|
| Single thumb LUN only | Empty multi-LUN showed as dead drives on Windows | lun.1/2 empty mass storage |
| Folder → VFAT `thumb.img` | configfs mass_storage cannot bind a directory | binding bare folder |
| MSI in `share/` | Client install media on DESKTOP thumb | MSI only on install/ |
| Drop `kernel-command-line.target` | Isolation prevents multi-user/sshd while firstrun hangs | Isolation + telnet-inside-firstrun only |
| install/ stage non-fatal | Share seed is the thumb path; install staging was flaky | Failing entire write on install/ |

## Do not
- Blame the USB cable without new Present=0 **and** prior success re-proven absent.
- Hand-edit MCP TODO/requirements storage files; use plugin/API.
- Push to `github` remote unless asked; origin/Azure DevOps is primary per workspace rules where applicable.

## Success criteria for next agent
1. `ssh mkp@<pi-ip>` works after first boot (or after firstrun reboot).
2. DESKTOP shows **one** present USB mass-storage device / `MKP-SHARE` with MSI.
3. `mkp-hid-gadget` active; UDC non-empty; `thumb.img` present.
4. Firstrun not leaving the machine permanently without remote access.
