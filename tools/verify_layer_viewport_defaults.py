#!/usr/bin/env python3
"""Verify LAYER new-viewport defaults, independent of global freeze and visibility.

Requires the 42 source/output/resave drawings produced by LayerViewportDefaultTests.
The source generation deliberately carries the informational referenced flag (64);
output toggles only the new-viewport default (2), and resave retains that edit.
These fixtures are not native AutoCAD execution evidence.
"""
from __future__ import annotations

import itertools
from pathlib import Path
import sys

import ezdxf
from verify_raw_line_geometry import audit_signature, key, load_tags, reject, require

PROFILES = {
    "AutoCad12": "AC1009", "AutoCad2000": "AC1015", "AutoCad2004": "AC1018",
    "AutoCad2007": "AC1021", "AutoCad2010": "AC1024", "AutoCad2013": "AC1027",
    "AutoCad2018": "AC1032",
}
STAGES = ("source", "output", "resave")


def layer_name(flags: int, visible: bool) -> str:
    return f"VP_{flags}_{int(visible)}"


def records(tags: list[tuple]) -> list[list[tuple]]:
    rows: list[list[tuple]] = []
    for tag in tags:
        if tag[0] == 0:
            rows.append([])
        require(bool(rows), "DXF must start with a group 0 record")
        rows[-1].append(tag)
    return rows


def one(row: list[tuple], code: int):
    values = [value for group, value in row if group == code]
    require(len(values) == 1, f"Missing or duplicate group {code}")
    return values[0]


def check_tags(tags: list[tuple], profile: str, stage: str) -> tuple:
    require(profile in PROFILES and stage in STAGES, "Unknown fixture profile or stage")
    positions = [i for i, tag in enumerate(tags) if tag == (9, "$ACADVER")]
    require(len(positions) == 1 and positions[0] + 1 < len(tags)
            and tags[positions[0] + 1] == (1, PROFILES[profile]), "Wrong DXF version")
    rows = records(tags)
    layers = [row for row in rows if row[0] == (0, "LAYER")]
    expected_names = {"0"} | {layer_name(flags, visible)
                              for flags in range(8) for visible in (False, True)}
    require(len(layers) == len(expected_names), "Wrong layer inventory")
    by_name = {one(row, 2): row for row in layers}
    require(set(by_name) == expected_names, "Missing, duplicate or unexpected layer")
    require(one(by_name["0"], 70) == 0, "Default layer acquired a frozen/locked default")
    lines = [row for row in rows if row[0] == (0, "LINE")]
    require(len(lines) == 32, "Wrong LINE inventory")
    for flags, visible in itertools.product(range(8), (False, True)):
        name = layer_name(flags, visible)
        expected_flags = flags | 64 if stage == "source" else flags ^ 2
        row = by_name[name]
        require(one(row, 70) == expected_flags, f"Wrong flags for {name} at {stage}")
        require(one(row, 62) == (5 if visible else -5), "Layer visibility/color changed")
        require(str(one(row, 6)).upper() == "CONTINUOUS", "Layer linetype changed")
        shared = [line for line in lines if one(line, 8) == name]
        require(len(shared) == 2, "Shared layer references changed")
        x = flags * 2 + int(visible)
        for y in (2, 12):
            candidates = [line for line in shared if one(line, 20) == y]
            require(len(candidates) == 1, "Missing or duplicate geometry carrier")
            line = candidates[0]
            expected = {10: float(x), 20: float(y), 30: float(y + 1),
                        11: x + .5, 21: float(y + 3), 31: float(y + 4)}
            for code, value in expected.items():
                require(one(line, code) == value, f"LINE component {code} changed")
    # Layer identity, names, nonflag values and all LINE tags must survive each save.
    retained_layers = tuple((name, tuple(key(tag) for tag in by_name[name] if tag[0] != 70))
                            for name in sorted(by_name))
    retained_lines = tuple(tuple(map(key, row)) for row in lines)
    return retained_layers, retained_lines


