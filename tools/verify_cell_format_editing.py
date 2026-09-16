#!/usr/bin/env python3
"""Independently verify nested CELLSTYLEMAP edits against complete DXF records.

The oracle locates fields by nested frame scope, not production array offsets.
It computes the requested changes itself, checks all other physical records,
and challenges actual parsed output with individual field/structure corruptions.
No format-expression evaluation or native rendering is asserted.
"""
from __future__ import annotations

import argparse
import copy
from dataclasses import dataclass, field
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata, FILES
from verify_table_row_settings import wire_text
from verify_table_style_borders import PROFILES
from verify_mleader_inputs import check

MODES = ("table", "content", "margins", "grids", "resources", "combined")
NATIVE_PROFILES = dict(zip(FILES, ("AC1027", "AC1032", "AC1018", "AC1021", "AC1024")))


@dataclass
class Frame:
    name: str
    start: int
    end: int = -1
    fields: list[int] = field(default_factory=list)
    children: list[Frame] = field(default_factory=list)


def framed_map(tags):
    starts = [i for i, item in enumerate(tags) if item == [100, "AcDbCellStyleMap"]]
    check(len(starts) == 1, "Expected one CELLSTYLEMAP subclass")
    root = Frame("MAP", starts[0])
    stack = [root]
    for index in range(root.start + 1, len(tags)):
        code, value = tags[index]
        if code >= 1000 and len(stack) == 1:
            break  # Common XData is compared exactly but is not a format frame.
        if code == 1:
            check(isinstance(value, str) and value.endswith("_BEGIN"), "Invalid frame opening")
            frame = Frame(value[:-6], index)
            stack[-1].children.append(frame)
            stack.append(frame)
        elif code == 309:
            check(len(stack) > 1 and value == stack[-1].name + "_END", "Mismatched frame ending")
            stack.pop().end = index
        else:
            stack[-1].fields.append(index)
    check(len(stack) == 1, "Unterminated CELLSTYLEMAP frame")
    check([tags[i] for i in root.fields] == [[90, 3]] + [[300, "CELLSTYLE"]] * 3,
          "CELLSTYLEMAP count or entry marker changed")
    check([node.name for node in root.children] == ["TABLEFORMAT", "CELLSTYLE"] * 3,
          "CELLSTYLEMAP entry framing/order changed")
    return root


def field_index(tags, frame, code):
    found = [i for i in frame.fields if tags[i][0] == code]
    check(len(found) == 1, f"Expected one group {code} within {frame.name}")
    return found[0]


def target_handle(items, kind, name, profile):
    matches = [key for key, tags in items.items()
               if tags[0] == [0, kind] and [2, wire_text(name, profile)] in tags]
    check(len(matches) == 1, "Missing/ambiguous existing " + kind + " target")
    return matches[0]


def expected_edit(before, mode, profile):
    maps = [key for key, tags in before.items() if tags[0] == [0, "CELLSTYLEMAP"]]
    check(len(maps) == 1, "Expected one physical CELLSTYLEMAP")
    handle = maps[0]
    tags = before[handle]
    wanted = copy.deepcopy(tags)
    root = framed_map(tags)
    style = target_handle(before, "STYLE", "EDIT_MAP_STYLE_Ω", profile)
    line = target_handle(before, "LTYPE", "EDIT_MAP_LTYPE_Ω", profile)
    changed, protected = set(), set(root.fields)

    def replace(node, values):
        for code, value in values.items():
            index = field_index(tags, node, code)
            wanted[index] = [code, value]
            changed.add(index)

    for row in range(3):
        table, entry = root.children[2 * row:2 * row + 2]
        check([n.name for n in table.children] == ["CONTENTFORMAT", "CELLMARGIN"] + ["GRIDFORMAT"] * 6,
              "Expected complete scoped content, margins and six grids")
        check([tags[i][0] for i in entry.fields] == [90, 91, 300], "Entry identity/type/name framing changed")
        content, margins, *grids = table.children
        check(tags[field_index(tags, table, 170)][1] != 0 and tags[field_index(tags, table, 171)][1] != 0,
              "Expected present format and margin data")
        check(tags[field_index(tags, table, 94)][1] == 6, "Expected six declared grids")
        mask_indices = [i for i in table.fields if tags[i][0] == 95]
        check(len(mask_indices) == 6 and all(tags[i][1] != 0 for i in mask_indices), "Expected six nonzero stored masks")
        check([tags[i][0] for i in margins.fields] == [40] * 6, "Margin fields changed")
        protected.update(entry.fields)
        protected.update(mask_indices)
        protected.update((table.start, table.end, entry.start, entry.end))
        protected.update(field_index(tags, table, c) for c in (90, 170, 171, 94))
        for child in table.children:
            protected.update((child.start, child.end))
        if row == 1:
            # Mutate every untouched middle-entry field in the corruption controls.
            protected.update(table.fields)
            for child in table.children:
                protected.update(child.fields)
            continue
        if mode in ("table", "combined"):
            replace(table, {91: 17, 92: 42, 62: 10 + row, 93: 3})
        if mode in ("content", "combined"):
            expression = f"%lu2%pr3 Literal \\U+0041 — Ω 😀 {row}"
            replace(content, {90: 19, 91: 2, 92: 4, 93: 2, 300: wire_text(expression, profile),
                              40: .25 + row, 140: 2.25, 94: 6, 62: 11 + row, 144: 3.5 + row})
        if mode in ("margins", "combined"):
            for index, value in zip(margins.fields, (.25, .5, .75, 1, 1.25, 1.5 + row)):
                wanted[index] = [40, value]
                changed.add(index)
        if mode in ("resources", "combined"):
            replace(content, {340: style})
        for slot, grid in enumerate(grids):
            if mode in ("grids", "combined"):
                replace(grid, {90: 7, 91: 1, 62: 20 + slot, 92: 25 + row, 93: slot % 2, 40: .125 * (slot + 1)})
            if mode in ("resources", "combined"):
                replace(grid, {340: line})
        if mode == "combined":
            replace(entry, {300: "Edited map " + str(row)})
    protected.update(i for i, tag in enumerate(tags[:root.start]) if tag[0] in (5, 330, 360))
    return handle, wanted, sorted(changed | protected)


