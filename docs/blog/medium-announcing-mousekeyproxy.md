# MouseKeyProxy: Hotkey-Only Multi-PC Control Without Edge Switching (and a HID Appliance Option)

**Subtitle:** A free, open-source alternative to PowerToys Mouse Without Borders for people who want explicit control handoff, not magic screen edges.

---

If you work across more than one Windows machine (or a Windows host and a “dumb” PC that should only see a USB keyboard and mouse), you already know the pain: either you juggle two keyboards, or you install a remote-control stack that switches focus when the pointer hits the edge of a monitor, or you accept full remote-desktop latency for something as simple as typing into the other box.

**MouseKeyProxy** is my answer to that problem. It is a free, hotkey-only control path: one physical keyboard and mouse, explicit toggle to the remote, and an emergency release that always puts the host back first.

Repository: [github.com/sharpninja/MouseKeyProxy](https://github.com/sharpninja/MouseKeyProxy)

---

## What it is

MouseKeyProxy is a small system of cooperating processes:

1. **Service** — authenticated gRPC host (mTLS) that owns pairing and input effects.
2. **Agent** — Windows tray / dashboard app that owns Win32 hooks, the toggle and emergency hotkeys, and the local link to the service.
3. **`mkp` tool** — the .NET CLI for install, pairing, toggle, status, HID checks, and Pi provisioning.
4. **Optional HID appliance** — the same service on Linux (`linux-arm64`), injecting keyboard and relative mouse through a USB HID gadget instead of a second Windows agent.

The product intent is simple: **when forwarding is active, the remote (or HID target) gets exclusive keyboard and mouse.** When you toggle off, emergency-release, or lose the HID link, **the host is restored first** so a hung peer cannot trap your local machine.

Default hotkeys (both configurable):

- Toggle: **Ctrl+Win+F1**
- Emergency release: **Ctrl+Alt+F3**
- Safety fallbacks: **F1** or **F3** with any two of Ctrl, Alt, and Win

---

## What it is not

- Not edge-of-screen switching.
- Not mirror mode.
- Not a remote desktop for pixels (you still look at the target’s own display or KVM if you need one).
- Not “install software on every machine you want to type into” when you use the HID path: the target PC only sees a USB keyboard and mouse.

---

## Two ways to use it

### 1. Windows peer to Windows peer

Install the service and agent on both machines, pair over mTLS (one-time code or trust-on-first-use discovery), then toggle. Advanced effects such as LIFO clipboard sync are available on that path.

### 2. Windows control host → Pi HID appliance → any USB host

This is the path I use in the lab most often:

- **Control host:** Windows 11 with Agent + `mkp`.
- **Appliance:** Orange Pi Zero 2W (or Raspberry Pi Zero 2 W) running MouseKeyProxy over Wi-Fi or Ethernet gRPC.
- **Target:** whatever PC the Pi’s USB OTG port is plugged into. It does not need MouseKeyProxy installed for keyboard and mouse.

The appliance presents as a composite USB device (HID keyboard + relative mouse, with optional lab USB networking). Letters, digits, and US punctuation map to boot-protocol HID usages so inject works even when the target is at a firmware or login screen that ignores high-level remote tools.

Provisioning is aimed at operators: Nuke/Rufus-oriented SD workflows, `mkp pi provision`, and board-specific HID docs for Orange Pi and Raspberry Pi.

---

## Safety as a product requirement

Remote input tools fail in the worst way: **you lose local keyboard and mouse and cannot get them back.** MouseKeyProxy treats that as a first-class failure mode.

- Toggle, emergency release, and HID link-loss **unhook local capture before** peer RPC.
- A Windows **busy pointer** marks remote ownership; normal cursor returns when local control is restored.
- Pairing is certificate-based (mTLS). Untrusted peers do not get effect RPCs.

Those choices make daily use a little more explicit and a lot less scary.

---

## Hardware and enclosure

Verified lab configurations include Windows 11 x64 control hosts, optional Windows peers, and Linux HID appliances on **Orange Pi Zero 2W** and **Raspberry Pi Zero 2 W**.

For the Zero 2W, the repo also includes a parametric **OpenSCAD case** (base + lid, open connector edge, M2.5 socket-head stack, locate rails, multi-object 3MF for Orca). It is not required to use the software, but it is there if you want a printable shell for the appliance.

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

License: **Apache-2.0** for MouseKeyProxy code. The bundled Rufus fork used for SD writing is GPLv3 (mere aggregation; see third-party notices in the repo).

---

## Why I built it

I wanted multi-machine control that:

1. Never surprises me with edge-triggered focus changes.
2. Always returns local input when something goes wrong.
3. Can drive a machine that should not (or cannot) run my agent software, via a small USB HID appliance.

If that matches how you work, try MouseKeyProxy and open issues or PRs on GitHub. If you only need classic Mouse Without Borders-style edge switching, keep using PowerToys; this project is intentionally a different product shape.

---

## Suggested Medium tags

`windows` · `open-source` · `productivity` · `dotnet` · `hardware` · `raspberry-pi` · `homelab`

## Suggested Medium title variants

1. MouseKeyProxy: Hotkey-Only Multi-PC Control Without Edge Switching  
2. I Built a Free Hotkey KVM-Style Control Path for Windows (and a Pi HID Option)  
3. Exclusive Keyboard/Mouse Forwarding With Host-Restore-First Safety  

---

*Draft for Medium. Facts aligned with the MouseKeyProxy README and user guide as of the docs refresh that ships with this file. Edit tone for your Medium voice; do not invent features beyond the repo docs.*