def check_ezdxf(doc, stage: str) -> None:
    for flags, visible in itertools.product(range(8), (False, True)):
        layer = doc.layers.get(layer_name(flags, visible))
        expected = flags | 64 if stage == "source" else flags ^ 2
        require(layer.dxf.flags == expected, "Independent reader's layer flags differ")
        require(layer.is_frozen() == bool(flags & 1), "Global freezing changed")
        require(layer.is_locked() == bool(flags & 4), "Layer locking changed")
        require(layer.is_off() == (not visible), "Layer visibility changed")
        require(bool(layer.dxf.flags & 2) == bool(expected & 2), "New-viewport default differs")
    require(not any(audit_signature(doc)), "Independent audit reported errors or repairs")


def controls(tags: list[tuple], profile: str, stage: str) -> int:
    boundaries = [i for i, tag in enumerate(tags) if tag[0] == 0] + [len(tags)]
    start, end = next((a, b) for a, b in zip(boundaries, boundaries[1:])
                      if tags[a] == (0, "LAYER") and (2, "VP_0_0") in tags[a:b])
    flag_at, = [i for i in range(start, end) if tags[i][0] == 70]
    color_at, = [i for i in range(start, end) if tags[i][0] == 62]
    failures = 0
    for bit in (1, 2, 4, 16, 64):
        bad = list(tags); bad[flag_at] = (70, int(bad[flag_at][1]) ^ bit)
        failures += reject(lambda: check_tags(bad, profile, stage))
    bad = list(tags); bad.pop(flag_at)
    failures += reject(lambda: check_tags(bad, profile, stage))
    bad = list(tags); bad.insert(flag_at, bad[flag_at])
    failures += reject(lambda: check_tags(bad, profile, stage))
    bad = list(tags); bad[color_at] = (62, 5)
    failures += reject(lambda: check_tags(bad, profile, stage))
    line_at = tags.index((0, "LINE"))
    coordinate_at = next(i for i in range(line_at + 1, len(tags)) if tags[i][0] == 10)
    bad = list(tags); bad[coordinate_at] = (10, float(bad[coordinate_at][1]) + 1)
    failures += reject(lambda: check_tags(bad, profile, stage))
    return failures


def names() -> set[str]:
    return {f"layer-viewport-default-{profile}-{transport}-{stage}.dxf"
            for profile, transport, stage in itertools.product(PROFILES, ("text", "binary"), STAGES)}


def main(directory: Path) -> None:
    expected = names()
    def inventory(actual):
        require(actual == expected, "Missing or extra layer-viewport-default drawings")
    inventory({path.name for path in directory.glob("layer-viewport-default-*.dxf")})
    rejected = reject(lambda: inventory(expected - {min(expected)}))
    rejected += reject(lambda: inventory(expected | {"layer-viewport-default-extra.dxf"}))
    for profile, transport in itertools.product(PROFILES, ("text", "binary")):
        retained = None
        for stage in STAGES:
            path = directory / f"layer-viewport-default-{profile}-{transport}-{stage}.dxf"
            require(path.read_bytes().startswith(b"AutoCAD Binary DXF")
                    == ((transport == "binary") != (stage == "output")), "Wrong transport")
            tags = load_tags(path)
            current = check_tags(tags, profile, stage)
            if retained is None:
                retained = current
            else:
                require(current == retained, "Unselected layer fields, identity or geometry changed")
            check_ezdxf(ezdxf.readfile(path), stage)
            rejected += controls(tags, profile, stage)
    print(f"PASS: {len(expected)} drawings, {len(expected) * 16} selected layer records, "
          f"{len(expected) * 32} LINE records, {rejected} corruption/inventory controls rejected; "
          "zero independent audit errors/repairs. No native AutoCAD execution is claimed.")


if __name__ == "__main__":
    require(len(sys.argv) == 2, "Usage: verify_layer_viewport_defaults.py ARTIFACTS")
    main(Path(sys.argv[1]))
