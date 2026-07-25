// Orange Pi Zero 2W enclosure (OpenSCAD library body)
// Full-length open face on the connector edge (USB-C power, USB-C OTG, mini-HDMI).
//
// Fastener path uses the four board mounting holes:
//   Lid: long posts run from outer lid TOP down to board top;
//        cylindrical counterbore for M2.5 socket-head (Allen) screw in post top
//   Board hole
//   Base: short posts run from outer base BOTTOM up to board underside; hex nut in post bottom
//
// Floor and lid plates are thin (1.6 mm). Nut/head recesses live in the posts.
// Height is driven by screw under-head length (ISO 4762 / DIN 912 callout).
//
// Units: mm. FDM 0.4 mm nozzle / 0.2 mm layers.

/* [Fasteners - drive stack height] */
// ISO 4762 / DIN 912 M2.5 socket head cap screw (cylindrical Allen head).
// screw_length = under-head shank length (e.g. M2.5x12 => 12 mm under head).
screw_length = 12;
// Nominal major diameter of M2.5 thread (solid shank for fit / bore calc)
screw_major_d = 2.5;
// Radial clearance around screw shaft inside base/lid pillars only (per side).
// Pillar bore Ø = screw_major_d + 2 * pillar_shaft_clear.
pillar_shaft_clear = 0.1;
screw_clear_d = screw_major_d + 2 * pillar_shaft_clear; // 2.7 mm through posts
// Cylindrical head (nominal ISO 4762 M2.5)
head_d = 4.5;            // head diameter
head_h = 2.5;            // head height
allen_af = 2.0;          // hex socket across flats
allen_depth = 1.2;       // socket depth
// Hex nut ISO 4032 M2.5 (pocket in bottom of short posts, flush with base outer face)
nut_af = 5.0;            // nominal across-flats
nut_h = 2.0;
// Print/fit gap outside the metal: 0.1 mm per side
// Pocket hex AF = nut_af + 2*gap; head counterbore D = head_d + 2*gap
hw_recess_gap = 0.1;
// Tip of screw ends this far above outer base bottom (inside the nut)
screw_tip_z = 0.4;

// Derived pocket sizes (shape of nut / head + outside gap)
nut_pocket_af = nut_af + 2 * hw_recess_gap;
head_recess_d = head_d + 2 * hw_recess_gap;
head_recess_h = head_h + 0.05; // slight extra so head sits fully home
nut_pocket_h = nut_h + 0.05;
// Overall screw length (head + shank) for tip placement
screw_overall = head_h + screw_length;

/* [Board] */
board_l = 65.0;
board_w = 30.0;
board_t = 1.2;
hole_inset = 3.5;
// PCB hole diameter (M2.5 clearance on Zero-class boards is ~2.7–2.75)
board_hole_d = 2.75;

/* [Standoffs / posts] */
// Short posts (base): outer bottom -> pad under PCB; nut recess in post bottom
short_standoff_h = 2.2;  // height of pad above floor top
// Post OD: clear hex pocket (vertex diameter of hex ≈ AF / cos(30°)) + wall
short_standoff_od = max(5.4, nut_pocket_af / cos(30) + 1.2);
// Long posts (lid): outer lid top -> pad on PCB top; cylindrical head counterbore in post top.
// Wide boss only through the lid plate + post_head_boss_below under the underside;
// shaft below that stays at long_standoff_shaft_od. 45° cone blends the two ODs.
// Radial wall around head seat must be FDM-printable (0.4 mm nozzle: >= 2–3 perimeters).
post_head_wall = 1.2;
post_head_boss_below = 1.6;  // wide OD extends this far below lid underside (z=0)
long_standoff_od = max(head_recess_d + 2 * post_head_wall, short_standoff_od);
// Default wide: 4.7 + 2*1.2 = 7.1 mm OD
// Shaft OD (board-side column) — prior full-length post diameter
long_standoff_shaft_od = max(head_recess_d + 0.4, 5.2);
// 45° blend height = radial step (run = rise)
long_standoff_blend_h = (long_standoff_od - long_standoff_shaft_od) / 2;
// Minimum air above PCB (length of long post below lid underside)
min_top_clearance = 4.0;
// Non-zero seating gaps so solids do not interpenetrate in fit check
board_seat_gap = 0.10;   // air between long-post tip and PCB top
part_fit_gap = 0.10;     // general keepout around board/connectors vs case
// Preview colors so posts stand out from the shell (F5; ignored by STL/3MF export)
post_color_base = [0.95, 0.45, 0.08];  // orange — base short posts
post_color_lid  = [0.15, 0.55, 0.95];  // blue  — lid long posts

/* [Case shell] */
// LEGO-like shell: every plate is the same thickness (no extra lips/shelves/skirts)
wall = 1.6;
floor_t = wall;
lid_t = wall;
board_clearance = 0.6;
board_edge_lip = 0;      // no bars / lips
outer_r = 2.5;
// 45° edge chamfer: setback along each face meeting an edge (0 = sharp).
// Applied to final base/lid solids. Needs min feature thickness > 2*edge_chamfer
// (wall/floor/lid 1.6 and ridge 0.8 are safe at 0.2).
edge_chamfer = 0.2;
// Lid underside locate ridge: follows inside of base walls at the joint
lid_ridge_h = 0.8;       // protrusion down from lid underside
lid_ridge_w = 1.6;       // rib thickness (grows inward from the gap face)
lid_ridge_gap = 0.1;     // clearance outside ridge to base wall inner face

/* [GPIO edge] */
// Closed wall by default (no header window). Set true only if a 40-pin header must exit.
gpio_slot = false;
gpio_slot_margin = 1.5;

/* [Ventilation] */
vents = true;
vent_slot_w = 1.4;
vent_slot_len = 14.0;
vent_count = 4;

/* [Dummy board / connectors - fitment] */
// Layout from Orange Pi Zero 2W top-view product photos / interface map:
//   Open long edge (y=0): two Type-C side-by-side near left, mini-HDMI toward right.
//   Right short edge (x=board_l): microSD slot.
//   Rear long edge (y=board_w): 40-pin GPIO footprint (case closed by default).
//   Left short edge (x=0): 24-pin FPC (under-side flex; optional keepout).
// Dimensions: USB-IF Type-C shell + common mini-HDMI / microSD mid-mount footprints.
show_dummy_board = true;

