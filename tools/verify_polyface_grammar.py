#!/usr/bin/env python3
"""Independent signed POLYFACE grammar and physical output-sequence gate.

All inputs are explicit schema cases, not native producer evidence. Header
71/72 counts are advisory. This gate checks output geometry, signs, fixed slot
codes, child ordering and actual parent identities without netDxf.
"""
from __future__ import annotations

import argparse
import copy
from pathlib import Path

from verify_fourth_mixed_modules import PROFILES, check, load

POINTS = [(11.0, 12.0, 13.0), (21.0, 22.0, 23.0),
          (31.0, 32.0, 33.0), (41.0, 42.0, 43.0)]
EXPECTED = [(-1,), (1, -2), (1, -2, 3), (-1, 2, -3, 4),
            (1,), (-1, 2, -3, 4), (1, 2, 1), (1, 2, 3)]


def values(record, code):
    return [value for group, value in record if group == code]


def only(record, code):
    result = values(record, code)
    check(len(result) == 1, f"Expected exactly one group {code}")
    return result[0]


def validate(records, variant):
    packets = list(records.values())
    headers = [record for record in packets if record[0] == (0, "POLYLINE")]
    check(len(headers) == 1, "POLYFACE header inventory")
    header = headers[0]
    check(only(header, 70) == 64, "POLYFACE header flag")
    check(only(header, 71) == -17 and only(header, 72) == 123, "Stored advisory header counts changed")
    parent = only(header, 5)
    start = packets.index(header)
    sequence = packets[start + 1:start + 7]
    check([record[0][1] for record in sequence] == ["VERTEX"] * 5 + ["SEQEND"],
          "Physical coordinate/face/SEQEND output sequence")
    coordinates = sequence[1:5] if variant & 1 else sequence[:4]
    face, end = sequence[0] if variant & 1 else sequence[4], sequence[5]
    for record, point in zip(coordinates, POINTS):
        check(only(record, 70) == 192, "Coordinate vertex role")
        check(tuple(only(record, 10)) == point, "Coordinate order or position changed")
        check(not any(71 <= code <= 74 for code, _ in record), "Coordinate record has face slots")
    check(only(face, 70) == 128, "Face vertex role")
    check(tuple(only(face, 10)) == (0.0, 0.0, 0.0), "Face dummy coordinate changed")
    indices = [(code, value) for code, value in face if 71 <= code <= 74]
    raw_slots = [(-1, 0, 32767, -32768), (1, -2, 0, 4), (1, -2, 3, 0),
                 (-1, 2, -3, 4), (1, 0, -4, 0), (-1, 2, -3, 4),
                 (1, 2, 1, 0), (1, 2, 3, 0)][variant]
    order = (3, 1, 0, 2) if variant == 5 else (0, 1, 2, 3)
    expected_packet = [(71+slot, raw_slots[slot]) for slot in order
                       if not (variant == 4 and slot in (1, 3)) and not (variant == 7 and slot == 3)]
    check(indices == expected_packet, "Stored fixed slots, omissions, inactive values or physical slot order changed")
    by_slot = dict(indices); active = []
    for code in range(71, 75):
        value = by_slot.get(code, 0)
        if value == 0: break
        active.append(value)
    check(tuple(active) == EXPECTED[variant], "Active semantic face indices or invisible-edge signs changed")
    check(all(1 <= abs(value) <= len(POINTS) for value in active), "Dangling active face index")
    for record in sequence:
        check(only(record, 330) == parent, "Child owner does not identify actual POLYFACE parent")
    following = packets[start + 7]
    check(following[0] == (0, "LINE"), "Following LINE lost or swallowed")
    check(tuple(only(following, 10)) == (101.0, 102.0, 103.0), "Following LINE start changed")
    check(tuple(only(following, 11)) == (104.0, 105.0, 106.0), "Following LINE end changed")
    return parent, only(face, 5), only(coordinates[0], 5), only(end, 5)


def change(record, code, value):
    index = next(index for index, tag in enumerate(record) if tag[0] == code)
    record[index] = (code, value)


def controls(records, variant):
    parent, face, coordinate, end = validate(records, variant)
    count = 0
    for fault in range(6):
        damaged = copy.deepcopy(records)
        if fault == 0:
            change(damaged[face], 71, -EXPECTED[variant][0])
        elif fault == 1:
            change(damaged[face], 71, 0)
        elif fault == 2:
            change(damaged[face], 71, 5)
        elif fault == 3:
            change(damaged[coordinate], 10, (99.0, 12.0, 13.0))
        elif fault == 4:
            change(damaged[face], 330, only(damaged[coordinate], 5))
        else:
            del damaged[end]
        try:
            validate(damaged, variant)
        except (ValueError, IndexError, StopIteration):
            count += 1
        else:
            raise ValueError(f"Corruption control {fault} was not detected")
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    expected_names = {f"polyface-grammar-AutoCad{year}-{binary}-{variant}.dxf"
                      for year in PROFILES for binary in (False, True) for variant in range(8)}
    check({path.name for path in args.artifacts.glob("polyface-grammar-*.dxf")} == expected_names,
          "Mandatory 96-drawing POLYFACE inventory differs")
    total = 0
    for year in PROFILES:
        for binary in (False, True):
            for variant in range(8):
                name = f"polyface-grammar-AutoCad{year}-{binary}-{variant}.dxf"
                doc, records, _ = load(args.artifacts / name, year, binary)
                validate(records, variant)
                total += controls(records, variant)
                audit = doc.audit()
                check(not audit.errors and not audit.fixes, name + ": external audit errors or repairs")
                mesh = list(doc.modelspace().query("POLYLINE"))
                check(len(mesh) == 1 and mesh[0].is_poly_face_mesh, name + ": independent POLYFACE parse")
                print("PASS", name)
    print(f"PASS 96 explicit schema drawings, {total} corruption controls, zero audit errors or repairs")


if __name__ == "__main__":
    main()
