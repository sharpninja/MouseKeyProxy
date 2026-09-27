# HID control transfer diagnosis, 2026-09-27

The published tray Agent is running as PID 113768 in interactive session 1. `MouseKeyProxy.Repl.exe agent status --json` returned `remoteState: Connected`, endpoint `https://192.168.0.172:50051`, and `forwardingActive: false` at 22:38 UTC. This proves the saved pairing and authenticated network channel, not USB HID transfer to OMARCHY.

The tray `forwarder.log` recorded `DEVICE_HID_DISCONNECTED: UDC state=not attached` for a real input event at 22:06:56 UTC. An authenticated one-event neutral zero-delta MouseMove probe at 22:20:43 UTC returned the same error. A zero-event RPC is not a HID-write test because `InjectInput` bypasses the injector for an empty batch.

The user confirmed the cable is on the Pi Zero 2W's inner data/OTG USB-C port and reseated it. The one-event neutral probe after reseating timed out after 5 seconds, so it was not a success. The user then changed the OMARCHY USB port. A fresh one-event neutral probe at 22:31:23 UTC returned `ok:false`, `err:DEVICE_HID_DISCONNECTED`, `message:DEVICE_HID_DISCONNECTED: UDC state=not attached`. A different known data-capable USB-C cable remains to be confirmed.

`GetDeviceConfiguration` returned keyboard and mouse enabled and file-share enabled at 22:21:27 UTC. This shows requested gadget configuration, not target enumeration. OMARCHY is PAYTON-DESKTOP per MCP memory. Host SSH to OMARCHY at 192.168.0.149 stalled before authentication, so target USB enumeration was not inspected. The Pi SSH login was unavailable; its current sysfs gadget binding and service logs were not read directly. The physical USB cause is therefore not yet confirmed, and control transfer is not verified.

Do not reset pairing or mark the HID transfer fixed on the basis of Agent Connected state or a zero-event RPC. Recheck after a known data-capable cable change, then verify Pi UDC state and OMARCHY USB enumeration if the one-event probe still fails.