// USB Type-C receptacle shell (USB-IF outer envelope ~8.94 x 3.16; mid-mount depth ~7.35)
typec_w = 8.94;
typec_h = 3.16;
typec_d = 7.35;          // shell depth from mating face back onto board
typec_overhang = 0.85;   // mating face past PCB edge (edge/mid-mount)

// Mini-HDMI (Type C) receptacle shell (typical SMT mid-mount)
hdmi_w = 6.50;
hdmi_h = 2.95;
hdmi_d = 6.60;
hdmi_overhang = 0.70;

// MicroSD push-push / friction slot body (short-edge mount)
sd_w = 14.0;             // along board length (x), measured into board from edge
sd_h = 1.85;
sd_d = 15.0;             // along board width (y)
sd_overhang = 0.5;       // slight protrusion of slot mouth past PCB short edge
// Card stick-out when inserted (keepout for case wall notch)
sd_card_overhang = 2.5;

// Centers along board X (origin = corner at open edge + left short edge).
// Derived from official top-view layout (two Type-C clustered left, HDMI right).
// Left mounting hole at x=3.5; right at x=61.5.
conn_typec_a_x = 10.8;   // Type-C nearer left (power / USB pair)
conn_typec_b_x = 20.7;   // second Type-C immediately beside first (center ~shell+1mm)
conn_hdmi_x = 50.0;      // mini-HDMI toward right hole
// MicroSD mouth center on right short edge
conn_sd_y = 15.0;        // mid-board in Y (slot on x = board_l edge)

// Optional 40-pin header keepout on GPIO edge (y = board_w). Off when case closed.
gpio_header = false;
gpio_header_h = 5.5;
gpio_header_w = 51.0;
gpio_header_d = 5.0;

// microSD access: modest thumb scoop in the BASE only (right short wall + floor).
// Lid stays solid on that edge (no matching window).
sd_thumb_access = true;
// Sized for nail/fingertip under the card — not a full palm scoop
sd_thumb_w = 14.0;           // along board Y (slightly wider than microSD card ~11 mm)
sd_thumb_depth = 7.0;        // how far the scoop cuts into the case from outer right wall
sd_thumb_floor_inset = 6.0;  // floor cut depth under the card for under-grip

// ---------------------------------------------------------------------------
// Stack math (cylindrical socket head)
//
//   outer top  [cylindrical head in counterbore, top flush with lid outer]
//              [shank through long post + board + short post into nut]
//   outer bot  [hex nut flush with base outer]
//
// Head top at z=outer_h, tip at z=screw_tip_z:
//   screw_overall = head_h + screw_length = outer_h - screw_tip_z
//   long = screw_overall + screw_tip_z - floor - short - board - lid
//        = head_h + screw_length + screw_tip_z - floor - short - board - lid
// ---------------------------------------------------------------------------
_long_raw = head_h + screw_length + screw_tip_z - floor_t - short_standoff_h - board_t - lid_t;
long_standoff_h = max(_long_raw, min_top_clearance);

_min_shank = min_top_clearance + floor_t + short_standoff_h + board_t + lid_t - head_h - screw_tip_z;
echo(str(
    "M2.5 SHCS stack: shank=", screw_length,
    " mm; head Ø/h=", head_d, "/", head_h,
    " mm; floor/lid/wall=", floor_t, "/", lid_t, "/", wall,
    " mm; short_post_pad=", short_standoff_h,
    " mm; long_below_lid=", long_standoff_h,
    " mm; outer_h=", floor_t + short_standoff_h + board_t + long_standoff_h + lid_t,
    " mm; min_shank≈", _min_shank, " mm (M2.5x", ceil(_min_shank), ")"
));
if (_long_raw + 0.05 < min_top_clearance) {
    echo(str(
        "WARNING: shank too short for min_top_clearance; clamped long_standoff_h=",
        long_standoff_h, ". Prefer M2.5x", ceil(_min_shank), " socket head."
    ));
}

// Cavity height = short + board + long (floor top to lid underside)
// Long post solid stops board_seat_gap above the PCB so it does not dig into the board.
inner_h = short_standoff_h + board_t + long_standoff_h;
outer_h = floor_t + inner_h + lid_t;
top_clearance = long_standoff_h;
// Actual solid length of long post below lid underside
long_post_below = max(0.5, long_standoff_h - board_seat_gap);

// Footprint: open connector face at y=0
inner_l = board_l + 2 * board_clearance;
inner_w = board_w + 2 * board_clearance;
outer_l = inner_l + 2 * wall;
outer_w = inner_w + wall;

board_ox = wall + board_clearance;
board_oy = board_clearance;
board_oz = floor_t + short_standoff_h;

// Board-local hole centers -> world XY
function hole_world(i) =
    let (p = [
        [hole_inset, hole_inset],
        [board_l - hole_inset, hole_inset],
        [hole_inset, board_w - hole_inset],
        [board_l - hole_inset, board_w - hole_inset]
    ][i])
    [board_ox + p[0], board_oy + p[1]];

// ---------------------------------------------------------------------------
// Primitives
// ---------------------------------------------------------------------------
module rounded_rect(size, r) {
    x = size[0];
    y = size[1];
    rr = min(r, x / 2 - 0.01, y / 2 - 0.01);
    offset(r = rr)
        offset(delta = -rr)
            square([x, y]);
}

module hex_2d(af) {
    r = af / sqrt(3);
    circle(r = r, $fn = 6);
}

// Extrude a 2D profile with 45° top and bottom outer-edge chamfers of setback c.
// Overall height stays h; top/bottom faces shrink by c (45° faces).
// When c <= 0 or h <= 2c, falls back to a plain extrusion.
module linear_extrude_chamfered(h, c = edge_chamfer) {
    if (c <= 0 || h <= 2 * c + 0.02) {
        linear_extrude(height = h)
            children();
    } else {
        // Bottom chamfer: inset face at z=0 -> full profile at z=c
        hull() {
            linear_extrude(height = 0.01)
                offset(delta = -c)
                    children();
            translate([0, 0, c])
                linear_extrude(height = 0.01)
                    children();
        }
        // Straight mid section
        translate([0, 0, c])
            linear_extrude(height = h - 2 * c)
                children();
        // Top chamfer: full profile at z=h-c -> inset face at z=h
        hull() {
            translate([0, 0, h - c - 0.01])
                linear_extrude(height = 0.01)
                    children();
            translate([0, 0, h - 0.01])
                linear_extrude(height = 0.01)
                    offset(delta = -c)
                        children();
        }
    }
}

