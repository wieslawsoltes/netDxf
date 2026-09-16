#!/usr/bin/env python3
"""Verify complete TABLESTYLE data/unit and explicit named STYLE edits with ezdxf.

Only the requested public fields may change. Unrelated physical records,
private packets, map references and raw format strings remain exact. This gate
checks stored semantics, not formatting evaluation or native CAD rendering.
"""
import argparse
import copy
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata, FILES, DESCRIPTION, ROW, HEADER
from verify_table_style_borders import public_positions, PROFILES
from verify_mleader_inputs import check

TARGET = "ROW_REPLACEMENT_Ω"
MODES = ("types", "style", "combined")


def wire_text(text, profile):
    if profile >= "AC1021":
        return text.replace("\\", "\\U+005C")
    data = text.encode("utf-16-le")
    units = [int.from_bytes(data[i:i + 2], "little") for i in range(0, len(data), 2)]
    return "".join(f"\\U+{unit:04X}" if unit > 127 or unit == 92 else chr(unit) for unit in units)


def check_pair(before, after, mode, profile, has_types):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    check(list(before) == list(after), "Physical identity/order inventory changed")
    styles = [key for key, tags in before.items() if tags[0] == [0, "TABLESTYLE"]]
    check(len(styles) == 1, "Expected one stored TABLESTYLE")
    handle = styles[0]
    for other in before:
        if other != handle:
            check(before[other] == after[other], "Unedited physical record changed: " + other)
    targets = [key for key, tags in before.items() if tags[0] == [0, "STYLE"] and [2, wire_text(TARGET, profile)] in tags]
    check(len(targets) == 1, "Explicit target STYLE must have one pre-existing physical identity")
    original, actual = before[handle], after[handle]
    check(len(original) == len(actual), "TABLESTYLE packet length changed")
    expected = copy.deepcopy(original)
    public = public_positions(original)
    starts = [j for j, index in enumerate(public) if original[index][0] == 7]
    check(len(starts) == 3, "Expected three ordered row packets")
    changed, protected = [], []
    for row in range(3):
        positions = public[starts[row]:starts[row + 1] if row < 2 else len(public)]
        counts = [sum(original[index][0] == code for index in positions) for code in (90, 91)]
        check(counts == ([1, 1] if has_types else [0, 0]), "Unexpected source data/unit presence")
        protected += [index for index in positions if original[index][0] in (7, 1)]
        if row == 1:
            continue
        values = {}
        if mode in ("types", "combined") and has_types:
            values.update({90: 4 + row, 91: 2 + row})
        if mode in ("style", "combined"):
            values[7] = wire_text(TARGET, profile)
        if mode == "combined":
            values.update(ROW)
            for slot in range(6):
                values.update({274 + slot: 25 if slot % 2 == 0 else 50,
                               284 + slot: int((slot + row) % 2 == 0),
                               64 + slot: 10 + row * 6 + slot})
        for code, value in values.items():
            indices = [i for i in positions if original[i][0] == code]
            check(len(indices) == 1, "Missing or repeated requested field")
            index = indices[0]
            expected[index] = [code, value]
            changed.append(index)
    header = public[:starts[0]]
    if header and original[header[0]] == [280, 0]:
        check(profile >= "AC1024", "Unqualified version prefix")
        protected.append(header.pop(0))
    check([original[i][0] for i in header] == [3, 70, 71, 40, 41, 280, 281], "Header framing changed")
    if mode == "combined":
        for index in header:
            code = original[index][0]
            expected[index] = [code, wire_text(DESCRIPTION, profile) if code == 3 else HEADER[code]]
            changed.append(index)
    check(actual == expected, "Incorrect row edit or unrequested packet change")
    protected += [i for i, tag in enumerate(original) if tag[0] in (5, 330, 360)]
    return handle, sorted(set(changed + protected))


def challenge(before, after, mode, profile, has_types):
    handle, indices = check_pair(before, after, mode, profile, has_types)
    count = 0

    def rejects(candidate):
        nonlocal count
        try:
            check_pair(before, candidate, mode, profile, has_types)
        except (ValueError, KeyError):
            count += 1
        else:
            raise AssertionError("Actual-output corruption escaped the row-settings gate")

    for index in indices:
        candidate = copy.deepcopy(after)
        value = candidate[handle][index][1]
        candidate[handle][index][1] = value + "_CORRUPT" if isinstance(value, str) else value + 1
        rejects(candidate)
    candidate = copy.deepcopy(after)
    candidate[handle].append([91, 123])
    rejects(candidate)
    for kind in ("STYLE", "CELLSTYLEMAP", "ACAD_TABLE"):
        key = next((key for key, tags in after.items() if tags[0] == [0, kind]), None)
        if key is not None:
            candidate = copy.deepcopy(after)
            candidate[key].append([999, "CORRUPT"])
            rejects(candidate)
    return count


