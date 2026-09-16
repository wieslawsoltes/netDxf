#!/usr/bin/env python3
"""Verify scoped TABLESTYLE stored scalar edits independently with ezdxf tag readers.

Compares complete before/after object/entity/table records from fourteen native
outputs. Only the declared header and first/third row scalar fields may differ.
CELLSTYLEMAP and TABLE data must remain byte-value-equivalent stored packets;
this gate makes no rendering or synchronization claim.
"""
import argparse
import copy
import io
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler
from verify_mleader_inputs import check, decode_once, json_value

DESCRIPTION = "Edited Żółć \\U+0041 😀"
ROW = {140: 4.125, 170: 6, 62: 3, 63: 257, 283: 1}
HEADER = {70: 1, 71: -7, 40: 2.125, 41: 3.25, 280: 1, 281: 0}
FILES = ("acad_table_simple.dxf", "acad_table_with_blk_ref.dxf", "sample_AC1018_ascii.dxf", "sample_AC1021_ascii.dxf", "sample_AC1024_ascii.dxf")


def records(path):
    data = path.read_bytes()
    loader = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None))
    result, record = {}, []

    def finish(tags):
        if not tags or tags[0][1] in ("SECTION", "ENDSEC", "ENDTAB", "EOF", "CLASS"):
            return
        identity_code = 105 if tags[0] == [0, "DIMSTYLE"] else 5
        handles, depth = [], 0
        for code, value in tags:
            if code == 100 and depth == 0:
                break
            if code == 102:
                if value.startswith("{"): depth += 1
                elif value == "}": depth -= 1
            elif code == identity_code and depth == 0:
                handles.append(value)
        check(len(handles) == 1, "Indexed native record requires exactly one common identity")
        check(handles[0] not in result, "Duplicate native physical identity")
        result[handles[0]] = tags

    for tag in tag_compiler(loader):
        if tag.code == 0:
            finish(record)
            record = []
        record.append([tag.code, json_value(tag.value)])
    finish(record)
    return result


def normalize_save_metadata(items):
    # The common writer assigns a new identity to the empty generated layer-state
    # dictionary on every save. Validate that exact known shape before normalizing
    # its identity. HEADER contains HANDSEED/time values, not physical objects.
    result = copy.deepcopy(items)
    for owner, tags in list(result.items()):
        positions = [i for i, tag in enumerate(tags) if tag == [3, "ACAD_LAYERSTATES"]]
        if not positions:
            continue
        check(len(positions) == 1, "Repeated layer-state dictionary slot")
        index = positions[0] + 1
        check(tags[index][0] == 360, "Generated layer states require hard ownership")
        child = tags[index][1]
        expected = [[0, "DICTIONARY"], [5, child], [330, owner], [100, "AcDbDictionary"], [280, 1], [281, 1]]
        check(result.get(child) == expected, "Generated layer-state dictionary is no longer the qualified empty shape")
        tags[index] = [360, "<generated-empty-layer-states>"]
        del result[child]
    return result


def check_edit(before, after):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    check(list(before) == list(after), "Physical record identities changed")
    styles = [h for h, tags in before.items() if tags[0] == [0, "TABLESTYLE"]]
    check(len(styles) == 1, "Expected one native TABLESTYLE")
    handle = styles[0]
    for other in before:
        if other != handle:
            check(before[other] == after[other], "Unedited record changed: " + other)
    original, actual = before[handle], after[handle]
    expected = copy.deepcopy(original)
    start = expected.index([100, "AcDbTableStyle"]) + 1
    row_starts = [i for i in range(start, len(expected)) if expected[i][0] == 7]
    check(len(row_starts) == 3, "Expected three classic native row packets")
    # The leading group 280 is a fixed format version, not title suppression.
    if expected[start] == [280, 0] and [t[0] for t in expected[start + 1:row_starts[0]]] == [3, 70, 71, 40, 41, 280, 281]:
        start += 1
    header_indices = range(start, row_starts[0])
    classic_header = [expected[i][0] for i in header_indices] == [3, 70, 71, 40, 41, 280, 281]
    check(len(actual) == len(expected), "TABLESTYLE packet length changed")
    changed_indices = []
    if classic_header:
        check(decode_once(actual[start][1]) == DESCRIPTION, "Edited description has incorrect Unicode or literal escape semantics")
        expected[start] = actual[start]
        changed_indices.append(start)
        for index in header_indices:
            code = expected[index][0]
            if code in HEADER:
                expected[index] = [code, HEADER[code]]
                changed_indices.append(index)
    for row in (0, 2):
        first, last = row_starts[row], row_starts[row + 1] if row < 2 else len(expected)
        for code, value in ROW.items():
            indices = [i for i in range(first, last) if expected[i][0] == code]
            check(len(indices) == 1, "Native row scalar ambiguity")
            expected[indices[0]] = [code, value]
            changed_indices.append(indices[0])
    check(expected == actual, "TABLESTYLE changed outside the explicit scalar edits or has incorrect values")
    return handle, changed_indices, row_starts


def controls(before, after):
    handle, indices, rows = check_edit(before, after)
    count = 0
    # Alter each declared edit independently and several untouched resource/format
    # fields in the real parsed output. The normal gate must reject every change.
    targets = [(handle, index) for index in indices]
    targets += [(handle, rows[1]), (handle, rows[1] + 1)]
    for key, tags in after.items():
        if tags[0] == [0, "CELLSTYLEMAP"]:
            targets += [(key, next(i for i, t in enumerate(tags) if t[0] == code)) for code in (90, 300, 340)]
    for kind, code in (("DIMSTYLE", 105), ("TABLE", 70)):
        item = next(((key, tags) for key, tags in after.items() if tags[0] == [0, kind]), None)
        check(item is not None, "Native control lacks " + kind)
        key, tags = item
        targets.append((key, next(i for i, tag in enumerate(tags) if tag[0] == code)))
    for key, index in targets:
        changed = copy.deepcopy(after)
        value = changed[key][index][1]
        changed[key][index][1] = value + "_CORRUPT" if isinstance(value, str) else value + 1
        try:
            check_edit(before, changed)
        except ValueError:
            count += 1
        else:
            raise AssertionError("Gate accepted altered native packet field")
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    checked = negative = 0
    for kind, files in (("native", FILES), ("full-native", FILES[:2])):
        for file in files:
            for binary in (False, True):
                prefix = "edited-table-style-" + kind
                before_path = args.directory / f"{prefix}-before-{file}-{binary}.dxf"
                after_path = args.directory / f"{prefix}-after-{file}-{binary}.dxf"
                check(after_path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Transport changed")
                before_doc, after_doc = ezdxf.readfile(before_path), ezdxf.readfile(after_path)
                check(before_doc.dxfversion == after_doc.dxfversion, "Native source profile changed")
                before, after = records(before_path), records(after_path)
                check_edit(before, after)
                negative += controls(before, after)
                checked += 1
                print("PASS " + after_path.name)
    check(checked == 14, "Native output inventory incomplete")
    print(f"PASS ezdxf {ezdxf.__version__}: {checked} native TABLESTYLE edits, {negative} actual-packet corruption controls; no rendering claim")


if __name__ == "__main__":
    main()