// 45° internal lip chamfer at the TOP of a cylindrical hole (opens the lip).
// Negative solid; place on axis. z0 = top plane of the hole opening.
module hole_top_chamfer_neg(r, c = edge_chamfer, z0 = 0) {
    if (c > 0) {
        translate([0, 0, z0 - c])
            cylinder(h = c + 0.02, r1 = r, r2 = r + c, $fn = 48);
    }
}

// 45° internal lip chamfer at the BOTTOM of a cylindrical hole.
// z0 = bottom plane of the hole opening.
module hole_bot_chamfer_neg(r, c = edge_chamfer, z0 = 0) {
    if (c > 0) {
        translate([0, 0, z0 - 0.02])
            cylinder(h = c + 0.02, r1 = r + c, r2 = r, $fn = 48);
    }
}

// Outer-rim 45° chamfer cutter at the TOP of a cylinder (negative for difference).
// Removes the sharp outer corner at z=z_top; diameter at top becomes d-2c.
module cyl_outer_top_chamfer_neg(d, c = edge_chamfer, z_top = 0) {
    if (c > 0) {
        translate([0, 0, z_top - c])
            difference() {
                cylinder(h = c + 0.05, d = d + 1, $fn = 48);
                cylinder(h = c + 0.05, d1 = d, d2 = max(0.2, d - 2 * c), $fn = 48);
            }
    }
}

// Outer-rim 45° chamfer cutter at the BOTTOM of a cylinder (negative).
module cyl_outer_bot_chamfer_neg(d, c = edge_chamfer, z_bot = 0) {
    if (c > 0) {
        translate([0, 0, z_bot - 0.05])
            difference() {
                cylinder(h = c + 0.05, d = d + 1, $fn = 48);
                translate([0, 0, 0.05])
                    cylinder(h = c, d1 = max(0.2, d - 2 * c), d2 = d, $fn = 48);
            }
    }
}

// Outer top perimeter 45° chamfer cutter for a rounded_rect solid of height h.
// Applied AFTER bodies are merged (cuts the merged solid).
module outer_top_perimeter_chamfer_neg(h, size, r, c = edge_chamfer) {
    if (c > 0) {
        difference() {
            translate([0, 0, h - c])
                linear_extrude(height = c + 0.05)
                    rounded_rect(size, r);
            hull() {
                translate([0, 0, h - c])
                    linear_extrude(height = 0.01)
                        rounded_rect(size, r);
                translate([0, 0, h - 0.01])
                    linear_extrude(height = 0.01)
                        offset(delta = -c)
                            rounded_rect(size, r);
            }
        }
    }
}

// Outer bottom perimeter 45° chamfer cutter for a rounded_rect solid on z=0.
module outer_bot_perimeter_chamfer_neg(size, r, c = edge_chamfer) {
    if (c > 0) {
        difference() {
            translate([0, 0, -0.05])
                linear_extrude(height = c + 0.05)
                    rounded_rect(size, r);
            hull() {
                translate([0, 0, -0.05])
                    linear_extrude(height = 0.01)
                        offset(delta = -c)
                            rounded_rect(size, r);
                translate([0, 0, c])
                    linear_extrude(height = 0.01)
                        rounded_rect(size, r);
            }
        }
    }
}

// 45° triangular-prism cutters for all 12 edges of an axis-aligned box.
// size = [sx, sy, sz]; origin at (0,0,0) corner of the box.
// Use inside difference() to chamfer a rectangular solid (e.g. ridge ribs).
module box_edge_chamfer_45_neg(size, c = edge_chamfer) {
    if (c > 0) {
        sx = size[0];
        sy = size[1];
        sz = size[2];

        // Bottom z=0
        translate([0, 0, 0])
            rotate([0, 90, 0])
                linear_extrude(height = sx)
                    polygon([[0, 0], [-c, 0], [0, c]]);
        translate([0, sy, 0])
            rotate([0, 90, 0])
                linear_extrude(height = sx)
                    polygon([[0, 0], [-c, 0], [0, -c]]);
        translate([0, 0, 0])
            rotate([-90, 0, 0])
                linear_extrude(height = sy)
                    polygon([[0, 0], [c, 0], [0, -c]]);
        translate([sx, 0, 0])
            rotate([-90, 0, 0])
                linear_extrude(height = sy)
                    polygon([[0, 0], [-c, 0], [0, -c]]);

        // Top z=sz
        translate([0, 0, sz])
            rotate([0, 90, 0])
                linear_extrude(height = sx)
                    polygon([[0, 0], [c, 0], [0, c]]);
        translate([0, sy, sz])
            rotate([0, 90, 0])
                linear_extrude(height = sx)
                    polygon([[0, 0], [c, 0], [0, -c]]);
        translate([0, 0, sz])
            rotate([-90, 0, 0])
                linear_extrude(height = sy)
                    polygon([[0, 0], [c, 0], [0, c]]);
        translate([sx, 0, sz])
            rotate([-90, 0, 0])
                linear_extrude(height = sy)
                    polygon([[0, 0], [-c, 0], [0, c]]);

        // Vertical
        translate([0, 0, 0])
            linear_extrude(height = sz)
                polygon([[0, 0], [c, 0], [0, c]]);
        translate([sx, 0, 0])
            linear_extrude(height = sz)
                polygon([[0, 0], [-c, 0], [0, c]]);
        translate([0, sy, 0])
            linear_extrude(height = sz)
                polygon([[0, 0], [c, 0], [0, -c]]);
        translate([sx, sy, 0])
            linear_extrude(height = sz)
                polygon([[0, 0], [-c, 0], [0, -c]]);
    }
}

// Chamfer the top inner rim of the base cavity (U-walls open at y=0).
// zt = top of walls; cuts into the solid from the cavity side.
module base_cavity_top_inner_chamfer_neg(c = edge_chamfer) {
    if (c > 0) {
        zt = floor_t + inner_h;
        // Left wall inner face x=wall, edge along Y from 0 to outer_w-wall
        translate([wall, 0, zt])
            rotate([-90, 0, 0])
                linear_extrude(height = outer_w - wall)
                    polygon([[0, 0], [-c, 0], [0, -c]]);
        // Right wall inner face x=outer_l-wall
        translate([outer_l - wall, 0, zt])
            rotate([-90, 0, 0])
                linear_extrude(height = outer_w - wall)
                    polygon([[0, 0], [c, 0], [0, -c]]);
        // Rear wall inner face y=outer_w-wall, edge along X from wall to outer_l-wall
        translate([wall, outer_w - wall, zt])
            rotate([0, 90, 0])
                linear_extrude(height = inner_l)
                    polygon([[0, 0], [c, 0], [0, -c]]);
    }
}

