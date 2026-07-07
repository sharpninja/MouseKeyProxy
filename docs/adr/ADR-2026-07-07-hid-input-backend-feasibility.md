# ADR-2026-07-07: HID input backend feasibility

## Status
Accepted

## Context
MouseKeyProxy needs a durable answer on whether a future keyboard/mouse proxy backend can move from Win32 input injection to a HID-shaped backend without requiring paid signing keys or Microsoft driver validation.

The 2026-07-07 review checked current Microsoft documentation for HID transports, keyboard/mouse HID client drivers, Virtual HID Framework, and kernel driver signing.

## Decision
A physical USB or Bluetooth HID-compliant keyboard/mouse device is a valid future backend path without a custom Windows driver, signing key, or Microsoft driver validation. MouseKeyProxy can treat this as a hardware appliance path: the device presents standard HID keyboard/mouse top-level collections over an in-box Windows HID transport.

A software-only virtual HID keyboard/mouse backend is not a valid no-signing/no-dashboard path. On Windows, Virtual HID Framework requires a HID source driver in kernel mode, and public kernel-mode drivers on Windows 10+ must be Microsoft-signed through the Hardware Dev Center flow. This means a virtual HID driver path needs driver signing/dashboard work and cannot be assumed to install on ordinary customer machines without that investment.

## Consequences
- Prefer continuing the Win32 `SendInput`/Raw Input backend for the current software-only implementation.
- If a HID backend becomes important, scope it as either:
  - A physical microcontroller/appliance that enumerates as USB HID/Bluetooth HID keyboard and mouse.
  - A signed Windows driver product with explicit budget for EV certificate, Partner Center/Hardware Dev Center setup, attestation or certification, installer, update, and support burden.
- Do not plan a user-mode-only virtual HID keyboard/mouse backend as a supported Windows production path.

## Evidence
- Windows includes in-box HID transport minidrivers for USB, Bluetooth, Bluetooth LE, I2C, GPIO, and SPI, and Microsoft recommends using the included drivers for those transports: https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-transports
- Microsoft states vendor drivers are not required for keyboards and mice compliant with supported HID usages/top-level collections, and also lists HID-standard devices as not requiring vendor drivers: https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/keyboard-and-mouse-hid-client-drivers
- VHF is for a HID source driver and Microsoft notes that VHF supports HID source drivers only in kernel mode: https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-
- Microsoft kernel-mode code signing requirements state that virtual drivers have the same requirements as hardware drivers, and Windows 10 version 1607+ will not load new kernel-mode drivers unless signed by Microsoft through Hardware Dev Center certification or attestation: https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-requirements--windows-vista-and-later-
- The 2026 driver code signing requirements page states that Hardware Dev Center submissions require a signing certificate and that attestation/WHCP submission requires an EV certificate associated with the dashboard account: https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-reqs