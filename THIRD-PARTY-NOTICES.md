# Third-Party Notices

MouseKeyProxy (Apache-2.0) redistributes the following third-party components.

## Rufus - "RUFUS For MouseKeyProxy" (`rufus.exe`)

The `mkp` .NET tool bundles a modified build of Rufus (shipped in the package at
`tools/<tfm>/any/payloads/rufus/rufus.exe`, and in this repository at
`assets/rufus/rufus.exe`) used by the `mkp pi provision` command to write the
Raspberry Pi HID image to SD media.

- Original work: Rufus - The Reliable USB Formatting Utility
- Original copyright: Copyright (C) 2011-2026 Pete Batard / Akeo Consulting
- Upstream: https://rufus.ie and https://github.com/pbatard/rufus
- License: **GNU General Public License v3 (GPLv3)** - see
  [`assets/rufus/COPYING`](assets/rufus/COPYING) or
  https://www.gnu.org/licenses/gpl-3.0.html
- Modified: yes. Full details in
  [`assets/rufus/THIRD-PARTY-NOTICES.md`](assets/rufus/THIRD-PARTY-NOTICES.md).
- Corresponding source (GPLv3): https://github.com/sharpninja/rufus-mkp

The bundled Rufus binary remains licensed under GPLv3 (mere aggregation).
MouseKeyProxy's own code stays under Apache-2.0.