// Chamfer top outer rim of open-face wall ends (front free edges at y≈0).
module base_open_edge_chamfer_neg(c = edge_chamfer) {
    if (c > 0) {
        zt = floor_t + inner_h;
        // Left wall front free edge (x from 0..wall, y=0, z=zt) — top front corner
        translate([0, 0, zt])
            rotate([0, 90, 0])
                linear_extrude(height = wall)
                    polygon([[0, 0], [c, 0], [0, c]]);
        // Right wall front free edge
        translate([outer_l - wall, 0, zt])
            rotate([0, 90, 0])
                linear_extrude(height = wall)
                    polygon([[0, 0], [c, 0], [0, c]]);
        // Floor front edge at open face (y=0, z=0) along X — bottom exterior
        translate([0, 0, 0])
            rotate([0, 90, 0])
                linear_extrude(height = outer_l)
                    polygon([[0, 0], [-c, 0], [0, c]]);
        // Floor front top edge at cavity start (y=0, z=floor_t) along inner opening
        translate([wall, 0, floor_t])
            rotate([0, 90, 0])
                linear_extrude(height = inner_l)
                    polygon([[0, 0], [c, 0], [0, c]]);
    }
}

// Cylindrical counterbore for socket head — pure cylinder, never a cone.
// Caller places origin at the BOTTOM of the head pocket; +z toward outer top.
module head_counterbore_negative(d_head, h_head, d_shank) {
    // Head pocket only (cylindrical). Shank hole is cut separately full-length.
    cylinder(h = h_head, d = d_head, $fn = 48);
}

// ---------------------------------------------------------------------------
// Dummy board + connectors (fitment reference)
// ---------------------------------------------------------------------------
module dummy_typec() {
    // Shell on PCB top; mating face past open edge (y negative)
    translate([-typec_w / 2, -typec_overhang, 0])
        cube([typec_w, typec_d, typec_h]);
}

module dummy_hdmi() {
    translate([-hdmi_w / 2, -hdmi_overhang, 0])
        cube([hdmi_w, hdmi_d, hdmi_h]);
}

module dummy_microsd() {
    // Slot body on top of PCB, mouth on right short edge (x = board_l)
    translate([board_l - sd_w + sd_overhang, conn_sd_y - sd_d / 2, board_t])
        cube([sd_w, sd_d, sd_h]);
    // Inserted card stick-out past short edge (fitment keepout)
    translate([board_l + sd_overhang, conn_sd_y - 11.0 / 2, board_t + 0.15])
        cube([sd_card_overhang, 11.0, 0.8]);
}

module dummy_board(at_world = true) {
    module body() {
        // PCB
        difference() {
            cube([board_l, board_w, board_t]);
            for (i = [0 : 3]) {
                p = [
                    [hole_inset, hole_inset],
                    [board_l - hole_inset, hole_inset],
                    [hole_inset, board_w - hole_inset],
                    [board_l - hole_inset, board_w - hole_inset]
                ][i];
                translate([p[0], p[1], -0.1])
                    cylinder(h = board_t + 0.2, d = board_hole_d, $fn = 24);
            }
        }

        // Open long edge (y=0): two Type-C clustered left, mini-HDMI right
        // (matches Orange Pi Zero 2W product top view)
        translate([conn_typec_a_x, 0, board_t])
            dummy_typec();
        translate([conn_typec_b_x, 0, board_t])
            dummy_typec();
        translate([conn_hdmi_x, 0, board_t])
            dummy_hdmi();

        // Right short edge: microSD
        dummy_microsd();

        // GPIO header on rear edge (optional keepout)
        if (gpio_header) {
            translate([
                (board_l - gpio_header_w) / 2,
                board_w - gpio_header_d + 0.5,
                board_t
            ])
                cube([gpio_header_w, gpio_header_d, gpio_header_h]);
        }

        // SoC keepout (H618 region, center-right of board topside)
        translate([28, 10, board_t])
            cube([14, 12, 1.5]);
        // LPDDR package keepout (right of SoC toward SD)
        translate([44, 11, board_t])
            cube([10, 10, 1.2]);
    }

    if (at_world) {
        translate([board_ox, board_oy, board_oz])
            body();
    } else {
        body();
    }
}

// ---------------------------------------------------------------------------
// Base: short standoffs over nuts at board holes
//
// Additive single solid (no cavity-then-reinsert posts — that leaves a ring):
//   1. Floor plate
//   2. Posts with DEEP volume overlap into the floor (not coplanar touch)
//   3. Walls with open-face cavity (does not re-cut post columns)
//   4. Subtract bores / nuts / vents / scoops
// Chamfers run only after render() of this solid.
// ---------------------------------------------------------------------------

module base_post_solids() {
    // Posts run from outer bottom (z=0) through the floor and up to the pad.
    // Overlap with the floor plate is the full floor_t height (real volume union).
    post_h = floor_t + short_standoff_h;
    for (i = [0 : 3]) {
        p = hole_world(i);
        translate([p[0], p[1], 0])
            cylinder(h = post_h, d = short_standoff_od, $fn = 48);
    }
}

// RAW single-body base (sharp). Additive floor + posts + walls.
module case_base_merged(colored_posts = false) {
    post_h = floor_t + short_standoff_h;

