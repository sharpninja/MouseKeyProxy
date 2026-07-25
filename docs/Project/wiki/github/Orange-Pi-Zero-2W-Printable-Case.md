# Orange Pi Zero 2W printable case

Parametric OpenSCAD enclosure for the **Orange Pi Zero 2W** (H618), PCB **65 x 30 x 1.2 mm**.

## Design goals

- **Full-length open connector edge** (model front, `y = 0`): dual USB-C + mini-HDMI free for cables.
- U-shaped shell: walls on left, right, and GPIO (rear) only.
- **Board sandwich fasteners** through the four RPi Zero-pattern holes (3.5 mm inset):
  - **Base posts:** run from **outer bottom** up to the PCB; **hex nut** recess is in the post (flush with bottom)
  - **Lid posts:** run from **outer top** down to the PCB; **cylindrical counterbore** for M2.5 socket-head (Allen) screws
  - Thin **floor / lid / walls = 1.6 mm** (LEGO-like plates); recesses are not in thick plates
  - Screw path: SHCS head (post top) → long post → board hole → short post → nut (post bottom)
- **0.2 mm 45° chamfer** on free edges (`edge_chamfer`), applied only after shell + posts (+ ridge) are union-merged into one body (no freestanding post clearance rings)
- **Dummy board** with Type-C + mini-HDMI + microSD for fitment preview.

## Height driven by screw length

```
long_below_lid ≈ screw_length + screw_tip_z - floor_t - short - board - lid_t
outer_h = floor_t + short + board + long + lid_t
```

Defaults:

| Parameter | Default | Role |
|-----------|---------|------|
| `wall` / `floor_t` / `lid_t` | **1.6** | Thin shell plates |
| `screw_length` | **12** | M2.5×12 under-head (DIN 912 / ISO 4762) |
| `head_d` / `head_h` | 4.5 / 2.5 | Cylindrical socket head |
| `short_standoff_h` | 2.2 | Base pad height above floor |
| `min_top_clearance` | 4.0 | Min long post below lid |
| `nut_h` / `nut_af` | 2.0 / 5.0 | Seat in **base post bottom** |
| `hw_recess_gap` | 0.1 | Outside clearance for nut hex and head Ø |
| `post_head_wall` | **1.2** | Radial wall around lid head counterbore (printability) |
| `post_head_boss_below` | **1.6** | Wide boss extends this far below lid underside |
| `long_standoff_od` | **~7.1** | Lid post **boss** OD at head seat |
| `long_standoff_shaft_od` | **~5.2** | Lid post **shaft** OD below the 45° blend |
| `pillar_shaft_clear` | **0.1** | Radial clear around screw shaft in pillars (bore = major + 2×) |
| `edge_chamfer` | **0.2** | 45° setback on free edges (0 = sharp) |

OpenSCAD **echo** reports short/long standoff heights, outer height, and minimum shank.

**Hardware BOM (per case):**

- 4× M2.5 **socket head** Allen screws (cylindrical head), under-head length = `screw_length` (default **12 mm**)
- 4× M2.5 hex nuts

## Files

- `case-body.scad` — parametric geometry (shell, posts, rails, chamfers, dummy board)
- `case.scad` — OpenSCAD GUI driver (`part` = base / lid / preview / fit_*)
- `export-base.scad` / `export-lid.scad` — STL drivers
- `export-3mf.ps1` — OpenSCAD STL export + multi-object 3MF for Orca
- `open-in-orca.ps1` — open latest `zero2w-case.3mf` in Orca Slicer
- `zero2w-case.3mf` — latest plate layout (base + lid, 40 mm gap)

## Generate 3MF (preferred for Orca: base + lid in one file)

Requires OpenSCAD CLI (`winget install --id OpenSCAD.OpenSCAD`).

```powershell
cd cad/orange-pi-zero-2w
pwsh -File .\export-3mf.ps1 -OpenInOrca
```

Produces:

- `zero2w-case.3mf` — two objects: `zero2w-case-base` + `zero2w-case-lid` (lid shifted +X, 40 mm gap)
- `zero2w-case-base.stl` / `zero2w-case-lid.stl` — intermediate meshes

Open the **3MF** in Orca Slicer (not the `.scad`). You should see two separate parts on the plate.

