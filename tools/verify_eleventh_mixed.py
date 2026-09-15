#!/usr/bin/env python3
"""Check actual legacy-child/TABLE/periodic-HATCH editing graphs and curve geometry.

The native TABLE packet remains stored data. The checks qualify scalar edits,
physical identities, reference release and curve mathematics, not CAD rendering.
"""
import argparse
import copy
import json
from pathlib import Path

import ezdxf
import numpy as np
from ezdxf.math import BSpline, OCS, Vec3
from verify_datatable import records, first, text, check, audit
from verify_hatch_periodic_conversion import near, packet

YEARS = {"AutoCad2013": 2013, "AutoCad2018": 2018}
FILES = ("acad_table_simple.dxf", "acad_table_with_blk_ref.dxf")


def map_names(tags):
    result = []
    for index, tag in enumerate(tags):
        if tag == (1, "CELLSTYLE_BEGIN"):
            check([code for code, _ in tags[index:index + 5]] == [1, 90, 91, 300, 309], "Map entry framing")
            result.append(text(tags[index + 3][1]))
    return result


def validate(wire, expected):
    for key, kind in (("legacy", "POLYLINE"), ("end", "SEQEND"), ("geometry", "TABLEGEOMETRY"),
                      ("hatch", "HATCH"), ("curve", "SPLINE"), ("map", "CELLSTYLEMAP"),
                      ("style", "TABLESTYLE"), ("textStyle", "STYLE"), ("manager", "SECTION_MANAGER")):
        check(wire[expected[key]][0] == (0, kind), "Actual physical identity/type " + key)
    legacy = wire[expected["legacy"]]
    check(first(legacy, 8) == "ELEVENTH_LEGACY" and first(legacy, 70) & 88 == 0, "Legacy 2D representation/role")
    check(first(legacy, 40) == 6. and first(legacy, 41) == 4., "Scaled and reversed inherited legacy widths")
    ordered = list(wire)
    index = ordered.index(expected["legacy"])
    check(ordered[index + 1:index + 6] == expected["records"] + [expected["end"]], "Actual retained VERTEX/SEQEND sequence")
    for handle in expected["records"]:
        check(wire[handle][0] == (0, "VERTEX"), "Retained child type")
    check(first(wire[expected["records"][1]], 91) == 1107, "Edited legacy vertex identifier followed its actual record")
    check(first(wire[expected["records"][2]], 41) == 0., "Reversed explicit zero width override was not omitted")
    check(expected["target"] == expected["records"][-1], "Reversal did not retain the referred actual child")
    target = wire[expected["target"]]
    at = target.index((1001, "ELEVENTH_LINK")) + 1
    end = next((i for i in range(at, len(target)) if target[i][0] == 1001), len(target))
    check(target[at:end] == [(1005, expected[key]) for key in ("hatch", "manager", "curve")], "Ordered exact retained-child XData graph")
    geometry = wire[expected["geometry"]]
    start = geometry.index((100, "AcDbTableGeometry")) + 1
    check(first(geometry[start:], 330) == expected["target"], "TABLEGEOMETRY actual child reference")
    check(map_names(wire[expected["map"]]) == expected["names"], "CELLSTYLEMAP edited names")

    style = wire[expected["style"]]
    start = style.index((100, "AcDbTableStyle")) + 1
    starts = [i for i in range(start, len(style)) if style[i][0] == 7]
    check(style[start:starts[0]] == [(tag["code"], tag["value"]) for tag in expected["styleHeader"]], "Complete edited TABLESTYLE header")
    row_index = expected["row"]
    row = style[starts[row_index]:starts[row_index + 1] if row_index + 1 < len(starts) else len(style)]
    for key, code in (("height", 140), ("alignment", 170), ("textColor", 62), ("fillColor", 63), ("background", 283)):
        check(first(row, code) == expected[key], "TABLESTYLE edited row scalar " + key)
    check(text(first(row, 7)) == expected["textStyleName"], "TABLESTYLE renamed STYLE reference")
    check(text(first(wire[expected["textStyle"]], 2)) == expected["textStyleName"], "Actual renamed STYLE resource")

    curve = wire[expected["curve"]]
    controls = [value for code, value in curve if code == 10]
    knots = [value for code, value in curve if code == 40]
    weights = [value for code, value in curve if code == 41]
    check(first(curve, 70) & 7 == 7 and first(curve, 71) == 2, "Closed rational periodic standalone convention")
    check(len(controls) == len(weights) == 7 and weights == [1.] * 7, "Expanded cyclic controls and weights")
    original = [(0, 0), (4, 7), (10, 1), (7, -5), (-3, -2), (0, 0), (4, 7)]
    ocs = OCS((1, 2, 3)); matrix = np.array([[1., .75, -.2], [0., 1., .5], [0., 0., 1.]])
    wanted_controls = [matrix @ np.array(ocs.to_wcs(Vec3(x, y, 4))) + np.array([7., -11., 2.]) for x, y in original]
    maximum = near(wanted_controls, controls, "Independently transformed original periodic controls")
    check(knots == [i * .25 - 3 for i in range(10)], "Supplied periodic knot domain")
    independent = BSpline(controls, 3, knots, weights)
    scale = (independent.knots()[-1] - independent.knots()[0]) / (knots[-1] - knots[0])
    shift = independent.knots()[0] - scale * knots[0]
    first_knot, last_knot = knots[2], knots[-3]
    wanted = [independent.point((first_knot + (last_knot - first_knot) * i / 32) * scale + shift) for i in range(32)]
    check(len(expected["samples"]) == 32, "Actual evaluator sample inventory")
    maximum = max(maximum, near(wanted, expected["samples"], "Independent evaluation of emitted periodic curve"))
    hatch = wire[expected["hatch"]]
    check(first(hatch, 71) == 1, "HATCH association")
    boundary = packet(hatch, YEARS[expected["version"]])
    check(boundary == expected["hatchPacket"], "Complete HATCH spline packet: flags, knots, weights, fits and tangents")
    check(boundary["periodic"] == 1 and boundary["rational"] == 1 and boundary["degree"] == 2, "Periodic HATCH boundary grammar")
    check(boundary["knots"] == knots and [p[2] for p in boundary["controls"]] == weights, "HATCH and source curve cyclic knots and weights")
    hatch_ocs = OCS(first(hatch, 210)); elevation = first(hatch, 10)[2]
    hatch_controls = [hatch_ocs.to_wcs(Vec3(p[0], p[1], elevation)) for p in boundary["controls"]]
    maximum = max(maximum, near(wanted_controls, hatch_controls, "HATCH stored boundary and standalone world curve agree"))
    wanted_fits = [matrix @ np.array(ocs.to_wcs(Vec3(x, y, 4))) + np.array([7., -11., 2.]) for x, y in ((1, 2), (3, 4))]
    actual_fits = [value for code, value in curve if code == 11]
    hatch_fits = [hatch_ocs.to_wcs(Vec3(p[0], p[1], elevation)) for p in boundary["fits"]]
    maximum = max(maximum, near(wanted_fits, actual_fits, "Independent standalone fit-point transport"), near(wanted_fits, hatch_fits, "Independent HATCH fit-point transport"))
    for field, code, original_tangent in (("start", 12, (1, 2)), ("end", 13, (-3, 4))):
        wanted_tangent = matrix @ np.array(ocs.to_wcs(Vec3(*original_tangent, 0)))
        actual_tangent = first(curve, code); tangent = boundary[field]
        check(tangent is not None, "Stored HATCH tangent presence")
        hatch_tangent = hatch_ocs.to_wcs(Vec3(tangent[0], tangent[1], 0))
        maximum = max(maximum, near(wanted_tangent, actual_tangent, "Independent source tangent"), near(wanted_tangent, hatch_tangent, "Independent HATCH tangent"))
    at = [i for i, tag in enumerate(hatch) if tag[0] == 97][-1]
    check(hatch[at:at + 2] == [(97, 1), (330, expected["curve"])], "HATCH exact source occurrence")
    return maximum


