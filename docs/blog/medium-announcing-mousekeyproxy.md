# MouseKeyProxy: Hotkey-Only Keyboard and Mouse Through a USB HID Appliance

**Subtitle:** Explicit control handoff from a Windows host to any PC that can take a USB keyboard and mouse. No edge switching, no agent on the target.

---

If you work across a Windows machine and another PC (lab box, game PC, kiosk, firmware console), you already know the pain: either you juggle two keyboards, or you install a remote-control stack that switches focus when the pointer hits the edge of a monitor, or you accept full remote-desktop latency for something as simple as typing into the other box.

**MouseKeyProxy** is my answer to that problem. It is a free, open-source, hotkey-only path: one physical keyboard and mouse on a Windows **control host**, a small **USB HID appliance** (Orange Pi or Raspberry Pi) on the network, and a **target PC** that only sees a standard USB keyboard and mouse. Explicit toggle to remote, emergency release that always puts the host back first.

Repository: [github.com/sharpninja/MouseKeyProxy](https://github.com/sharpninja/MouseKeyProxy)

---

## What it is

MouseKeyProxy is a small system of cooperating pieces:

1. **Service** (control host) — local Windows service and the authenticated gRPC peer surface.
2. **Agent** — Windows tray / dashboard that owns Win32 hooks, the toggle and emergency hotkeys, and the user-session UI.
3. **`mkp` tool** — the .NET CLI for install, pairing, toggle, status, share, HID checks, and Pi provisioning.
4. **HID appliance** — the same cross-platform service on Linux (`linux-arm64` / arm), injecting keyboard and relative mouse through a USB HID gadget into whatever PC it is plugged into.

**Product path for keyboard and mouse:** control host → authenticated gRPC → Pi → USB OTG HID → target. The target does **not** run MouseKeyProxy for input inject. Direct Windows-to-Windows mouse/key proxy is **not** a supported product mode.

When forwarding is active, the HID target gets exclusive keyboard and mouse from the control host. When you toggle off, emergency-release, or lose the HID link, **the host is restored first** so a hung appliance cannot trap your local machine.

Default hotkeys (both configurable):

- Toggle: **Ctrl+Win+F1**
- Emergency release: **Ctrl+Alt+F3**
- Safety fallbacks: **F1** or **F3** with any two of Ctrl, Alt, and Win

---

## What it is not

- Not edge-of-screen switching (that is the classic Mouse Without Borders / PowerToys shape; keep that if you want it).
- Not mirror mode.
- Not a remote desktop for pixels (you still look at the target’s own display or a KVM if you need video).
- Not “install our agent on every machine you want to type into.” The target only needs USB.

---

## How I use it in the lab

- **Control host:** Windows 11 with Agent + Service + `mkp`.
- **Appliance:** Orange Pi Zero 2W (or Raspberry Pi Zero 2 W) on Wi-Fi or Ethernet, paired over mTLS.
- **Target:** whatever the Pi’s USB OTG port is plugged into.

The appliance presents as a composite USB device (HID keyboard + relative mouse; optional lab mass-storage / networking in the gadget config). Letters, digits, and US punctuation map to boot-protocol HID usages so inject works even when the target is at a firmware or login screen that ignores high-level remote tools.

Provisioning is aimed at operators: Nuke/Rufus-oriented SD workflows, `mkp pi provision`, board-specific HID docs, and an optional printable OpenSCAD case for the Zero 2W.

---

## Safety as a product requirement

Remote input tools fail in the worst way: **you lose local keyboard and mouse and cannot get them back.** MouseKeyProxy treats that as a first-class failure mode.

- Toggle, emergency release, and HID link-loss **unhook local capture before** peer RPC.
- A Windows **busy pointer** marks remote ownership; normal cursor returns when local control is restored.
- Pairing is certificate-based (mTLS). Untrusted peers do not get effect RPCs.

Those choices make daily use a little more explicit and a lot less scary.

---

## Bonus: manage appliance share content from the host

Beyond keyboard and mouse, the appliance can expose a **sandboxed folder share** over the same paired gRPC channel (`MKP_FOLDER_SHARE=1` on the device). The control host can list, upload, download, mkdir, rename, and delete under that root (often the tree that seeds the USB mass-storage LUN). Access is **paired identity only** (client cert), not a hand-edited IP list for gRPC.

On Windows, if you install the **WinFsp** runtime, the tray Agent can mount that share as a normal drive letter so Explorer and ordinary apps work against the appliance store. Mount lives in the **user session** (Agent), not the Windows service.

A **client MSI / install kit** (`PackClientMsi`) stages Service + Agent + bootstrap for USB or MKP-DEPLOY media when you need a simple installer path.

---

## Get started

```powershell
dotnet tool install --global MouseKeyProxy.Repl
mkp --version
mkp service install
mkp pair status
mkp toggle
```

Docs worth reading first:

- [User Guide](https://github.com/sharpninja/MouseKeyProxy/blob/master/docs/USER-GUIDE.md)
- [Security Administration Guide](https://github.com/sharpninja/MouseKeyProxy/blob/master/docs/SECURITY-ADMIN-GUIDE.md)
- [Orange Pi Zero 2 / 2W HID](https://github.com/sharpninja/MouseKeyProxy/blob/master/docs/hardware/orange-pi-zero-2-hid.md)
- [Raspberry Pi Zero 2 W HID](https://github.com/sharpninja/MouseKeyProxy/blob/master/docs/hardware/pi-zero-2-hid.md)
- [Orange Pi Zero 2W printable case](https://github.com/sharpninja/MouseKeyProxy/blob/master/cad/orange-pi-zero-2w/README.md)

License: **Apache-2.0** for MouseKeyProxy code. The bundled Rufus fork used for SD writing is GPLv3 (mere aggregation; see third-party notices in the repo). WinFsp, if you use the virtual drive, has its own license (GPLv3 with a FLOSS exception for open-source consumers).

---

## Why I built it

I wanted multi-machine control that:

1. Never surprises me with edge-triggered focus changes.
2. Always returns local input when something goes wrong.
3. Can drive a machine that should not (or cannot) run my agent software, via a small USB HID appliance.

If that matches how you work, try MouseKeyProxy and open issues or PRs on GitHub. If you only need classic Mouse Without Borders-style edge switching between two Windows desktops, keep using PowerToys; this project is intentionally a different product shape.

---

## Suggested Medium tags

`windows` · `open-source` · `productivity` · `dotnet` · `hardware` · `raspberry-pi` · `orange-pi` · `homelab` · `usb`

## Suggested Medium title variants

1. MouseKeyProxy: Hotkey-Only Keyboard and Mouse Through a USB HID Appliance  
2. I Built a Free Hotkey Path From Windows to Any USB PC (via a Pi)  
3. Exclusive Keyboard/Mouse Forwarding With Host-Restore-First Safety  

---

*Draft for Medium. Aligned with HID-only product scope in the MouseKeyProxy README and user guide (2026-08-01). Edit tone for your Medium voice; do not invent features beyond the repo docs.*
