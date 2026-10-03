#!/usr/bin/env python3
"""Verify canonical VIEWPORT group331 references and independent LAYER defaults.

Consumes 36 actual C# source/output/resave drawings. Oracle drawings in the
checker unit tests validate this checker only, not execution of the C# codec.
"""
from __future__ import annotations
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_layer_viewport_defaults import records, one
from verify_raw_line_geometry import audit_signature, key, load_tags, reject, require

PROFILES = {"AutoCad2000": "AC1015", "AutoCad2004": "AC1018", "AutoCad2007": "AC1021",
            "AutoCad2010": "AC1024", "AutoCad2013": "AC1027", "AutoCad2018": "AC1032"}
STAGES = ("source", "output", "resave")


def check_tags(tags, profile, stage):
    require(profile in PROFILES and stage in STAGES, "Unknown frozen-layer fixture profile/stage")
    versions = [i for i, tag in enumerate(tags) if tag == (9, "$ACADVER")]
    require(len(versions) == 1 and tags[versions[0] + 1] == (1, PROFILES[profile]), "Wrong DXF profile")
    rows = records(tags)
    layer_rows = [r for r in rows if r[0] == (0, "LAYER")]
    layers = {one(r, 2): r for r in layer_rows}
    require(len(layers) == len(layer_rows) and set(layers) == {"0", "FROZEN_A", "FROZEN_B"}, "Wrong layer inventory")
    handles = {name: one(row, 5) for name, row in layers.items()}
    require(all(int(value, 16) > 0 for value in handles.values()), "Null layer identity")
    require(len({int(value, 16) for value in handles.values()}) == 3, "Duplicate layer identity")
    require(one(layers["0"], 70) == 0, "Default layer flags changed")
    require(one(layers["FROZEN_A"], 70) == (2 if stage == "source" else 0), "Wrong layer A defaults")
    require(one(layers["FROZEN_B"], 70) == (0 if stage == "source" else 2), "Wrong layer B defaults")
    viewports = [r for r in rows if r[0] == (0, "VIEWPORT") and one(r, 69) != 1]
    require(len(viewports) == 2, "Wrong viewport inventory")
    by_x = {one(r, 10): r for r in viewports}
    require(set(by_x) == {10., 40.}, "Viewport centers missing or duplicated")
    for x, y, width, height in ((10., 20., 6., 4.), (40., 50., 8., 5.)):
        row = by_x[x]
        for code, expected in ((20, y), (30, 0.), (40, width), (41, height), (45, 250.)):
            require(one(row, code) == expected, f"Viewport geometry {code} changed")
        target = "FROZEN_A" if x == 10 and stage == "source" else "FROZEN_B"
        require(one(row, 331) == handles[target], "Missing, duplicate, foreign or stale frozen pointer")
        require(one(row, 8) == "0", "Viewport appearance layer changed")
    retained_layers = tuple((name, tuple(key(t) for t in layers[name] if t[0] != 70)) for name in sorted(layers))
    retained_viewports = tuple((x, tuple(key(t) for t in by_x[x] if not (x == 10 and t[0] == 331))) for x in sorted(by_x))
    return retained_layers, retained_viewports


def check_ezdxf(doc, stage):
    viewports = [v for v in doc.layouts.get("FrozenWire").query("VIEWPORT") if v.dxf.id != 1]
    require(len(viewports) == 2, "Independent reader viewport inventory differs")
    for viewport in viewports:
        target = "FROZEN_A" if viewport.dxf.center.x == 10 and stage == "source" else "FROZEN_B"
        require(viewport.frozen_layers == [target], "Independent reader frozen-layer identity differs")
    require(doc.layers.get("FROZEN_A").dxf.flags == (2 if stage == "source" else 0), "Independent default A differs")
    require(doc.layers.get("FROZEN_B").dxf.flags == (0 if stage == "source" else 2), "Independent default B differs")
    require(not any(audit_signature(doc)), "Independent reader reported audit errors or repairs")


def controls(tags, profile, stage):
    boundaries = [i for i, tag in enumerate(tags) if tag[0] == 0] + [len(tags)]
    begin, end = next((a, b) for a, b in zip(boundaries, boundaries[1:])
                      if tags[a] == (0, "VIEWPORT") and (10, 10.) in tags[a:b])
    index = next(i for i in range(begin, end) if tags[i][0] == 331)
    rejected = 0
    for replacement in ([(331, "0")], [(331, "DEADBEEF")], [], [tags[index], tags[index]]):
        bad = tags[:index] + replacement + tags[index + 1:]
        rejected += reject(lambda: check_tags(bad, profile, stage))
    for code in (10, 20, 40, 41, 45):
        at = next(i for i in range(begin, end) if tags[i][0] == code)
        bad = tags.copy(); bad[at] = (code, tags[at][1] + .5)
        rejected += reject(lambda: check_tags(bad, profile, stage))
    return rejected


def main(directory: Path):
    expected = {f"viewport-frozen-{p}-{t}-{s}.dxf"
                for p, t, s in itertools.product(PROFILES, ("text", "binary"), STAGES)}
    actual = {p.name for p in directory.glob("viewport-frozen-*.dxf")}
    def inventory(names): require(names == expected, "Missing or extra frozen-layer fixture files")
    inventory(actual)
    rejected = reject(lambda: inventory(expected - {min(expected)}))
    rejected += reject(lambda: inventory(expected | {"viewport-frozen-unexpected.dxf"}))
    for profile, transport in itertools.product(PROFILES, ("text", "binary")):
        retained = None
        for stage in STAGES:
            path = directory / f"viewport-frozen-{profile}-{transport}-{stage}.dxf"
            require(path.read_bytes().startswith(b"AutoCAD Binary DXF") == ((transport == "binary") != (stage == "output")), "Wrong transport")
            tags = load_tags(path); signature = check_tags(tags, profile, stage)
            if retained is None: retained = signature
            else: require(retained == signature, "Unselected layer/viewport packets changed")
            check_ezdxf(ezdxf.readfile(path), stage)
            rejected += controls(tags, profile, stage)
    print(f"PASS: {len(expected)} drawings; 72 canonical frozen-layer pointers; {rejected} corruption/inventory controls rejected; "
          "zero independent audit errors/repairs. No native AutoCAD execution is claimed.")


if __name__ == "__main__":
    require(len(sys.argv) == 2, "Usage: verify_viewport_frozen_layers.py ARTIFACTS")
    main(Path(sys.argv[1]))
