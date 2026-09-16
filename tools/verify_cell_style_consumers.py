#!/usr/bin/env python3
"""Check coordinated map-ID/consumer edits against complete physical DXF records.

This independent oracle builds a frame tree and associates IDs by scope. It does
not use production payload indices or an emitted expected-value manifest.
"""
from __future__ import annotations

import argparse
import copy
from dataclasses import dataclass, field
import gzip
import hashlib
import json
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata, FILES
from verify_cell_map_structure import split_entries, payload as map_payload
from verify_mleader_inputs import check

MODES = ("rename", "cycle", "remove", "clear", "identity", "append")
KINDS = {"TABLECOLUMN": 1, "TABLEROW": 2, "TABLECELL": 3}
SUBCLASSES = ("AcDbLinkedData", "AcDbLinkedTableData", "AcDbFormattedTableData", "AcDbTableContent")


@dataclass
class Frame:
    name: str
    start: int
    end: int = -1
    fields: list[int] = field(default_factory=list)
    children: list[Frame] = field(default_factory=list)


def value_end(tags, start, limit):
    """Skip only a declared AcValue, including marker-like scalar string data."""
    if tags[start + 1][0] == 93 and tags[start + 2][0] == 90:
        for end in range(start + 3, limit):
            if tags[end] == [304, "ACVALUE_END"]:
                return end
        raise ValueError("Unterminated native AcValue")
    check(tags[start + 1][0] == 90, "Unknown compact value envelope")
    kind = tags[start + 1][1]
    codes = {0: (91,), 1: (91,), 2: (140,), 4: (1,), 32: (11,)}.get(kind)
    if codes is not None:
        check(tuple(t[0] for t in tags[start + 2:start + 2 + len(codes)]) == codes,
              "Unknown compact scalar payload")
        return start + 1 + len(codes)
    if kind == 8:
        check(tags[start + 2][0] == 92, "Binary value length absent")
        end = start + 2
        while end + 1 < limit and tags[end + 1][0] == 310:
            end += 1
        return end
    raise ValueError("Unqualified native value kind")


def content_slots(tags):
    starts = [i for i, item in enumerate(tags) if item[0] == 100]
    check([tags[i][1] for i in starts] == list(SUBCLASSES), "Consumer subclasses changed")
    start, limit = starts[1], starts[2]
    root = Frame("LINKED", start, limit)
    stack = [root]
    index = start + 1
    while index < limit:
        code, value = tags[index]
        value_marker = code == 300 and value == "VALUE" and stack[-1].name == "CELLCONTENT"
        data_marker = code == 301 and value == "DATAMAP_VALUE" and stack[-1].name == "DATAMAP"
        if value_marker or data_marker:
            end = value_end(tags, index, limit)
            stack[-1].fields.extend(range(index, end + 1))
            index = end + 1
            continue
        if code == 1:
            check(isinstance(value, str) and value.endswith("_BEGIN"), "Unknown consumer frame")
            node = Frame(value[:-6], index)
            stack[-1].children.append(node)
            stack.append(node)
        elif code == 309:
            check(len(stack) > 1 and value == stack[-1].name + "_END", "Unbalanced consumer frame")
            stack.pop().end = index
        else:
            stack[-1].fields.append(index)
        index += 1
    check(len(stack) == 1, "Unterminated consumer tree")
    columns = [tags[i][1] for i in root.fields if tags[i][0] == 90]
    rows = [tags[i][1] for i in root.fields if tags[i][0] == 91]
    check(len(columns) == len(rows) == 1, "Ambiguous outer dimensions")
    columns, rows = columns[0], rows[0]
    expected = ["LINKEDTABLEDATACOLUMN", "FORMATTEDTABLEDATACOLUMN", "TABLECOLUMN"] * columns
    expected += ["LINKEDTABLEDATAROW", "FORMATTEDTABLEDATAROW", "TABLEROW"] * rows
    check([n.name for n in root.children] == expected, "Column/row frame order or count changed")
    result = []

    def slot(frame, kind):
        ids = [i for i in frame.fields if tags[i][0] == 90]
        check(len(ids) == 1 and not frame.children, "Repeated or nested consumer ID")
        check(tags[ids[0]][1] >= 0, "Negative consumer ID")
        result.append((ids[0], kind))

    for column in range(columns):
        slot(root.children[3 * column + 2], "TABLECOLUMN")
    for row in range(rows):
        linked, _, reference = root.children[columns * 3 + row * 3:columns * 3 + row * 3 + 3]
        children = [n for n in linked.children if n.name != "DATAMAP"]
        check([n.name for n in children] == ["LINKEDTABLEDATACELL", "FORMATTEDTABLEDATACELL", "TABLECELL"] * columns,
              "Cell count/order changed")
        for column in range(columns):
            slot(children[column * 3 + 2], "TABLECELL")
        slot(reference, "TABLEROW")
    check(len(result) == columns + rows + columns * rows, "Consumer occurrence count changed")
    return sorted(result)