    difference() {
        union() {
            // Floor plate
            linear_extrude(height = floor_t)
                rounded_rect([outer_l, outer_w], outer_r);

            // Posts (overlap into floor — one continuous solid after union)
            base_post_solids();

            // Walls only: outer ring from floor top up (cavity open at y=0)
            difference() {
                translate([0, 0, floor_t])
                    linear_extrude(height = inner_h)
                        rounded_rect([outer_l, outer_w], outer_r);
                translate([wall, -0.25, floor_t - 0.05])
                    cube([inner_l, inner_w + 0.25, inner_h + 0.15]);
            }
        }

        // Screw bores + nut traps
        for (i = [0 : 3]) {
            p = hole_world(i);
            translate([p[0], p[1], 0]) {
                translate([0, 0, -0.1])
                    cylinder(h = post_h + 0.2, d = screw_clear_d, $fn = 32);
                translate([0, 0, -0.05])
                    linear_extrude(height = nut_pocket_h + 0.05)
                        hex_2d(nut_pocket_af);
            }
        }

        if (vents) {
            pitch = (board_l - 10) / max(1, vent_count - 1);
            for (i = [0 : vent_count - 1]) {
                vx = board_ox + 5 + i * pitch;
                vy = board_oy + board_w / 2 - vent_slot_len / 2;
                translate([vx, vy, -0.05])
                    cube([vent_slot_w, vent_slot_len, floor_t + 0.1]);
            }
        }

        if (gpio_slot) {
            gh = gpio_header ? gpio_header_h : top_clearance;
            translate([
                board_ox + gpio_slot_margin,
                outer_w - wall - 0.05,
                floor_t + short_standoff_h + board_t - 0.2
            ])
                cube([
                    board_l - 2 * gpio_slot_margin,
                    wall + 0.1,
                    gh + 1.2
                ]);
        }

        if (sd_thumb_access) {
            ty0 = board_oy + conn_sd_y - sd_thumb_w / 2;
            wall_open_h = short_standoff_h + board_t + sd_h + sd_card_overhang + 2.0;
            translate([
                outer_l - wall - 0.05,
                ty0,
                floor_t - 0.05
            ])
                cube([
                    wall + 0.2 + max(sd_card_overhang, 1.0) + 0.5,
                    sd_thumb_w,
                    wall_open_h
                ]);

            translate([
                outer_l - sd_thumb_floor_inset,
                ty0,
                -0.05
            ])
                cube([sd_thumb_floor_inset + 0.1, sd_thumb_w, floor_t + 0.15]);

            translate([
                outer_l - sd_thumb_depth,
                ty0,
                floor_t - 0.05
            ])
                cube([
                    sd_thumb_depth - wall + 0.2,
                    sd_thumb_w,
                    short_standoff_h + 0.8
                ]);
        }
    }
}

// Preview helpers
module case_base_shell_raw() {
    difference() {
        case_base_merged();
        translate([0, 0, floor_t])
            for (i = [0 : 3]) {
                p = hole_world(i);
                translate([p[0], p[1], 0])
                    cylinder(
                        h = short_standoff_h + 0.1,
                        d = short_standoff_od + 0.05,
                        $fn = 48
                    );
            }
    }
}
module case_base_posts_raw() {
    intersection() {
        case_base_merged();
        translate([0, 0, -0.01])
            for (i = [0 : 3]) {
                p = hole_world(i);
                translate([p[0], p[1], 0])
                    cylinder(
                        h = floor_t + short_standoff_h + 0.02,
                        d = short_standoff_od + 0.05,
                        $fn = 48
                    );
            }
    }
}

// All chamfer cutters for the base, applied only AFTER merge.
module case_base_chamfer_negs(c = edge_chamfer) {
    if (c <= 0) {
        // nothing
    } else {
        base_h = floor_t + inner_h;
        post_h = floor_t + short_standoff_h;
        r_clear = screw_clear_d / 2;

        // Outer shell perimeter (top + bottom)
        outer_top_perimeter_chamfer_neg(base_h, [outer_l, outer_w], outer_r, c);
        outer_bot_perimeter_chamfer_neg([outer_l, outer_w], outer_r, c);

        // Cavity top inner rim + open-face free edges
        base_cavity_top_inner_chamfer_neg(c);
        base_open_edge_chamfer_neg(c);

        // Posts: free top outer rim + bore lips + nut mouth (bottom is fused into floor)
        for (i = [0 : 3]) {
            p = hole_world(i);
            translate([p[0], p[1], 0]) {
                cyl_outer_top_chamfer_neg(short_standoff_od, c, z_top = post_h);
                hole_top_chamfer_neg(r_clear, c, z0 = post_h);
                hole_bot_chamfer_neg(r_clear, c, z0 = 0);
                // Nut pocket mouth entry on outer bottom
                translate([0, 0, -0.02])
                    linear_extrude(
                        height = c + 0.02,
                        scale = nut_pocket_af / (nut_pocket_af + 2 * c)
                    )
                        hex_2d(nut_pocket_af + 2 * c);
            }
        }

        // Vent slot lips
        if (vents) {
            pitch = (board_l - 10) / max(1, vent_count - 1);
            for (i = [0 : vent_count - 1]) {
                vx = board_ox + 5 + i * pitch;
                vy = board_oy + board_w / 2 - vent_slot_len / 2;
                hull() {
                    translate([vx - c, vy - c, -0.02])
                        cube([vent_slot_w + 2 * c, vent_slot_len + 2 * c, 0.02]);
                    translate([vx, vy, c])
                        cube([vent_slot_w, vent_slot_len, 0.02]);
                }
                hull() {
                    translate([vx, vy, floor_t - c])
                        cube([vent_slot_w, vent_slot_len, 0.02]);
                    translate([vx - c, vy - c, floor_t - 0.01])
                        cube([vent_slot_w + 2 * c, vent_slot_len + 2 * c, 0.04]);
                }
            }
        }

        // microSD thumb scoop free edges
        if (sd_thumb_access) {
            ty0 = board_oy + conn_sd_y - sd_thumb_w / 2;
            wall_open_h = short_standoff_h + board_t + sd_h + sd_card_overhang + 2.0;
            translate([outer_l, ty0, floor_t])
                linear_extrude(height = wall_open_h)
                    polygon([[0, 0], [-c, 0], [0, c]]);
            translate([outer_l, ty0 + sd_thumb_w, floor_t])
                linear_extrude(height = wall_open_h)
                    polygon([[0, 0], [-c, 0], [0, -c]]);
            translate([outer_l - wall, ty0, floor_t + wall_open_h])
                rotate([-90, 0, 0])
                    linear_extrude(height = sd_thumb_w)
                        polygon([[0, 0], [c, 0], [0, c]]);
        }
    }
}

// Public base: render() forces CGAL to fuse the additive union into one
// polyhedron BEFORE any chamfer cutter runs.
module case_base(colored_posts = true) {
    difference() {
        render(convexity = 20)
            case_base_merged(colored_posts = colored_posts);
        case_base_chamfer_negs(edge_chamfer);
    }
}

