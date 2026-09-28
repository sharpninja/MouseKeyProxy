# HID control transfer diagnosis, 2026-09-27

The published tray Agent is running as PID 113768 in interactive session 1. `MouseKeyProxy.Repl.exe agent status --json` returned `remoteState: Connected`, endpoint `https://192.168.0.172:50051`, and `forwardingActive: false` at 22:38 UTC. This proves the saved pairing and authenticated network channel, not USB HID transfer to OMARCHY.

The tray `forwarder.log` recorded `DEVICE_HID_DISCONNECTED: UDC state=not attached` for a real input event at 22:06:56 UTC. An authenticated one-event zero-delta MouseMove probe at 22:20:43 UTC returned the same error. That probe exercised the injector's UDC gate, but it did not write a HID report: `PiHidEncoder.EncodeMouseMove` emits no report for `(dx=0, dy=0)`. A zero-event RPC bypasses even the UDC gate.

The user confirmed the cable is on the Pi Zero 2W's inner data/OTG USB-C port and reseated it. The one-event neutral probe after reseating timed out after 5 seconds, so it was not a success. The user then changed the OMARCHY USB port. A fresh one-event neutral probe at 22:31:23 UTC returned `ok:false`, `err:DEVICE_HID_DISCONNECTED`, `message:DEVICE_HID_DISCONNECTED: UDC state=not attached`. A different known data-capable USB-C cable remains to be confirmed.

`GetDeviceConfiguration` returned keyboard and mouse enabled and file-share enabled at 22:21:27 UTC. This shows requested gadget configuration, not target enumeration. OMARCHY is PAYTON-DESKTOP per MCP memory. Host SSH to OMARCHY at 192.168.0.149 stalled before authentication, so target USB enumeration was not inspected. The Pi SSH login was unavailable; its current sysfs gadget binding and service logs were not read directly. The physical USB cause is therefore not yet confirmed, and control transfer is not verified.

At that point, Agent Connected state and a zero-event RPC were insufficient to verify HID transfer. The next check was a one-event probe after the port change, with Pi UDC state and OMARCHY USB enumeration still needed if that probe failed.

## Retest, 2026-09-27 23:41 UTC

An authenticated one-event zero-delta MouseMove probe at 2026-09-27T23:41:02.5144208Z returned `ok:true`, an empty error, and `message:ok`. This showed that the Pi's UDC gate passed at that instant. Because the encoder emits no report for zero delta, it did not test `/dev/hidg1` or OMARCHY input. The physical change that allowed the UDC gate to pass is unknown; the user asked for a retest after changing the OMARCHY USB port, but did not confirm a cable replacement.

The current Release CLI `toggle` returned `[AGENT toggle] ok=True` with forwarding enabled. `agent status --json` then reported Connected and `forwardingActive:true`. `emergency-release --json` succeeded, restoring local input; the subsequent Agent status remained Connected with `forwardingActive:false`. These results verify the local Agent control path and saved pairing status. They do not establish that a Pi HID report was written or that OMARCHY visibly responded to input.

The globally installed `mkp` 0.5.5 was older than the current source. Running `mkp toggle --help` unexpectedly executed its direct gRPC toggle, printed `[REAL bidi via transport] toggle FAILED` with a remote certificate rejection, and left Agent status Connected with forwarding off. For an installed CLI that prints this prefix, use the tray hotkey or dashboard until a matching CLI is installed. Do not reset a working tray pairing based on this older CLI result.