def expected_records(before, mode, all_slots=False):
    wanted = copy.deepcopy(before)
    maps = [(h, tags) for h, tags in before.items() if tags[0] == [0, "CELLSTYLEMAP"]]
    styles = [h for h, tags in before.items() if tags[0] == [0, "TABLESTYLE"]]
    check(len(maps) == len(styles) == 1, "Fixture style/map inventory changed")
    map_handle, tags = maps[0]
    prefix = len(tags) - len(map_payload(tags))
    blocks = split_entries(map_payload(tags))
    check([b[-4][1] for b in blocks] == [1, 2, 3], "Native map ID inventory changed")
    mapping = {1: 11, 2: 12, 3: 13} if mode == "rename" else {1: 2, 2: 3, 3: 1} if mode == "cycle" else {3: 2} if mode == "remove" else {1: 0, 2: 0, 3: 0} if mode == "clear" else {}
    if mode == "clear":
        blocks = []
    elif mode == "remove":
        blocks = [b for b in blocks if b[-4][1] != 3]
    for block in blocks:
        block[-4][1] = mapping.get(block[-4][1], block[-4][1])
    if mode == "rename":
        blocks.reverse()
    if mode == "append":
        extra = copy.deepcopy(blocks[0])
        extra[-4][1], extra[-2][1] = 10, "Unused"
        blocks.append(extra)
    wanted[map_handle] = tags[:prefix] + [[100, "AcDbCellStyleMap"], [90, len(blocks)]] + sum(blocks, [])
    controls = [(map_handle, i) for i in range(len(wanted[map_handle]))]
    nonzero_changed = consumers = 0
    for handle, record in before.items():
        if record[0] != [0, "TABLECONTENT"]:
            continue
        check(record[-1] == [340, styles[0]], "Native consumer targets a different style")
        consumers += 1
        slots = content_slots(record)
        for index, kind in slots:
            old = record[index][1]
            if all_slots:
                check(old == KINDS[kind], "Synthetic scoped ID construction changed")
            new = mapping.get(old, old)
            nonzero_changed += old != new
            wanted[handle][index] = [90, new]
            controls.append((handle, index))
        # Values/formatting, names, all common metadata and terminal resource links
        # are compared as well as the selected style-ID fields.
        controls += [(handle, i) for i, tag in enumerate(record) if tag[0] in (5, 330, 340, 140, 144, 300, 304)]
        check(content_slots(wanted[handle]) == slots, "Remapping changed occurrence positions")
    check(consumers in (1, 2), "Native consumer inventory changed")
    return wanted, sorted(set(controls)), nonzero_changed


def compare(wanted, actual):
    check(list(wanted) == list(actual), "Ordered physical record identity inventory changed")
    check(wanted == actual, "Unexpected coordinated map/consumer change")


def check_pair(before, after, mode, all_slots=False):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    wanted, positions, changed = expected_records(before, mode, all_slots)
    compare(wanted, after)
    destination = {b[-4][1] for h, t in after.items() if t[0] == [0, "CELLSTYLEMAP"] for b in split_entries(map_payload(t))}
    for record in after.values():
        if record[0] == [0, "TABLECONTENT"]:
            check(all(record[i][1] == 0 or record[i][1] in destination for i, _ in content_slots(record)), "Dangling remapped identifier")
    negatives = 0
    for handle, index in positions:
        candidate = dict(after)
        candidate[handle] = list(after[handle])
        code, value = after[handle][index]
        candidate[handle][index] = [code, value + "_CORRUPT" if isinstance(value, str) else value + 1]
        try:
            compare(wanted, candidate)
        except ValueError:
            negatives += 1
        else:
            raise AssertionError("A corrupted field escaped the same complete-record comparator")
    # Corrupt each unselected physical record and remove each participating object.
    changed_objects = {h for h, _ in positions}
    for handle in after:
        candidate = dict(after)
        if handle in changed_objects:
            del candidate[handle]
        else:
            candidate[handle] = after[handle] + [[999, "CORRUPT"]]
        try:
            compare(wanted, candidate)
        except ValueError:
            negatives += 1
        else:
            raise AssertionError("A corrupted or missing physical record escaped the comparator")
    return changed, negatives


def inventory():
    result = []
    for file in FILES:
        for binary in (False, True):
            for mode in MODES:
                result.append((f"cell-style-consumers-before-{file}-{binary}-{mode}.dxf", f"cell-style-consumers-after-{file}-{binary}-{mode}.dxf", file, binary, mode, False))
            result.append((f"cell-style-consumers-all-before-{file}-{binary}.dxf", f"cell-style-consumers-all-after-{file}-{binary}.dxf", file, binary, "cycle", True))
    return result


def check_inventory(directory):
    pairs = inventory()
    expected = {n for first, last, *_ in pairs for n in (first, last)}
    check({p.name for p in directory.glob("cell-style-consumers-*.dxf")} == expected, "Exact consumer fixture inventory required")
    return pairs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    pairs = check_inventory(args.directory)
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / "tools/table_oracle/fixtures.json").read_text(encoding="utf-8"))["files"]
    pins = {item["file"]: item for item in manifest}
    check(set(pins) == set(FILES), "Native source inventory changed")
    for file in FILES:
        source = gzip.decompress((root / "tests/fixtures/table-oracle" / (file + ".gz")).read_bytes())
        check(hashlib.sha256(source).hexdigest() == pins[file]["sha256"], "Pinned producer source hash changed")
    changed = controls = 0
    for first, last, file, binary, mode, all_slots in pairs:
        paths = [args.directory / first, args.directory / last]
        for path in paths:
            check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Consumer transport changed")
            doc = ezdxf.readfile(path)
            check(doc.dxfversion == pins[file]["profile"], "Source profile changed")
            audit = doc.audit()
            check(not audit.errors and not audit.fixes, "Consumer graph required independent repairs")
        edits, negatives = check_pair(records(paths[0]), records(paths[1]), mode, all_slots)
        changed += edits
        controls += negatives
        print("PASS " + last)
    check(len(pairs) == 70, "Expected 60 native and 10 explicitly constructed all-scope pairs")
    print(f"PASS ezdxf {ezdxf.__version__}: {len(pairs)} coordinated pairs; {changed} ID changes; {controls} corruption controls; no layout/regeneration claim")


if __name__ == "__main__":
    main()