def self_test():
    pairs = controls = 0
    for profile in ("AC1018", "AC1021", "AC1024"):
        for has_types in (False, True):
            for mode in MODES:
                original = [[0, "TABLESTYLE"], [5, "A"], [330, "C"], [100, "AcDbTableStyle"]]
                if profile == "AC1024":
                    original.append([280, 0])
                original += [[3, "before"], [70, 0], [71, 0], [40, .06], [41, .06], [280, 0], [281, 0]]
                for row in range(3):
                    original += [[7, "Standard"], [140, 1.0], [170, 5], [62, 0], [63, 257], [283, 0], [1, "do not interpret"]]
                    if has_types:
                        original += [[90, 512], [91, 0]]
                    for slot in range(6):
                        original += [[274 + slot, -2], [284 + slot, 1], [64 + slot, 0]]
                before = {"A": original, "B": [[0, "STYLE"], [5, "B"], [2, wire_text(TARGET, profile)]]}
                after = copy.deepcopy(before)
                row = -1
                version_index = 4 if profile == "AC1024" else -1
                for index, item in enumerate(after["A"]):
                    code = item[0]
                    if code == 7:
                        row += 1
                    if row in (0, 2):
                        if mode != "style" and code in (90, 91):
                            item[1] = (4 if code == 90 else 2) + row
                        elif mode != "types" and code == 7:
                            item[1] = wire_text(TARGET, profile)
                        elif mode == "combined":
                            if code in ROW: item[1] = ROW[code]
                            elif 274 <= code <= 279: item[1] = 25 if (code - 274) % 2 == 0 else 50
                            elif 284 <= code <= 289: item[1] = int((code - 284 + row) % 2 == 0)
                            elif 64 <= code <= 69: item[1] = 10 + row * 6 + code - 64
                    elif row == -1 and mode == "combined" and index != version_index:
                        if code == 3: item[1] = wire_text(DESCRIPTION, profile)
                        elif code in HEADER: item[1] = HEADER[code]
                controls += challenge(before, after, mode, profile, has_types)
                pairs += 1
    print(f"PASS row-settings oracle self-test: {pairs} pairs; {controls} corruption controls")


def inventory():
    pairs = []
    for version, profile in PROFILES.items():
        for binary in (False, True):
            for mode in MODES:
                pairs.append((f"synthetic-{version}-{binary}-{mode}", binary, mode, profile, True))
    native_profiles = dict(zip(FILES, ("AC1027", "AC1032", "AC1018", "AC1021", "AC1024")))
    for kind, files in (("native", FILES), ("full-native", FILES[:2])):
        for file in files:
            for binary in (False, True):
                for mode in MODES:
                    pairs.append((f"{kind}-{file}-{binary}-{mode}", binary, mode, native_profiles[file], "AC1018" not in file))
    return pairs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if args.directory is None:
        parser.error("directory is required")
    pairs = inventory()
    expected = {f"table-row-settings-{phase}-{suffix}.dxf" for suffix, *_ in pairs for phase in ("before", "after")}
    check({p.name for p in args.directory.glob("table-row-settings-*.dxf")} == expected, "Row-settings fixture inventory must match exactly")
    controls = 0
    for suffix, binary, mode, profile, has_types in pairs:
        paths = [args.directory / f"table-row-settings-{phase}-{suffix}.dxf" for phase in ("before", "after")]
        for path in paths:
            check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Transport changed")
            check(ezdxf.readfile(path).dxfversion == profile, "Source profile changed")
        controls += challenge(records(paths[0]), records(paths[1]), mode, profile, has_types)
        print("PASS " + suffix)
    check(len(pairs) == 72, "Row-settings pair count changed")
    print(f"PASS ezdxf {ezdxf.__version__}: {len(pairs)} row-settings pairs; {controls} corruption controls; no regeneration claim")


if __name__ == "__main__":
    main()
