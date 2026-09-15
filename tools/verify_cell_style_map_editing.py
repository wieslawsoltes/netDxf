#!/usr/bin/env python3
"""Independently qualify CELLSTYLEMAP name edits against pinned producer packets.

Only the three known producer entry-name tags may change in native map payloads.
The schema controls qualify Unicode and a literal DXF escape with ezdxf's decoder.
No netDxf projection or name-index metadata is used by this oracle.
"""
import argparse
import copy
import json
from pathlib import Path

import ezdxf
from verify_fourth_mixed_modules import load, metadata, dictionary_edges, decode_once
from verify_stored_table_content import (
    FILES, source, payload, check, exact, verify_extraction, plain_records,
    verify_carrier_graph, symbol_name,
)

NAMES = (r"Literal \U+0041 — Zażółć 日本語 😀", "CELLSTYLE_END")
NATIVE_NAMES = ("_TITLE", "_HEADER", "_DATA")


def maps(records):
    return {handle: row for handle, row in records.items() if row[0] == (0, "CELLSTYLEMAP")}


def verify_class(classes, count):
    definitions = [row for row in classes if (1, "CELLSTYLEMAP") in row]
    check(len(definitions) == 1, "CELLSTYLEMAP class inventory changed")
    check(all(tag in definitions[0] for tag in [
        (2, "AcDbCellStyleMap"), (3, "ObjectDBX Classes"), (90, 1152),
        (91, count), (280, 0), (281, 0),
    ]), "CELLSTYLEMAP class metadata changed")


def verify_native(records, classes, original):
    expected_maps = maps(original)
    check(set(maps(records)) == set(expected_maps), "Native map identities changed")
    check(len(expected_maps) == 1, "Pinned producer map inventory changed")
    for handle, before in expected_maps.items():
        after = records[handle]
        expected = copy.deepcopy(before)
        # Locate the three literal names in the pinned producer packet. This
        # does not reproduce the reader's nested framing or entry-index parser.
        for index, name in enumerate(NATIVE_NAMES):
            positions = [i for i, tag in enumerate(expected) if tag == (300, name)]
            check(len(positions) == 1, "Pinned producer entry name is ambiguous")
            expected[positions[0]] = (300, "Custom style " + str(index))
        check(payload(after) == payload(expected), "Native map changed outside its three entry-name tags")
        check(metadata(after) == metadata(before), "Native map owner/reactor/extension metadata changed")
        before_xdata = next((i for i, tag in enumerate(before) if tag[0] == 1001), len(before))
        after_xdata = next((i for i, tag in enumerate(after) if tag[0] == 1001), len(after))
        check(exact(after[after_xdata:]) == exact(before[before_xdata:]), "Native map XData changed")
        owner = metadata(before)[0]
        check(owner in records and records[owner][0] == (0, "DICTIONARY"), "Native owner identity changed")
        check(("ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP", 360, handle)
              in dictionary_edges(records[owner]), "Native map dictionary edge changed")
        style = metadata(original[owner])[0]
        check(metadata(records[owner])[0] == style and style in records
              and records[style][0] == (0, "TABLESTYLE"), "Native TABLESTYLE ownership changed")
        check(metadata(records[style])[2] == owner, "Native TABLESTYLE extension identity changed")
        for code, target in before:
            if (330 <= code <= 369 or 390 <= code <= 399 or code in (480, 481, 1005)) and int(target, 16):
                check(target in records and records[target][0] == original[target][0], "Native dependency identity/type changed")
                check(symbol_name(records[target]) == symbol_name(original[target]), "Native dependency resource name changed")
    verify_class(classes, len(expected_maps))


def verify_schema(records, classes):
    actual_maps = maps(records)
    check(len(actual_maps) == 1, "Schema map inventory changed")
    handle, row = next(iter(actual_maps.items()))
    expected = [(100, "AcDbCellStyleMap"), (90, 2)]
    for index, stored_type in enumerate((-2147483648, 2147483647)):
        expected += [(300, "CELLSTYLE"), (1, "TABLEFORMAT_BEGIN"), (90, 5), (170, 0),
                     (309, "TABLEFORMAT_END"), (1, "CELLSTYLE_BEGIN"), (90, -7),
                     (91, stored_type), (300, NAMES[index]), (309, "CELLSTYLE_END")]
    start = row.index((100, "AcDbCellStyleMap"))
    actual = list(row[start:])
    check(len(actual) == len(expected), "Schema payload length changed")
    for index in (10, 20):
        check(actual[index][0] == 300, "Schema entry-name group changed")
        actual[index] = (300, decode_once(actual[index][1]))
    check(exact(actual) == exact(expected), "Schema name Unicode/escaping or untouched fields changed")
    owner = metadata(row)[0]
    check(owner in records and ("MAP", 360, handle) in dictionary_edges(records[owner]), "Schema ownership edge changed")
    verify_class(classes, 1)


