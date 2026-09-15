#!/usr/bin/env python3
"""Verify SUNSTUDY/VIEW/SECTION and TABLEGEOMETRY/VERTEX mixed output graphs."""
import argparse
import copy
import json
from pathlib import Path
import ezdxf
from verify_datatable import records, first, check
from verify_view_live_section import public

ROOT = Path(__file__).resolve().parents[1]

def one(wire, kind):
    found = [tags for tags in wire.values() if tags[0] == (0, kind)]
    check(len(found) == 1, "Mixed physical inventory changed: " + kind)
    return found[0]

def body(tags):
    start = next(i for i, tag in enumerate(tags) if tag[0] == 100)
    return tags[start:]

def geometry_payload(target):
    return [(100, "AcDbTableGeometry"), (90, 1), (91, 1), (92, 1),
            (93, 7), (40, 10.5), (41, 20.25), (330, target), (94, 1),
            (10, (1.0, 2.0, 3.0)), (11, (4.0, 5.0, 6.0)),
            (43, 7.0), (44, 8.0), (45, 9.0), (46, 10.0), (95, 11)]

def validate(wire, source_study, before):
    study = one(wire, "SUNSTUDY")
    check(body(study) == body(source_study), "Mixed SUNSTUDY source payload changed")
    for code in (340, 341, 342, 343):
        check(first(study, code) in wire, "SUNSTUDY physical dependency missing")
    view = wire[first(study, 341)]
    check(view[0] == (0, "VIEW"), "SUNSTUDY view identity changed")
    section = one(wire, "SECTIONOBJECT")
    check(section == one(before, "SECTIONOBJECT"), "Complete SECTION identity, geometry or appearance changed")
    check([v for c, v in public(view, "AcDbViewTableRecord") if c == 334] == [first(section, 5)],
          "VIEW live-section reference changed")
    section_body = public(section, "AcDbSection")
    check(first(section_body, 40) == 5.25 and first(section_body, 41) == -15.5,
          "Mixed SECTION geometry changed")
    polyline = one(wire, "POLYLINE")
    vertices = [tags for tags in wire.values() if tags[0] == (0, "VERTEX")]
    before_vertices = [tags for tags in before.values() if tags[0] == (0, "VERTEX")]
    check(len(vertices) == 4, "Explicit topology edit changed VERTEX cardinality")
    check(len(before_vertices) == 4, "Mixed input VERTEX inventory changed")
    check(vertices == [before_vertices[index] for index in (0, 2, 3, 1)],
          "Retained VERTEX identities or metadata changed during explicit reordering")
    expected = [(0, 0, 0), (3, 0, 1), (5, 4, 2), (1, 2, 3)]
    for vertex, position in zip(vertices, expected):
        check(first(vertex, 330) == first(polyline, 5), "VERTEX structural owner changed")
        check(first(vertex, 10) == position,
              "Moved VERTEX identity lost its coordinate slot")
    end = one(wire, "SEQEND")
    check(end == one(before, "SEQEND"), "Retained SEQEND identity or metadata changed")
    check(first(end, 330) == first(polyline, 5), "SEQEND owner changed")
    geometry = one(wire, "TABLEGEOMETRY")
    target = first(vertices[-1], 5)
    check(target == first(before_vertices[1], 5), "TABLEGEOMETRY source identity changed")
    check(body(geometry) == geometry_payload(target), "TABLEGEOMETRY payload or actual VERTEX target changed")
    hatch = one(wire, "HATCH")
    start = next(i for i, (code, _) in enumerate(hatch) if code == 94)
    end_index = next(i for i in range(start + 1, len(hatch)) if hatch[i][0] == 72)
    spline = hatch[start:end_index]
    check(first(spline, 94) == 2, "Mixed HATCH spline degree changed")
    check(first(spline, 73) == 0 and first(spline, 74) == 1, "Mixed HATCH stored flags changed")
    check([v for c, v in spline if c == 40] == [-2, -2, -2, 3, 3, 3], "Mixed HATCH knots changed")
    controls = [value for code, value in spline if code == 10]
    check(controls == [(4, -2), (13, 13), (18, -2)], "Mixed HATCH affine geometry changed")
    check([v for c, v in spline if c == 42] == [-2.5], "Mixed HATCH accepted nonunit weight changed")
    line = hatch[end_index:]
    check(first(line, 72) == 1 and first(line, 10) == (18, -2) and first(line, 11) == (4, -2),
          "Mixed HATCH transformed closing line changed")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    outputs = controls = 0
    for year, profile in ((2013, "AC1027"), (2018, "AC1032")):
        source = ROOT / f"tests/fixtures/sunstudy-producer/typed-carriers/ixmilia-sunstudy-R{year}-ascii-no-dates-hours.dxf"
        original = one(records(source.read_bytes()), "SUNSTUDY")
        for binary in (False, True):
            path = args.artifacts / f"eighth-mixed-AutoCad{year}-{binary}.dxf"
            data = path.read_bytes()
            check(data.startswith(b"AutoCAD Binary DXF") == binary, "Mixed transport changed")
            input_path = args.artifacts / f"eighth-mixed-AutoCad{year}-{binary}-input.dxf"
            input_data = input_path.read_bytes()
            check(input_data.startswith(b"AutoCAD Binary DXF") == binary, "Mixed input transport changed")
            before = records(input_data)
            wire = records(data); validate(wire, original, before)
            drawing = ezdxf.readfile(path)
            check(drawing.dxfversion == profile, "Mixed profile changed")
            audit = drawing.audit()
            check(not audit.errors and not audit.fixes, "Mixed output requires independent audit repair")
            for fault in ("view-target", "geometry-target", "vertex-position", "section-height", "hatch-weight", "study-body",
                          "section-vertex", "section-appearance", "hatch-degree", "hatch-line", "vertex-rewrite", "sequence-rewrite"):
                changed = copy.deepcopy(wire)
                if fault == "view-target":
                    tags = changed[first(one(changed, "SUNSTUDY"), 341)]; code, value = 334, "0"
                elif fault == "geometry-target":
                    tags = one(changed, "TABLEGEOMETRY"); code, value = 330, "0"
                elif fault == "vertex-position":
                    tags = [t for t in changed.values() if t[0] == (0, "VERTEX")][-1]; code, value = 10, (999.0, 2.0, 3.0)
                elif fault == "section-height":
                    tags = one(changed, "SECTIONOBJECT"); code, value = 40, 999.0
                elif fault == "hatch-weight":
                    tags = one(changed, "HATCH"); code, value = 42, 99.0
                elif fault == "hatch-degree":
                    tags = one(changed, "HATCH"); code, value = 94, 3
                elif fault == "hatch-line":
                    tags = one(changed, "HATCH"); code, value = 11, (999.0, -2.0)
                elif fault == "section-vertex":
                    tags = one(changed, "SECTIONOBJECT"); code, value = 11, (999.0, 0.0, 0.0)
                elif fault == "section-appearance":
                    tags = one(changed, "SECTIONOBJECT"); code, value = 91, 99
                elif fault == "sequence-rewrite":
                    tags = one(changed, "SEQEND"); code, value = 5, "7FFFFF00"
                elif fault == "vertex-rewrite":
                    tags = [t for t in changed.values() if t[0] == (0, "VERTEX")][-1]; code, value = 5, "7FFFFF01"
                    geometry = one(changed, "TABLEGEOMETRY")
                    at = [i for i, tag in enumerate(geometry) if tag[0] == 330][-1]
                    geometry[at] = (330, value)
                else:
                    tags = one(changed, "SUNSTUDY"); code, value = 1, "Changed study"
                positions = [i for i, tag in enumerate(tags) if tag[0] == code]
                at = positions[-1] if fault == "geometry-target" else positions[0]
                tags[at] = (code, value)
                try: validate(changed, original, before)
                except ValueError: controls += 1
                else: raise ValueError("Mixed corruption escaped: " + fault)
            outputs += 1
    check(outputs == 4 and controls == 48, "Mixed required inventory changed")
    print(json.dumps({"outputs": outputs, "actual_output_corruptions_rejected": controls,
                      "pre_edit_snapshots": 4,
                      "audit_errors": 0, "audit_repairs": 0,
                      "mixed_links": "explicitly authored; SUNSTUDY body retained from independently produced packet"}))

if __name__ == "__main__":
    main()
