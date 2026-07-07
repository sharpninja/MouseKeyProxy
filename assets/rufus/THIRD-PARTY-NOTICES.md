# Third-Party Notice: bundled `rufus.exe` ("RUFUS For MouseKeyProxy")

The `rufus.exe` in this folder is **"RUFUS For MouseKeyProxy"**, a **modified**
version of Rufus that the MouseKeyProxy project redistributes under the
**GNU General Public License, version 3 (GPLv3)**.

- Original work: Rufus - The Reliable USB Formatting Utility
- Original copyright: Copyright (C) 2011-2026 Pete Batard / Akeo Consulting
- Upstream project: https://rufus.ie and https://github.com/pbatard/rufus
- License: GNU GPL v3 - full text in [`COPYING`](COPYING) beside this file, or
  https://www.gnu.org/licenses/gpl-3.0.html

## Modifications (2026, MouseKeyProxy project)

This binary is a modified fork of Rufus. Notable changes:

- Added a "Configure Pi HID..." dialog and MouseKeyProxy Raspberry Pi Zero 2 HID
  first-boot provisioning (SSH key, Wi-Fi, hostname, user, bearer token, sudo
  policy) with save/load profile support.
- Auto-discovery of a staged Raspberry Pi OS Lite image.
- Product rebrand to "RUFUS For MouseKeyProxy".

## Corresponding source (GPLv3 requirement)

The complete corresponding source for this modified binary is publicly available at:

  https://github.com/sharpninja/rufus-mkp

## Relationship to MouseKeyProxy's own license

MouseKeyProxy's own source is licensed under Apache-2.0. This bundled Rufus
component is a separate program that remains under GPLv3 (mere aggregation on a
storage/distribution medium); the Apache-2.0 license does not apply to it and
the GPLv3 does not apply to MouseKeyProxy's own code.