## Generate STL only

```powershell
cd cad/orange-pi-zero-2w
& "C:\Program Files\OpenSCAD\openscad.exe" -o zero2w-case-base.stl export-base.scad
& "C:\Program Files\OpenSCAD\openscad.exe" -o zero2w-case-lid.stl  export-lid.scad
```

OpenSCAD GUI: open `case.scad`, set **part** to `base` / `lid` / `preview`.

## Print settings (starting point)

- PLA or PETG, 0.4 mm nozzle, 0.2 mm layers
- Walls: 3–4 (hex nut traps need solid walls)
- Infill: 25–40% near bosses (or 100% for first 2–3 mm of base)
- Supports: none
- Base: floor down. Lid: outer skin up (export already flipped)

## Lid post stack (printability)

- **Head boss OD** (`long_standoff_od`, ~7.1 mm): through the lid plate and `post_head_boss_below` (1.6 mm) under the underside so the M2.5 socket-head counterbore has ~1.2 mm wall.
- **45° blend** down to **shaft OD** (`long_standoff_shaft_od`, ~5.2 mm) for the rest of the column to the board.
- **U-shaped locate rails** on the lid underside track base wall inners; rails share volume with the plate and are clipped to the same `outer_r` outline.

## Assembly

1. Drop **four M2.5 nuts** into the base hex pockets (from underside), under the short standoffs.
2. Place the Zero 2W on the short standoff pads (connectors toward the open face).
3. Fit the lid so the **long standoffs** land on the PCB around the four holes and the locate rails seat inside the base walls.
4. Install **four M2.5 socket-head (SHCS) Allen screws** from the top: through long posts, board holes, short posts, into the nuts. Heads sit flush in the cylindrical counterbores.
5. Route power / data Type-C and mini-HDMI out the open edge.

## Dummy board / hardware / fitment

| `part` | View |
|--------|------|
| `preview` | Case + dummy board + solid M2.5 screws/nuts |
| `board` | Dummy PCB + connectors only |
| `hardware` | One CSK screw + nut (isolated models) |
| `fit_base` | Base + board + nuts in traps |
| `fit_full` | Translucent case + board + full hardware |
| `fit_check` | Opaque case + board + full hardware (default for fit review) |

**Hardware solids (fit check):**
- `m25_socket_head_screw(shank_l)` — cylindrical Allen head + shank (ISO 4762)
- `m25_hex_nut()` — hex nut (`nut_af` × `nut_h`)
- `hardware_assembly()` — four screws + four nuts seated in the real stack

Head sits in a **cylindrical counterbore** (Ø head + 0.2 mm, depth ≈ head height), top flush with lid; nut flush in base post. Toggle with `show_hardware`.

### Connector layout (dummy board)

Matches official Zero 2W top view (open long edge = Type-C pair + mini-HDMI):

| Port | Param | Default center (board mm) | Shell (W×H×D mm) |
|------|--------|---------------------------|------------------|
| Type-C A | `conn_typec_a_x` | 10.8 | 8.94 × 3.16 × 7.35 |
| Type-C B | `conn_typec_b_x` | 20.7 | same |
| Mini-HDMI | `conn_hdmi_x` | 50.0 | 6.50 × 2.95 × 6.60 |
| microSD | right short edge | `conn_sd_y` = 15 | slot + card keepout |

Case: solid lid (no label/window); GPIO wall closed; **microSD fingertip scoop on the base only**
(`sd_thumb_w`≈14, shallow floor inset). Lid has no SD recess. Nut hex pockets are **flush with the base underside**.

Caliper-check X centers on your PCB and adjust the `conn_*` parameters if silkscreen revision differs.

## Fit notes

- Confirm hole alignment on your PCB with calipers before production.
- Connector ghosts in preview are schematic only.
- Tight XY: raise `board_clearance`. USB shells hit the lip: lower `board_edge_lip` or raise `standoff_h` (may force a longer screw via `min_top_clearance` / stack).
- Loose nut trap: reduce `nut_pocket_clear` (default 0.3). Tight trap: increase it slightly.

## License

Same as the MouseKeyProxy repository (Apache-2.0 project context). CAD is hardware support material for the HID appliance path.
