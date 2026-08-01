# MouseKeyProxy - Hardware HID Handoff (archived 2026-07-16)

> **Archived.** Feature-complete maintenance mode. See root HANDOFF.md for current pointer. Historical hard-stop notes retained below.

Last updated: 2026-07-16 by Codex

## Pickup Trigger

Resume this work after the correct Raspberry Pi hardware is available. The device needed for the .NET appliance path is a Raspberry Pi Zero 2 W or newer ARMv7/ARMv8-capable Pi with USB OTG data connected to the Windows target host.

Do not continue trying to make the current board run the .NET appliance. The attached board proved to be a Raspberry Pi Zero W Rev 1.1, which is ARMv6.

## Current Hard Stop

Current hardware proof from the live Pi:

```text
/proc/device-tree/model: Raspberry Pi Zero W Rev 1.1
uname -m: armv6l
getconf LONG_BIT: 32
OS: Raspbian GNU/Linux 11 (bullseye)
```

Current .NET 10 service proof:

```text
/opt/mousekeyproxy/MouseKeyProxy.Service: ELF 32-bit ARM, EABI5, hard-float
Tag_CPU_name: "7-A"
Tag_CPU_arch: v7
Tag_THUMB_ISA_use: Thumb-2
Tag_FP_arch: VFPv3-D16
```

Current .NET 8 proof:

```text
dotnet publish -c Release -r linux-arm --self-contained true produced an ARMv7 executable.
Tag_CPU_name: "7-A"
Tag_CPU_arch: v7
Running it on the Zero W failed with Illegal instruction.
Exit code: 132
```

Conclusion: .NET 8 and .NET 10 `linux-arm` do not solve this board. The OS can fix glibc/libstdc++ version gaps, but it cannot make an ARMv6 CPU execute an ARMv7 runtime. Supporting this exact Zero W would require a separate non-.NET ARMv6-compatible appliance implementation, which is outside the current C#/.NET-only plan.

## Relevant Repos

Primary target repo:

```text
F:\GitHub\MouseKeyProxy
```

Customized Rufus repo:

```text
F:\GitHub\rufus-mkp
```

MCP/orchestration workspace:

```text
F:\GitHub\MouseKeyProxy-Fresh
```

The current MCP plugin cache used for wrap-up/session logging is:

```text
C:\Users\kingd\.codex\plugins\cache\mcpserver-codex-plugin\mcpserver\1.74.0
F:\GitHub\MouseKeyProxy-Fresh\.mcpServer\codex
```

The older requested skill path under plugin 1.73.0 is stale in this session. Use the active 1.74.0 wrapper.

## Current Dirty State Summary

`F:\GitHub\rufus-mkp` has active WIP changes including:

```text
M  scripts/stage-mkp-pi-image.ps1
M  src/rufus.c
M  staging/mkp-pi/manifest.json
?? rufus.com
?? src/rufus.com
```

Important completed Rufus changes in `src/rufus.c`:

```text
HDMI forced to Full HD 1080p60:
  hdmi_force_hotplug=1
  hdmi_group=1
  hdmi_mode=16
  framebuffer_width=1920
  framebuffer_height=1080
  video=HDMI-A-1:1920x1080M@60D

Console sleep disabled:
  consoleblank=0
  proof metadata: consoleBlanking=disabled,consoleblank=0
```

Last known Rufus build passed:

```powershell
$env:MSYSTEM = 'MINGW64'
& 'C:\msys64\usr\bin\bash.exe' -lc 'cd /f/GitHub/rufus-mkp && make -j2'
```

Last rebuilt executable observed:

```text
F:\GitHub\rufus-mkp\src\rufus.exe
Length: 10830382
LastWriteTime: 2026-07-16T11:35:16.7532748-05:00
```

`F:\GitHub\MouseKeyProxy` has broader WIP for Pi HID service, docs, tests, and receipts. Do not assume all dirty files were created in one coherent commit. Inspect before committing.

