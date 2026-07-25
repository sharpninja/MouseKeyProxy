# Handoff: Orange Pi Zero 2W + Rufus MKP full-bundle write

**Date:** 2026-07-22  
**Workspace:** `F:\GitHub\MouseKeyProxy`  
**Related repo:** `F:\GitHub\rufus-mkp`  
**Board:** Orange Pi Zero 2W 1G (H618) — not Zero 2, not Raspberry Pi  
**Shell rule:** Always use **PowerShell.Mcp** (`pwsh__invoke_expression`) for shell; do not use built-in `run_terminal_command`.

---

## Goal

Write official Xunlong Orange Pi OS image to SD (disk 2) via custom `rufus-mkp`, with:

1. HDMI env patch (`orangepiEnv.txt`: `console=both`, `disp_mode=1920x1080p60`)
2. Full MKP bundle staged into ext4 rootfs (like RPi Zero path):
   - `/opt/mousekeyproxy/MouseKeyProxy.Service`
   - `/usr/local/bin/mkp`
   - `share/`, `install/`, `systemd/`, `sbin/`, `board.env`

---

## What works

| Item | Status |
|------|--------|
| Official image path | `F:\GitHub\rufus-mkp\staging\mkp-pi\Orangepizero2w_1.0.4_ubuntu_noble_server_linux6.1.31.img` (~2.6 GB) |
| MKP stage dir | `F:\GitHub\MouseKeyProxy\output\pi-stage` (linux-arm64 Service ~54MB, Repl ~40MB, share/install/systemd) |
| SD target | Disk **2** (SDHC ~32 GB), often letter G: when mounted |
| Rufus image DD write | **Succeeds** (exit path hit `Image write completed successfully`) |
| Elevation | Need UAC once; then elevated `pwsh` runs `rufus.exe` (not only `rufus.com` stub) |
| Launcher | `F:\GitHub\rufus-mkp\src\mkp-write-zero2w.cmd` |

### Correct elevated write command

```pwsh
cd F:\GitHub\rufus-mkp\src
$env:MKP_PI_STAGE_DIR = 'F:\GitHub\MouseKeyProxy\output\pi-stage'
# Prefer rufus.exe when already elevated — rufus.com is a 2KB re-launcher stub that exits immediately
.\rufus.exe --gui --iso=F:\GitHub\rufus-mkp\staging\mkp-pi\Orangepizero2w_1.0.4_ubuntu_noble_server_linux6.1.31.img --mkp-pi-profile=default --device 2 --mkp-auto-write
```

Or: `F:\GitHub\MouseKeyProxy\output\run-rufus-com.ps1` (elevated).

Log: `F:\GitHub\rufus-mkp\src\rufus-mkp-autowrite.log`

---

## What fails (core bug)

**Post-write ext4 staging on PhysicalDrive returns EACCES (error 13).**

Symptoms after successful image write:

```
MouseKeyProxy: could not set size on inode … (error 13).
MouseKeyProxy: could not write patched /boot/orangepiEnv.txt (non-fatal).
MouseKeyProxy: could not create directory 'mousekeyproxy' (error 13).
```

Root cause chain:

1. Official OPi image is **single ext4 root** (no FAT). Windows may still letter it (G:).
2. `nt_io.c` write path returns **EACCES (13)** when `nt_data->read_only` is set, OR when Windows denies WriteFile on mounted partition sectors.
3. Previously, `_OpenNtName` **silently fell back to read-only** on ACCESS_DENIED when RW was requested → every write became error 13. (Partial fix applied: no silent RO fallback.)
4. `MkpLockDismountAllVolumes` before stage helps open, but **writes still fail** (likely volume/PhysicalDrive lock interaction).
5. Free space on image is fine (~445 MB free) — not ENOSPC.

**Image write succeeds; MKP binary/HDMI injection does not land on card.**

---

## Code changes already made (rufus-mkp)

### `src/format_ext.c`

- HDMI patch prefers **`orangepiEnv.txt` then `armbianEnv.txt`**.
- Patch existing files first; create only if neither exists; single-file failures non-fatal.
- HDMI failure no longer aborts whole stage (best-effort).
- Refactor **started**: body renamed to static `MkpPiHidStageExt4OnVolume` — **INCOMPLETE**.

### `src/format.c`

- Calls `MkpLockDismountAllVolumes(DriveIndex)` before `MkpPiHidStageRootFs` on Orange Pi / RPi paths.

### `src/ext2fs/nt_io.c`

- Removed silent read-only fallback on ACCESS_DENIED when write was requested.

### `src/rufus.h`

- Declared `MkpPiHidStageImageFile` (not implemented yet).

### Incomplete refactor (blocks link/build of StageRootFs API)

`MkpPiHidStageRootFs` was converted into `MkpPiHidStageExt4OnVolume` but **wrappers were not re-added**:

