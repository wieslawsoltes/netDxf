#!/usr/bin/env python3
"""Independently check complete native/synthetic TABLESTYLE border-edit packets.

Only declared border triples, and explicitly requested header/row scalars, may
change. TABLE/CELLSTYLEMAP and every other physical record stay exact except the
existing checked empty layer-state dictionary regeneration. No rendering claim.
"""
import argparse
import copy
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata, FILES, DESCRIPTION, ROW, HEADER
from verify_mleader_inputs import check, decode_once

PROFILES = {"AutoCad2004": "AC1018", "AutoCad2007": "AC1021", "AutoCad2010": "AC1024", "AutoCad2013": "AC1027", "AutoCad2018": "AC1032"}


def public_positions(tags):
    positions = []
    depth, active, seen = 0, False, False
    for i, (code, value) in enumerate(tags):
        if code == 102:
            if value.startswith("{"):
                depth += 1
            elif value == "}" and depth:
                depth -= 1
            else:
                raise ValueError("Invalid TABLESTYLE application framing")
            continue
        if depth:
            continue
        if code == 100:
            active = value == "AcDbTableStyle"
            check(not (active and seen), "Repeated public TABLESTYLE subclass")
            seen |= active
        elif active:
            positions.append(i)
    check(depth == 0 and seen, "Missing or unterminated public TABLESTYLE packet")
    return positions


def check_border_edit(before, after, combined=False):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    check(list(before) == list(after), "Physical identities or record order changed")
    styles = [key for key, tags in before.items() if tags[0] == [0, "TABLESTYLE"]]
    check(len(styles) == 1, "Expected exactly one TABLESTYLE")
    handle = styles[0]
    for other in before:
        if other != handle:
            check(before[other] == after[other], "Unedited physical record changed: " + other)
    original, actual = before[handle], after[handle]
    check(len(original) == len(actual), "TABLESTYLE packet size changed")
    expected = copy.deepcopy(original)
    public = public_positions(original)
    starts = [j for j, index in enumerate(public) if original[index][0] == 7]
    check(len(starts) == 3, "Expected three public rows")
    changed = []
    for row in (0, 2):
        positions = public[starts[row]:starts[row + 1] if row < 2 else len(public)]
        values = {}
        for slot in range(6):
            values.update({274 + slot: 25 if slot % 2 == 0 else 50,
                           284 + slot: int((slot + row) % 2 == 0),
                           64 + slot: 10 + row * 6 + slot})
        if combined:
            values.update(ROW)
        for code, value in values.items():
            matches = [index for index in positions if original[index][0] == code]
            check(len(matches) == 1, "Missing or ambiguous public row field " + str(code))
            index = matches[0]
            expected[index] = [code, value]
            changed.append(index)
    if combined:
        positions = public[:starts[0]]
        if positions and original[positions[0]] == [280, 0]:
            positions = positions[1:]
        check([original[i][0] for i in positions] == [3, 70, 71, 40, 41, 280, 281], "Unrecognized edited header")
        for index in positions:
            code = original[index][0]
            if code == 3:
                check(decode_once(actual[index][1]) == DESCRIPTION, "Wrong edited Unicode description")
                expected[index] = actual[index]
            else:
                expected[index] = [code, HEADER[code]]
            changed.append(index)
    check(actual == expected, "Incorrect edit or unrelated TABLESTYLE packet changed")
    protected = [public[starts[0]], public[starts[1]], public[starts[1] + 1], public[0]]
    return handle, sorted(set(changed + protected))


def corruption_controls(before, after, combined=False):
    handle, targets = check_border_edit(before, after, combined)
    count = 0
    # Each actual edited value and protected row/version field is independently altered.
    for index in targets:
        changed = copy.deepcopy(after)
        value = changed[handle][index][1]
        changed[handle][index][1] = value + "_CORRUPT" if isinstance(value, str) else value + 1
        try:
            check_border_edit(before, changed, combined)
        except ValueError:
            count += 1
        else:
            raise AssertionError("Gate accepted corrupt TABLESTYLE field at " + str(index))
    for other in after:
        if other == handle:
            continue
        changed = copy.deepcopy(after)
        changed[other].append([999, "CORRUPT"])
        try:
            check_border_edit(before, changed, combined)
        except ValueError:
            count += 1
        else:
            raise AssertionError("Gate accepted corrupt unedited physical record")
        # One unrelated record per pair checks the non-style comparison path.
        break
    return count


def self_test():
    checked = controls = 0
    for versioned in (False, True):
        for combined in (False, True):
            tags = [[0, "TABLESTYLE"], [5, "A"], [100, "AcDbTableStyle"]]
            if versioned:
                tags.append([280, 0])
            tags += [[3, "before"], [70, 0], [71, 0], [40, .06], [41, .06], [280, 0], [281, 0]]
            for row in range(3):
                tags += [[7, "Standard"], [140, 1.0], [170, 5], [62, 0], [63, 257], [283, 0]]
                for slot in range(6):
                    tags += [[274 + slot, -2], [284 + slot, 1], [64 + slot, 0]]
            before = {"A": tags, "B": [[0, "XRECORD"], [5, "B"], [1, "unchanged"]]}
            after = copy.deepcopy(before)
            row = -1
            for item in after["A"]:
                code = item[0]
                if code == 7:
                    row += 1
                if row in (0, 2):
                    if 274 <= code <= 279:
                        item[1] = 25 if (code - 274) % 2 == 0 else 50
                    elif 284 <= code <= 289:
                        item[1] = int((code - 284 + row) % 2 == 0)
                    elif 64 <= code <= 69:
                        item[1] = 10 + row * 6 + code - 64
                    elif combined and code in ROW:
                        item[1] = ROW[code]
                elif row == -1 and combined:
                    if code == 3:
                        item[1] = DESCRIPTION.replace("\\", "\\U+005C")
                    elif code in HEADER and not (code == 280 and item is after["A"][3] and versioned):
                        item[1] = HEADER[code]
            controls += corruption_controls(before, after, combined)
            checked += 1
    print(f"PASS border oracle self-test: {checked} pairs; {controls} corruption controls")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, nargs="?")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if args.directory is None:
        parser.error("directory is required")
    pairs = []
    for version, profile in PROFILES.items():
        for binary in (False, True):
            for combined in (False, True):
                suffix = f"{version}-{binary}-{combined}.dxf"
                pairs.append(("table-style-borders-before-" + suffix, "table-style-borders-after-" + suffix, binary, combined, profile))
    for kind, files in (("native", FILES), ("full-native", FILES[:2])):
        for file in files:
            for binary in (False, True):
                prefix = "table-style-borders-" + kind
                pairs.append((f"{prefix}-before-{file}-{binary}.dxf", f"{prefix}-after-{file}-{binary}.dxf", binary, False, None))
    controls = 0
    for before_name, after_name, binary, combined, profile in pairs:
        before_path, after_path = args.directory / before_name, args.directory / after_name
        for path in (before_path, after_path):
            check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Transport changed")
        before_doc, after_doc = ezdxf.readfile(before_path), ezdxf.readfile(after_path)
        check(before_doc.dxfversion == after_doc.dxfversion, "Source profile changed")
        if profile is not None:
            check(before_doc.dxfversion == profile, "Wrong synthetic source profile")
        before, after = records(before_path), records(after_path)
        controls += corruption_controls(before, after, combined)
        print("PASS " + after_name)
    check(len(pairs) == 34, "Border output inventory changed")
    print(f"PASS ezdxf {ezdxf.__version__}: {len(pairs)} TABLESTYLE border pairs, {controls} corruption controls; no rendering claim")


if __name__ == "__main__":
    main()