`F:\GitHub\MouseKeyProxy-Fresh` also has generated/docs WIP. It is orchestration state, not the implementation target.

## Last Known SD Image Path

```text
F:\GitHub\rufus-mkp\staging\mkp-pi\raspios-lite-armhf.img
```

This image is Bullseye-era armhf and booted the wrong Zero W. For the correct device, prefer staging a newer Raspberry Pi OS 32-bit Bookworm/Trixie image before enabling the .NET 10 service. Bullseye caused GLIBC/GLIBCXX runtime failures for the current .NET 10 publish.

## Last Known Pi Access

```text
Host: mkp-hid-pi.local
User: mkp
SSH key: C:\Users\kingd\mkp_pi_hid_ed25519.key
Wi-Fi SSID staged by Rufus profile: BYRD3.1
```

Use host-key bypass for lab images because every rewritten SD image changes the host key:

```powershell
$key = 'C:\Users\kingd\mkp_pi_hid_ed25519.key'
ssh -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL -o GlobalKnownHostsFile=NUL -o UpdateHostKeys=no -o ConnectTimeout=8 -o ServerAliveInterval=2 -o ServerAliveCountMax=2 -i $key mkp@mkp-hid-pi.local 'hostname; cat /proc/device-tree/model; uname -m'
```

OpenSSH often hung locally after remote output. Use a bounded `Start-Process` wrapper and kill the local SSH process after timeout while preserving stdout/stderr.

## Correct Device Resume Checklist

1. Insert the correct Pi and boot a freshly written card.
2. Verify hardware before debugging software:

```sh
cat /proc/device-tree/model
uname -m
getconf LONG_BIT
```

Expected acceptable results:

```text
Raspberry Pi Zero 2 W or newer
armv7l on 32-bit OS, or aarch64 on 64-bit OS
```

If it reports `armv6l`, stop and replace the board.

3. Verify the .NET apphost/runtime CPU tags on the Pi:

```sh
readelf -A /opt/mousekeyproxy/MouseKeyProxy.Service | egrep 'Tag_CPU|Tag_ABI|Tag_FP|Tag_THUMB'
```

For the .NET `linux-arm` path, ARMv7 tags are expected and acceptable only on ARMv7-capable hardware.

4. Verify OS runtime baseline before enabling the service:

```sh
cat /etc/os-release
ldd --version
strings /lib/arm-linux-gnueabihf/libc.so.6 | grep GLIBC_ | tail
strings /lib/arm-linux-gnueabihf/libstdc++.so.6 | grep GLIBCXX_ | tail
```

For .NET 10 arm32, prefer Debian/Raspberry Pi OS 12+ or newer. If sticking with .NET 8, still use a real ARMv7 board; .NET 8 does not run on ARMv6 Zero W.

5. Verify first-boot provisioning actually ran:

```sh
ls -l /boot/firstrun.sh /boot/mkp-headless-firstboot.log 2>&1
ls -l /etc/systemd/system/mousekeyproxy.service /etc/systemd/system/mkp-hid-gadget.service 2>&1
systemctl is-enabled mousekeyproxy.service mkp-hid-gadget.service
systemctl is-active mousekeyproxy.service mkp-hid-gadget.service
systemctl is-active getty@tty1.service
```

Current known issue on the wrong Zero W image: the binaries were staged into rootfs, but first-boot service installation did not run. Evidence observed:

```text
/opt/mousekeyproxy/MouseKeyProxy.Service existed
/usr/local/bin/mkp existed
/etc/systemd/system/mousekeyproxy.service missing
/etc/systemd/system/mkp-hid-gadget.service missing
/etc/mousekeyproxy/status.env missing
/var/lib/mousekeyproxy/pairing.env missing
/boot/firstrun.sh still present
/boot/mkp-headless-firstboot.log missing
getty@tty1.service active
```