- Missing: `BOOL MkpPiHidStageRootFs(DWORD DriveIndex, …)` → build geometry + call `MkpPiHidStageExt4OnVolume`
- Missing: `BOOL MkpPiHidStageImageFile(const char* image_path, …)` → parse image MBR, open `\\?\path offset size`, stage **offline** into the `.img` file **before** DD write
- Missing: hook in `format.c` before `WriteDrive` to call `MkpPiHidStageImageFile` when `MKP_PI_STAGE_DIR` is set

**Intended fix (resume here):** stage HDMI + MKP into the **image file offline** (regular file RW works), then DD-write the already-provisioned image. Skip or best-effort post-write StageRootFs if pre-stage succeeded.

---

## Last run results (receipts)

| Run | Image write | HDMI patch | Service staged | Exit |
|-----|-------------|------------|----------------|------|
| First (old rufus) | HIT | FAIL armbianEnv create | FAIL | 1 |
| After HDMI prefer orangepi + non-fatal | HIT | FAIL set size error 13 | FAIL mkdir error 13 | 0 then treated as ok wrongly |
| After dismount + fail on dir create | HIT | FAIL error 13 | FAIL | 1 |

Card after write: single ext4 partition ~2.6 GB (image size), not expanded; first-boot resize is OS-side.

---

## Resume checklist

1. **Finish `format_ext.c`:**
   - `MkpPiHidStageRootFs` wrapper around `MkpPiHidStageExt4OnVolume`
   - `MkpPiHidStageImageFile`: read MBR from image file; `volume_buf = "\\?\\" + abs_path + " " + offset + " " + size`; call `MkpPiHidStageExt4OnVolume`
2. **Hook `format.c`:** before `WriteDrive` for DD image, if MKP provisioning + `MKP_PI_STAGE_DIR`, call `MkpPiHidStageImageFile(image_path, stage_dir)`; set `mkp_pi_hid_first_boot_written` on success so post-write path skips failing PhysicalDrive stage.
3. **Rebuild:** MSBuild `F:\GitHub\rufus-mkp\rufus.sln` Release|x64 → copy `x64\Release\rufus.exe` → `src\rufus.exe`
4. **Re-write SD** elevated with `MKP_PI_STAGE_DIR` set; verify log HITs:
   - `Image write completed successfully`
   - `patched /boot/orangepiEnv` or `created /boot/orangepiEnv`
   - `staged MouseKeyProxy.Service` / `staged mkp` / `staged share` / `staged systemd`
   - `exiting with code 0`
5. Boot Zero 2W; check HDMI + service (Wi-Fi still not baked unless added separately).

---

## Paths quick reference

```
Image:   F:\GitHub\rufus-mkp\staging\mkp-pi\Orangepizero2w_1.0.4_ubuntu_noble_server_linux6.1.31.img
Stage:   F:\GitHub\MouseKeyProxy\output\pi-stage
Rufus:   F:\GitHub\rufus-mkp\src\rufus.exe (+ rufus.com stub)
Log:     F:\GitHub\rufus-mkp\src\rufus-mkp-autowrite.log
Runner:  F:\GitHub\MouseKeyProxy\output\run-rufus-com.ps1
Board:   scripts/pi/boards/orange-pi-zero-2w.env
Docs:    docs/hardware/orange-pi-zero-2-hid.md
```

---

## Agent notes

- User prefers **`.\rufus.com`** as entry; when already elevated use **`rufus.exe`** so Wait-Process is real.
- No Python in this lab.
- Wi-Fi credentials not injected yet; LAN discovery may fail until Wi-Fi/SSH configured.
- PowerShell.MCP text edits: use `Add-LinesToFile` / `Update-MatchInFile` / `Show-TextFiles`, not `Set-Content`/`Get-Content` when in MCP console.
- Azure DevOps is primary remote for MouseKeyProxy; rufus-mkp is sibling checkout.

---

## Success criteria

SD boots Zero 2W with HDMI; rootfs contains staged MKP service/repl/units; autowrite log shows stage HITs and exit 0.

---

## SUCCESS RECEIPT (2026-07-22 ~15:12)

Offline image staging + DD write completed with **exit 0**.

### Verified HITs in `rufus-mkp-autowrite.log`

- offline staging / StageImageFile
- patched `/boot/orangepiEnv` (HDMI)
- staged MouseKeyProxy.Service (verify PASS 56374753 bytes)
- staged mkp (verify PASS 41921067 bytes)
- staged share/, install/, systemd units (enabled multi-user wants)
- staged sbin helpers + board.env
- Image write completed successfully (2.6 GB to disk 2)
- exiting with code 0
- no error 13 / EACCES / ENOSPC

### Fix that worked

Stage MKP into the **`.img` file offline** via `MkpPiHidStageImageFile` before `WriteDrive`, instead of post-write PhysicalDrive ext4 writes (Windows EACCES).

### Next for operator

1. Insert SD into Orange Pi Zero 2W 1G and power on.
2. Confirm HDMI (`console=both`, `disp_mode=1920x1080p60`).
3. Wi-Fi still not baked; configure network / first-boot as needed, then check `mousekeyproxy.service`.
4. Note: source image file was modified in place (now contains MKP); re-runs re-stage idempotently.