// Back-compat aliases used by previews / older call sites
module case_base_shell() { case_base_shell_raw(); }
module case_base_posts(colored = true) {
    if (colored) {
        color(post_color_base) case_base_posts_raw();
    } else {
        case_base_posts_raw();
    }
}
module short_standoff_with_nut() {
    // Slice one post out of the single-body base at hole 0
    p = hole_world(0);
    translate([-p[0], -p[1], 0])
        intersection() {
            case_base_merged();
            translate([p[0], p[1], -0.01])
                cylinder(
                    h = floor_t + short_standoff_h + 0.02,
                    d = short_standoff_od + 0.05,
                    $fn = 48
                );
        }
}

// ---------------------------------------------------------------------------
// Lid: long posts from outer TOP through thin lid plate down to board
//
// Plate + alignment rails are ONE solid: extrude a blank from -ridge_h to
// lid_t, then carve the underside everywhere except the U-rail footprint.
// Posts are added into that solid. No separate ridge body to leave a seam.
// render() then chamfer free edges only.
// ---------------------------------------------------------------------------

// Enlarged connector solids (world coords) used to carve keepouts from posts
module connector_keepouts_world() {
    g = part_fit_gap;
    module grow_box(x, y, z, sx, sy, sz) {
        translate([x - g, y - g, z - g])
            cube([sx + 2 * g, sy + 2 * g, sz + 2 * g]);
    }
    grow_box(
        board_ox + conn_typec_a_x - typec_w / 2,
        board_oy - typec_overhang,
        board_oz + board_t,
        typec_w, typec_d, typec_h
    );
    grow_box(
        board_ox + conn_typec_b_x - typec_w / 2,
        board_oy - typec_overhang,
        board_oz + board_t,
        typec_w, typec_d, typec_h
    );
    grow_box(
        board_ox + conn_hdmi_x - hdmi_w / 2,
        board_oy - hdmi_overhang,
        board_oz + board_t,
        hdmi_w, hdmi_d, hdmi_h
    );
    grow_box(
        board_ox + board_l - sd_w + sd_overhang,
        board_oy + conn_sd_y - sd_d / 2,
        board_oz + board_t,
        sd_w + sd_card_overhang, sd_d, sd_h
    );
}

// 2D U footprint of the alignment rails (locate into base wall inners).
// Rear bar is FULL width so L-corners are solid overlapping squares, not
// face-touch only.
//
// Clipped to the same radiused outline as the lid plate. Without that, the
// square rail ends at y=0 cut across the front corner radii (outer_r) and
// leave a sharp edge where the lid radius should run all the way to the front.
module lid_rail_u_2d() {
    g = lid_ridge_gap;
    rw = lid_ridge_w;
    x_left  = wall + g;
    x_right = outer_l - wall - g;
    y_rear  = outer_w - wall - g;
    y_front = 0;

    intersection() {
        union() {
            // Left
            translate([x_left, y_front])
                square([rw, y_rear - y_front]);
            // Right
            translate([x_right - rw, y_front])
                square([rw, y_rear - y_front]);
            // Rear full width (overlaps both side bars in the corner squares)
            translate([x_left, y_rear - rw])
                square([x_right - x_left, rw]);
        }
        // Same outer path as the lid plate (all four corners use outer_r)
        rounded_rect([outer_l, outer_w], outer_r);
    }
}

// Plate + alignment rails as ONE solid:
//   1) full plate disc (z = 0 .. lid_t)
//   2) U-rail prism extruded from z = -rh THROUGH the plate to z = lid_t
// The U and plate share the full plate thickness as volume overlap, so the
// rail "top" cannot form a coplanar sticker seam on the plate underside.
//
// Do NOT bore post clearances here. Oversized holes (OD+slack) with posts of
// only OD leave an air ring and multi-volume meshes. Posts are unioned into
// this solid later and share volume with plate + rails directly.
module lid_plate_with_rails() {
    rh = lid_ridge_h;

    union() {
        // Plate owns the outer top face
        linear_extrude(height = lid_t)
            rounded_rect([outer_l, outer_w], outer_r);

        // U-rails: continuous prism from free tip up through the plate
        translate([0, 0, -rh])
            linear_extrude(height = lid_t + rh)
                lid_rail_u_2d();
    }
}

// One lid post: wide boss (head seat) + 45° blend + narrow shaft to the board.
// Lid-local: z=0 = underside, +z = outer top, -z toward PCB.
//   wide:  z in [-post_head_boss_below, lid_t - post_top_inset]
//   blend: 45° frustum, height = (od_wide - od_shaft) / 2
//   shaft: remainder down to -long_post_below
module lid_post_solid() {
    post_top_inset = 0.05;
    z_top = lid_t - post_top_inset;
    z_wide_bot = -post_head_boss_below;
    z_blend_bot = z_wide_bot - long_standoff_blend_h;
    z_tip = -long_post_below;
    // Small overlaps so unions stay manifold
    ov = 0.05;

    // Wide boss through plate + boss_below under underside
    translate([0, 0, z_wide_bot - ov])
        cylinder(
            h = z_top - (z_wide_bot - ov),
            d = long_standoff_od,
            $fn = 48
        );

    // 45° blend: wide OD at z_wide_bot -> shaft OD at z_blend_bot
    if (long_standoff_blend_h > 0.01) {
        translate([0, 0, z_blend_bot])
            cylinder(
                h = long_standoff_blend_h + ov,
                d1 = long_standoff_shaft_od,
                d2 = long_standoff_od,
                $fn = 48
            );
    }

    // Narrow shaft to board pad
    shaft_h = (z_blend_bot + ov) - z_tip;
    if (shaft_h > 0.01) {
        translate([0, 0, z_tip])
            cylinder(
                h = shaft_h,
                d = long_standoff_shaft_od,
                $fn = 48
            );
    }
}

// RAW single-body lid (sharp).
module case_lid_merged(colored_posts = false) {
    // Posts stop a hair below the lid outer top so the plate owns z = lid_t.
    post_top_inset = 0.05;