Do not one-off patch this on the live card as the durable fix. Fix Rufus/image provisioning so first boot installs and enables the units through the supported path.

6. Verify HDMI/dashboard behavior:

```text
config.txt should include 1080p60 HDMI settings.
cmdline.txt should include video=HDMI-A-1:1920x1080M@60D and consoleblank=0.
Dashboard target is MouseKeyProxy.Service on /dev/tty1.
```

If login prompt appears instead of the dashboard, check whether `getty@tty1.service` still owns tty1 and whether `mousekeyproxy.service` is installed/running.

7. Verify USB HID behavior only after the service and gadget units are active:

```sh
ls -l /dev/hidg0 /dev/hidg1
systemctl status mkp-hid-gadget.service --no-pager
systemctl status mousekeyproxy.service --no-pager
```

Then run the Windows-side hardware HID tests from MouseKeyProxy.

## SD Card Write Command

Recheck Disk 2 before every destructive write. Previous target was a 31.9 GB SDHC card on Disk 2, but do not assume it.

```powershell
$disk = Get-Disk -Number 2 -ErrorAction Stop
if ($disk.BusType -ne 'SD' -or $disk.Size -lt 1GB -or $disk.Size -gt 128GB -or $disk.IsBoot -or $disk.IsSystem) {
  throw "Refusing write: disk 2 safety check failed: $($disk | ConvertTo-Json -Compress)"
}
$env:MKP_PI_STAGE_DIR = 'F:\GitHub\MouseKeyProxy\output\pi-stage'
& gsudo --copyev --chdir 'F:\GitHub\rufus-mkp' 'F:\GitHub\rufus-mkp\src\rufus.exe' --gui "--iso=F:\GitHub\rufus-mkp\staging\mkp-pi\raspios-lite-armhf.img" --mkp-pi-profile=default --device 2 --mkp-auto-write
```

After writing, inspect the mounted boot partition before ejecting:

```powershell
Select-String -Path 'G:\config.txt' -Pattern 'MouseKeyProxy|hdmi_|framebuffer_'
Get-Content -LiteralPath 'G:\cmdline.txt' -Raw
Get-Content -LiteralPath 'G:\mkp-pi-headless-provisioning.txt' -Raw
```

The proof file should include:

```text
hdmiConfig=firmware-hotplug,CEA-1080p60,kms-video-arg
consoleBlanking=disabled,consoleblank=0
hidBackend=physical-pi-configfs
```

## Validation Already Run In This Session

Rufus source/build:

```text
git -C F:\GitHub\rufus-mkp diff --check -- src/rufus.c
make -j2 under MSYS2 MINGW64
```

Live hardware/runtime probes:

```text
Pi came online as mkp-hid-pi.local over SSH.
Current board proved to be Raspberry Pi Zero W Rev 1.1 armv6l.
.NET 10 service was ARMv7-tagged and failed on Bullseye glibc/libstdc++.
.NET 8 test app was ARMv7-tagged and failed on ARMv6 with Illegal instruction exit 132.
```

## Wrap-Up Status

This handoff intentionally does not claim Transition/hardware acceptance complete. The hardware gate is blocked until a correct Zero 2-class board is available and verified.

No commit/push was performed in this wrap-up because multiple repos have broad WIP and the commit-sync acknowledgement contract was not initiated. Before committing, inspect dirty files in all three repos and create an intentional commit boundary.

## Next Agent Instruction

Start with live proof, not assumptions:

1. Verify the newly supplied board identity.
2. If it is ARMv7/ARMv8-capable, update the staged base image to a current Raspberry Pi OS 32-bit image and rewrite the SD through customized Rufus.
3. Fix first-boot service installation if units are still missing.
4. Only then run HID keyboard/mouse E2E and dashboard/HDMI acceptance.

Do not resume by debugging the old Zero W unless the plan explicitly changes away from C#/.NET-only.