def compare_records(before, after, handle, wanted):
    check(list(before) == list(after), "Physical record identities/order changed")
    for key in before:
        check(after[key] == (wanted if key == handle else before[key]), "Unexpected complete-record change: " + key)


def check_pair(before, after, mode, profile):
    before = normalize_save_metadata(before)
    after = normalize_save_metadata(after)
    handle, wanted, controls = expected_edit(before, mode, profile)
    compare_records(before, after, handle, wanted)
    return handle, controls


def challenge(before, after, mode, profile):
    # Normalize only the separately checked empty generated layer-state dictionary.
    # All controlled fields below are actual parsed payload values. The same exact
    # whole-record comparison validates both positive output and each corruption.
    before = normalize_save_metadata(before)
    after = normalize_save_metadata(after)
    handle, wanted, positions = expected_edit(before, mode, profile)
    compare_records(before, after, handle, wanted)
    count = 0

    def rejected(candidate):
        nonlocal count
        try:
            compare_records(before, candidate, handle, wanted)
        except (ValueError, KeyError):
            count += 1
        else:
            raise AssertionError("Actual-output corruption escaped the cell-format verifier")

    for index in positions:
        candidate = dict(after)
        candidate[handle] = copy.deepcopy(after[handle])
        value = candidate[handle][index][1]
        candidate[handle][index][1] = value + "_CORRUPT" if isinstance(value, str) else value + 1
        rejected(candidate)
    candidate = dict(after)
    candidate[handle] = after[handle] + [[94, 999]]
    rejected(candidate)
    for kind in ("TABLESTYLE", "TABLECONTENT", "TABLE", "ACAD_TABLE", "STYLE", "LTYPE"):
        key = next((key for key, tags in after.items() if tags[0] == [0, kind]), None)
        if key is not None:
            candidate = dict(after)
            candidate[key] = after[key] + [[999, "CORRUPT"]]
            rejected(candidate)
    candidate = dict(after)
    del candidate[handle]
    rejected(candidate)
    return count


def inventory():
    pairs = []
    for version, profile in PROFILES.items():
        for binary in (False, True):
            for mode in MODES:
                pairs.append((f"synthetic-{version}-{binary}-{mode}", profile, binary, mode))
    for file, profile in NATIVE_PROFILES.items():
        for binary in (False, True):
            for mode in MODES:
                pairs.append((f"native-{file}-{binary}-{mode}", profile, binary, mode))
    return pairs


def check_inventory(directory):
    pairs = inventory()
    wanted = {f"cell-format-{phase}-{suffix}.dxf" for suffix, *_ in pairs for phase in ("before", "after")}
    check({p.name for p in directory.glob("cell-format-*.dxf")} == wanted,
          "Exact nested cell-format fixture inventory is required")
    return pairs


def self_test():
    # Use independently parsed, pinned native source packets as model inputs.
    import gzip
    import tempfile
    root = Path(__file__).resolve().parents[1]
    pairs = controls = 0
    with tempfile.TemporaryDirectory() as temporary:
        for file, profile in NATIVE_PROFILES.items():
            path = Path(temporary) / file
            path.write_bytes(gzip.decompress((root / "tests/fixtures/table-oracle" / (file + ".gz")).read_bytes()))
            parsed = records(path)
            key = next(key for key, tags in parsed.items() if tags[0] == [0, "CELLSTYLEMAP"])
            before = {key: parsed[key], "FFFFFFFFFFF1": [[0, "STYLE"], [5, "FFFFFFFFFFF1"], [2, wire_text("EDIT_MAP_STYLE_Ω", profile)]],
                      "FFFFFFFFFFF2": [[0, "LTYPE"], [5, "FFFFFFFFFFF2"], [2, wire_text("EDIT_MAP_LTYPE_Ω", profile)]]}
            for mode in MODES:
                handle, wanted, _ = expected_edit(before, mode, profile)
                after = dict(before)
                after[handle] = wanted
                controls += challenge(before, after, mode, profile)
                pairs += 1
    print(f"PASS cell-format oracle challenge self-test: {pairs} modeled native pairs; {controls} corruptions rejected")


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
    pairs = check_inventory(args.directory)
    controls = 0
    for suffix, profile, binary, mode in pairs:
        paths = [args.directory / f"cell-format-{phase}-{suffix}.dxf" for phase in ("before", "after")]
        for path in paths:
            check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Cell-format transport changed")
            check(ezdxf.readfile(path).dxfversion == profile, "Cell-format source profile changed")
        controls += challenge(records(paths[0]), records(paths[1]), mode, profile)
        print("PASS " + suffix)
    check(len(pairs) == 120, "Expected sixty synthetic and sixty native pairs")
    print(f"PASS ezdxf {ezdxf.__version__}: {len(pairs)} complete cell-format pairs; {controls} actual-output corruptions rejected; no regeneration claim")


if __name__ == "__main__":
    main()