    difference() {
        intersection() {
            union() {
                // Plate + alignment rails: ONE solid (rails carved, not glued on)
                lid_plate_with_rails();

                // Posts: wide head boss + 45° blend + narrow shaft
                for (i = [0 : 3]) {
                    p = hole_world(i);
                    translate([p[0], p[1], 0])
                        lid_post_solid();
                }
            }
            // Nothing above lid outer top
            translate([-5, -5, -long_post_below - 1])
                cube([
                    outer_l + 10,
                    outer_w + 10,
                    long_post_below + 1 + lid_t
                ]);
        }

        // Shank bores + pure cylindrical head counterbores
        for (i = [0 : 3]) {
            p = hole_world(i);
            translate([p[0], p[1], 0]) {
                translate([0, 0, -long_post_below - 0.1])
                    cylinder(
                        h = long_post_below + lid_t + 0.2,
                        d = screw_clear_d,
                        $fn = 32
                    );
                translate([0, 0, lid_t - head_recess_h])
                    cylinder(
                        h = head_recess_h + 0.05,
                        d = head_recess_d,
                        $fn = 48
                    );
            }
        }

        translate([0, 0, -(floor_t + inner_h)])
            connector_keepouts_world();

        if (gpio_slot) {
            translate([
                board_ox + gpio_slot_margin,
                outer_w - wall - 0.2,
                -0.1
            ])
                cube([
                    board_l - 2 * gpio_slot_margin,
                    wall + 0.5,
                    lid_t + 0.2
                ]);
        }
    }
}

// Free-tip chamfer for the whole U-rail at once (z = -rh).
// Uses offset() on the U 2D profile so every free edge (inner + outer) insets
// the same way.
module lid_ridge_chamfer_negs(c = edge_chamfer) {
    if (c > 0) {
        rh = lid_ridge_h;
        difference() {
            translate([0, 0, -rh - 0.05])
                linear_extrude(height = c + 0.05)
                    lid_rail_u_2d();
            hull() {
                translate([0, 0, -rh - 0.05])
                    linear_extrude(height = 0.01)
                        offset(delta = -c)
                            lid_rail_u_2d();
                translate([0, 0, -rh + c])
                    linear_extrude(height = 0.01)
                        lid_rail_u_2d();
            }
        }
    }
}

// 45° chamfer on the plate/rail L-step edges at z = 0 (circled sharp edges).
// Triangle prism along the edge: into the rail and down into the rail body.
// Polyhedron keeps axis orientation explicit (no rotate/extrude mapping bugs).

// Edge parallel to +Y at (x, y0..y0+len, z=0). into_x = sign into the rail solid.
module lid_rail_step_edge_y(x, y0, len, c, into_x) {
    // Triangle in XZ: (0,0), (into_x*c, 0), (0, -c), extruded along Y
    polyhedron(
        points = [
            [x,             y0,       0],
            [x,             y0 + len, 0],
            [x + into_x * c, y0,       0],
            [x + into_x * c, y0 + len, 0],
            [x,             y0,      -c],
            [x,             y0 + len,-c]
        ],
        faces = [
            [0, 2, 4], [1, 5, 3],
            [0, 1, 3, 2],
            [0, 4, 5, 1],
            [2, 3, 5, 4]
        ],
        convexity = 2
    );
}

// Edge parallel to +X at (x0..x0+len, y, z=0). into_y = sign into the rail solid.
module lid_rail_step_edge_x(x0, y, len, c, into_y) {
    polyhedron(
        points = [
            [x0,       y,              0],
            [x0 + len, y,              0],
            [x0,       y + into_y * c, 0],
            [x0 + len, y + into_y * c, 0],
            [x0,       y,             -c],
            [x0 + len, y,             -c]
        ],
        faces = [
            [0, 2, 4], [1, 5, 3],
            [0, 1, 3, 2],
            [0, 4, 5, 1],
            [2, 3, 5, 4]
        ],
        convexity = 2
    );
}

module lid_rail_step_chamfer_negs(c = edge_chamfer) {
    if (c > 0) {
        g = lid_ridge_gap;
        rw = lid_ridge_w;
        x_left  = wall + g;
        x_right = outer_l - wall - g;
        y_rear  = outer_w - wall - g;
        y_len   = y_rear;
        x_len   = x_right - x_left;

        // Outer U edges (plate/rail L-step, outside of U)
        lid_rail_step_edge_y(x_left,  0, y_len, c, +1); // left outer
        lid_rail_step_edge_y(x_right, 0, y_len, c, -1); // right outer
        lid_rail_step_edge_x(x_left, y_rear, x_len, c, -1); // rear outer

        // Inner U edges (cavity side of U)
        lid_rail_step_edge_y(x_left + rw,  0, y_len, c, -1);
        lid_rail_step_edge_y(x_right - rw, 0, y_len, c, +1);
        lid_rail_step_edge_x(x_left, y_rear - rw, x_len, c, +1);

        // Front free ends of side bars (y = 0), top edges at z = 0
        lid_rail_step_edge_x(x_left,       0, rw, c, +1);
        lid_rail_step_edge_x(x_right - rw, 0, rw, c, +1);
    }
}

// Chamfers AFTER render-fused solid.
// DO chamfer:
//   - alignment-rail free tips (z = -rh)
//   - plate/rail L-step edges at z = 0 (circled sharp edges)
//   - post free tips
// Head counterbores stay pure cylinders.
// Lid outer shell perimeter left sharp (full-perimeter cutters fight the rails).
module case_lid_chamfer_negs(c = edge_chamfer) {
    if (c > 0) {
        r_clear = screw_clear_d / 2;
        z_tip = -long_post_below;

        // Alignment rail free tips
        lid_ridge_chamfer_negs(c);

        // Plate/rail step edges (the circled sharp edges)
        lid_rail_step_chamfer_negs(c);

        // Post free tips only (board side) — shaft OD, not the head boss
        for (i = [0 : 3]) {
            p = hole_world(i);
            translate([p[0], p[1], 0]) {
                cyl_outer_bot_chamfer_neg(long_standoff_shaft_od, c, z_bot = z_tip);
                hole_bot_chamfer_neg(r_clear, c, z0 = z_tip);
            }
        }
    }
}

// Public lid: render() fuses additive union, then chamfer.
module case_lid(for_print = false, colored_posts = true) {
    module lid_body() {
        difference() {
            render(convexity = 20)
                case_lid_merged(colored_posts = colored_posts);
            case_lid_chamfer_negs(edge_chamfer);
        }
    }

    if (for_print) {
        translate([0, outer_w, lid_t])
            rotate([180, 0, 0])
                lid_body();
    } else {
        lid_body();
    }
}

// Back-compat aliases
module long_standoff_with_head() {
    p = hole_world(0);
    translate([-p[0], -p[1], 0])
        intersection() {
            case_lid_merged();
            translate([p[0], p[1], -long_post_below - 0.01])
                cylinder(
                    h = long_post_below + lid_t + 0.02,
                    d = long_standoff_od + 0.05,
                    $fn = 48
                );
        }
}

