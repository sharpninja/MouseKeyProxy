// Orange Pi Zero 2W enclosure - GUI / preview driver
//
// Export 3MF for Orca:
//   pwsh -File export-3mf.ps1 -OpenInOrca

/* [Which part] */
// base | lid | preview | board | fit_base | fit_full | fit_check | hardware
part = "fit_check";

include <case-body.scad>

if (part == "base") {
    case_base();
} else if (part == "lid") {
    case_lid(for_print = false);
} else if (part == "lid_print") {
    case_lid(for_print = true);
} else if (part == "board") {
    dummy_board(at_world = false);
} else if (part == "hardware") {
    hardware_only();
} else if (part == "fit_base") {
    fit_base_board();
} else if (part == "fit_full") {
    fit_full();
} else if (part == "fit_check") {
    fit_check();
} else {
    preview();
}