def corruption_controls(records, classes, validate):
    """Corrupt independently parsed actual outputs, then call the normal oracle."""
    handle, row = next(iter(maps(records).items()))
    mutations = []
    for code in (90, 91, 300, 1, 309, 340):
        positions = [i for i, tag in enumerate(row) if tag[0] == code]
        if positions:
            mutations.append((handle, positions[0]))
    # Every edited name is independently corrupted, including the schema
    # literal escape whose decoded meaning must not collapse to an ASCII A.
    mutations += [(handle, i) for i, (code, value) in enumerate(row)
                  if code == 300 and (value.startswith("Custom style ")
                                    or value.startswith("Literal ") or value == "CELLSTYLE_END")]
    owner = metadata(row)[0]
    mutations.append((handle, row.index((330, owner))))
    mutations += [(owner, i) for i, tag in enumerate(records[owner]) if tag == (360, handle)]
    count = 0

    def rejects(changed, definitions):
        nonlocal count
        try:
            validate(changed, definitions)
        except (ValueError, KeyError, StopIteration):
            count += 1
        else:
            raise AssertionError("Oracle accepted an actual-output corruption")

    for key, index in mutations:
        changed = copy.deepcopy(records)
        code, value = changed[key][index]
        changed[key][index] = (code, value + "_CORRUPT" if isinstance(value, str) else value + 1)
        rejects(changed, classes)
    missing_owner = copy.deepcopy(records)
    del missing_owner[owner]
    rejects(missing_owner, classes)
    rejects(records, [])
    dependencies = {value for code, value in row if code == 340 and int(value, 16)}
    for dependency in dependencies:
        missing = copy.deepcopy(records)
        del missing[dependency]
        rejects(missing, classes)
    return count


def audit_clean(document):
    before = set(document.entitydb)
    audit = document.audit()
    check(not audit.errors and not audit.fixes and set(document.entitydb) == before,
          "Edited output required an audit error, repair or entity removal")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    expected = {f"cell-map-edit-AutoCad{year}-{binary}.dxf"
                for year in (2004, 2007, 2010, 2013, 2018) for binary in (False, True)}
    expected |= {f"cell-map-edit-native-{file}-{binary}.dxf" for file in FILES for binary in (False, True)}
    check({path.name for path in args.directory.glob("cell-map-edit-*.dxf")} == expected,
          "Exactly 10 schema and 10 native edited outputs are required")
    manifest = json.loads((args.repository / "tests/fixtures/table-content/manifest.json").read_text())
    selected = sum(verify_extraction(args.repository, entry) for entry in manifest["files"])
    check(selected == 316, "Pinned source carrier inventory changed")
    controls = native = schema = appearances = 0
    for file in FILES:
        original, _, year = source(args.repository, file)
        entry = next(item for item in manifest["files"] if item["file"] == file)
        retained = entry.get("records", [])
        carrier = plain_records((args.repository / "tests/fixtures/table-content" / entry["fixture"]).read_bytes(), entry["profile"]) if retained else {}
        for binary in (False, True):
            document, records, classes = load(args.directory / f"cell-map-edit-native-{file}-{binary}.dxf", year, binary)
            verify_native(records, classes, original)
            verify_carrier_graph(records, carrier, retained)
            controls += corruption_controls(records, classes, lambda rows, definitions: verify_native(rows, definitions, original))
            audit_clean(document)
            appearances += len(retained)
            native += 1
    for year in (2004, 2007, 2010, 2013, 2018):
        for binary in (False, True):
            document, records, classes = load(args.directory / f"cell-map-edit-AutoCad{year}-{binary}.dxf", year, binary)
            verify_schema(records, classes)
            controls += corruption_controls(records, classes, verify_schema)
            audit_clean(document)
            schema += 1
    check(native == 10 and schema == 10 and appearances == 632 and controls == 254,
          "Edited output or corruption-control inventory changed")
    print(json.dumps({"passed": True, "schema_outputs": schema, "native_outputs": native,
                      "native_changed_names": 30, "source_carrier_records": selected,
                      "retained_carrier_appearances": appearances, "actual_output_corruption_controls": controls,
                      "audit_errors": 0, "audit_repairs": 0, "audit_removed_entities": 0,
                      "ezdxf": ezdxf.__version__}))


if __name__ == "__main__":
    main()