// Note: long_standoff_od = head-boss OD; long_standoff_shaft_od = column OD.
module lid_locate_ridge() {
    // Ridge-only slice from single-body lid
    intersection() {
        case_lid_merged();
        translate([-1, -1, -lid_ridge_h - 0.05])
            cube([outer_l + 2, outer_w + 2, lid_ridge_h + 0.1]);
    }
}
module case_lid_plate() {
    intersection() {
        case_lid_merged();
        translate([-1, -1, -0.01])
            cube([outer_l + 2, outer_w + 2, lid_t + 0.02]);
    }
}
module case_lid_shell() {
    intersection() {
        case_lid_merged();
        translate([-1, -1, -lid_ridge_h - 0.05])
            cube([outer_l + 2, outer_w + 2, lid_t + lid_ridge_h + 0.1]);
    }
}
module case_lid_posts(colored = true) {
    module posts() {
        difference() {
            case_lid_merged();
            translate([-1, -1, -0.01])
                cube([outer_l + 2, outer_w + 2, lid_t + 0.02]);
            translate([-1, -1, -lid_ridge_h - 0.05])
                cube([outer_l + 2, outer_w + 2, lid_ridge_h + 0.1]);
        }
    }
    if (colored) {
        color(post_color_lid) posts();
    } else {
        posts();
    }
}

// ---------------------------------------------------------------------------
// Hardware models (M2.5) for fit / interference check
// ---------------------------------------------------------------------------
/* [Hardware model] */
// Slight radial clear so solid hardware does not z-fight case bores in preview
// (screw_major_d is defined under Fasteners; pillar bore uses pillar_shaft_clear)
hw_fit_slack = 0.02;
// Show solid screws+nuts in preview / fit modes
show_hardware = true;

// ISO 4032 style hex nut (flat-to-flat = nut_af, height = nut_h)
module m25_hex_nut() {
    difference() {
        linear_extrude(height = nut_h)
            hex_2d(nut_af);
        translate([0, 0, -0.05])
            cylinder(h = nut_h + 0.1, d = screw_major_d + 0.15, $fn = 24);
    }
}

// ISO 4762 / DIN 912 M2.5 socket head cap screw (cylindrical Allen head).
// Origin: tip at z=0; shank then cylindrical head; overall = head_h + shank.
// shank_l = under-head length (matches screw_length / commercial "M2.5xN").
module m25_socket_head_screw(shank_l = screw_length) {
    difference() {
        union() {
            // Shank
            cylinder(h = shank_l + 0.01, d = screw_major_d, $fn = 28);
            // Cylindrical head (not conical)
            translate([0, 0, shank_l])
                cylinder(h = head_h, d = head_d, $fn = 32);
        }
        // Hex Allen socket in top of head
        translate([0, 0, shank_l + head_h - allen_depth + 0.02])
            linear_extrude(height = allen_depth)
                hex_2d(allen_af);
    }
}

// Place one nut in base trap (world coords), flush with outer bottom (z ≈ 0)
module hardware_nut_at(i) {
    p = hole_world(i);
    translate([p[0], p[1], hw_fit_slack])
        m25_hex_nut();
}

// Place one SHCS through full stack. Head top flush with lid outer top.
// tip_z = outer_h - screw_overall
module hardware_screw_at(i) {
    p = hole_world(i);
    z_tip = outer_h - screw_overall;
    translate([p[0], p[1], z_tip])
        m25_socket_head_screw(screw_length);
}

// All four fasteners in assembly position
module hardware_assembly() {
    for (i = [0 : 3]) {
        color([0.72, 0.74, 0.78])
            hardware_screw_at(i);
        color([0.55, 0.58, 0.62])
            hardware_nut_at(i);
    }
}

// Hardware only (for isolated check / export)
module hardware_only() {
    color([0.72, 0.74, 0.78])
        m25_socket_head_screw(screw_length);
    color([0.55, 0.58, 0.62])
        translate([10, 0, 0])
            m25_hex_nut();
    translate([22, 0, 0]) {
        color([0.55, 0.58, 0.62])
            m25_hex_nut();
        color([0.72, 0.74, 0.78])
            translate([0, 0, outer_h - screw_overall])
                m25_socket_head_screw(screw_length);
    }
}

// ---------------------------------------------------------------------------
// Preview / fit check
// ---------------------------------------------------------------------------
module preview() {
    case_base();
    translate([0, 0, floor_t + inner_h + 0.05])
        case_lid(for_print = false);
    if (show_dummy_board) {
        color([0.15, 0.55, 0.25, 0.55])
            dummy_board(at_world = true);
    }
    if (show_hardware) {
        hardware_assembly();
    }
}

// Fitment-only: base + dummy board (no lid) for connector / lip checks
module fit_base_board() {
    case_base();
    color([0.15, 0.55, 0.25, 0.7])
        dummy_board(at_world = true);
    if (show_hardware) {
        for (i = [0 : 3]) {
            color([0.55, 0.58, 0.62])
                hardware_nut_at(i);
        }
    }
}

// Assembled case (base + lid) with optional tiny lid lift for visual separation
module assembled_case(lid_lift = 0) {
    case_base();
    translate([0, 0, floor_t + inner_h + lid_lift])
        case_lid(for_print = false);
}

// Red solids = interpenetration between board(+connectors) and case.
// Empty result means no geometric collision (within mesh resolution).
module collision_board_vs_case() {
    color([1, 0.1, 0.1, 0.85])
        intersection() {
            dummy_board(at_world = true);
            assembled_case(lid_lift = 0);
        }
}

// Fitment: translucent case + solid board + solid screws/nuts + collision highlight
module fit_full() {
    color([0.7, 0.75, 0.85, 0.28])
        assembled_case(lid_lift = 0);
    color([0.1, 0.6, 0.2, 0.85])
        dummy_board(at_world = true);
    hardware_assembly();
    collision_board_vs_case();
}

// Opaque case + board + hardware + red collision overlay
module fit_check() {
    assembled_case(lid_lift = 0);
    color([0.1, 0.55, 0.2, 0.55])
        dummy_board(at_world = true);
    hardware_assembly();
    collision_board_vs_case();
}

// ---------------------------------------------------------------------------
$fn = 48;
// No top-level geometry. Drivers call case_base / case_lid / preview / dummy_board.