def negative_controls(wire, expected):
    cases = []
    for key, code in (("geometry", 330), ("curve", 70), ("style", 140)):
        changed = copy.deepcopy(wire); row = changed[expected[key]]
        start = row.index((100, "AcDbTableGeometry")) + 1 if key == "geometry" else 0
        at = next(i for i in range(start, len(row)) if row[i][0] == code)
        row[at] = (code, expected["hatch"] if key == "geometry" else row[at][1] + 1)
        cases.append((changed, expected))
    changed = copy.deepcopy(wire); row = changed[expected["map"]]; at = row.index((1, "CELLSTYLE_BEGIN")) + 3
    row[at] = (300, "corrupted actual name"); cases.append((changed, expected))
    changed = copy.deepcopy(wire); row = changed[expected["curve"]]; at = next(i for i, tag in enumerate(row) if tag[0] == 10)
    point = row[at][1]; row[at] = (10, (point[0] + .25, point[1], point[2])); cases.append((changed, expected))
    numeric = copy.deepcopy(expected); numeric["samples"][0][0] += .25; cases.append((wire, numeric))
    for code in (40, 42, 73, 11, 12, 13):
        changed = copy.deepcopy(wire); row = changed[expected["hatch"]]; start = row.index((72, 4)) + 1
        at = next(i for i in range(start, len(row)) if row[i][0] == code)
        value = row[at][1]
        if isinstance(value, tuple):
            value = (value[0] + .125, *value[1:])
        else:
            value = 0 if code == 73 else value + .125
        row[at] = (code, value); cases.append((changed, expected))
    for code in (3, 40):
        changed = copy.deepcopy(wire); row = changed[expected["style"]]; start = row.index((100, "AcDbTableStyle")) + 1
        at = next(i for i in range(start, len(row)) if row[i][0] == code)
        row[at] = (code, "corrupted actual header" if code == 3 else row[at][1] + .125)
        cases.append((changed, expected))
    changed = copy.deepcopy(wire); row = changed[expected["target"]]
    row.remove((1005, expected["manager"])); cases.append((changed, expected))
    for changed, numeric in cases:
        try:
            validate(changed, numeric)
        except (ValueError, KeyError, StopIteration, IndexError):
            continue
        raise ValueError("Independent mixed verifier accepted actual output corruption")
    return len(cases)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    manifests = sorted(args.artifacts.glob("eleventh-mixed-*-edited.dxf.json"))
    check(len(manifests) == 8, "Required mixed native/transport output inventory")
    outputs = 0; controls = 0; maximum = 0.
    for manifest in manifests:
        expected = json.loads(manifest.read_text())
        check(expected["source"] in FILES and expected["version"] in YEARS, "Native source inventory")
        edited = Path(str(manifest)[:-5]); data = edited.read_bytes()
        check(data.startswith(b"AutoCAD Binary DXF") == expected["binary"], "Mixed output transport")
        wire = records(data); maximum = max(maximum, validate(wire, expected))
        drawing = ezdxf.readfile(edited); audit(drawing); outputs += 1
        check(drawing.entitydb[expected["curve"]].get_reactors() == [expected["hatch"]], "Standalone exact HATCH reactor")
        for phase in ("released", "retry"):
            path = Path(str(edited).replace("-edited.dxf", "-" + phase + ".dxf"))
            output = path.read_bytes(); check(output.startswith(b"AutoCAD Binary DXF") == expected["binary"], "Auxiliary mixed transport")
            other = records(output); audit(ezdxf.readfile(path)); outputs += 1
            if phase == "released":
                check(all(handle not in other for handle in expected["records"] + [expected["end"], expected["curve"], expected["hatch"]]), "Retired graph identities reappeared")
        if not expected["binary"]:
            controls += negative_controls(wire, expected)
    print(json.dumps({"outputs": outputs, "edited_native_graphs": len(manifests), "independent_curve_samples": len(manifests) * 32,
                      "maximum_world_error": maximum, "actual_output_negative_controls": controls,
                      "audit_errors": 0, "audit_repairs": 0, "native_cad_execution": False}, sort_keys=True))


if __name__ == "__main__":
    main()
