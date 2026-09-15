#!/usr/bin/env python3
"""Verify exact native ACAD_TABLE subclass preservation, independently of netDxf's parser.

Carrier documents intentionally do not include the complete native backing graph. This
checks 16 extracted table records and never certifies native regeneration or rendering.
"""
import argparse
import copy
import gzip
import hashlib
import json
import tempfile
from pathlib import Path
import ezdxf
from verify_mleader_inputs import records, check


def payload(record):
    begin = record.index([100, "AcDbBlockReference"])
    end = next((i for i in range(begin, len(record)) if record[i][0] == 1001), len(record))
    return record[begin:end]


def verify(expected, actual):
    check(expected == actual, "Native TABLE subclass tags changed, including private or redundant data")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    inventory = json.loads((root / "tools/table_oracle/fixtures.json").read_text())["files"]
    expected_names = set()
    checked = negative = full = 0
    with tempfile.TemporaryDirectory() as temporary:
        for item in inventory:
            source_bytes = gzip.decompress((root / "tests/fixtures/table-oracle" / (item["file"] + ".gz")).read_bytes())
            check(hashlib.sha256(source_bytes).hexdigest() == item["sha256"], "Pinned source digest changed")
            source_path = Path(temporary) / item["file"]
            source_path.write_bytes(source_bytes)
            source = records(source_path)
            tables = {handle: value for handle, value in source.items() if value[0] == [0, "ACAD_TABLE"]}
            check(len(tables) == item["tables"], "Source TABLE count changed")
            if item["file"] in ("acad_table_simple.dxf", "acad_table_with_blk_ref.dxf"):
                for binary in (False, True):
                    name = f"stored-table-full-{item['file']}-{binary}.dxf"
                    full_path = args.directory / name
                    check(full_path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Wrong full native transport")
                    full_doc = ezdxf.readfile(full_path)
                    check(full_doc.dxfversion == item["profile"], "Wrong full native profile")
                    full_records = records(full_path)
                    for handle, before in tables.items():
                        verify(payload(before), payload(full_records[handle]))
                    for handle, before in source.items():
                        if before[0][1] not in ("TABLECONTENT", "TABLEGEOMETRY", "TABLESTYLE", "CELLSTYLEMAP"):
                            continue
                        before_start = next(i for i, tag in enumerate(before) if tag[0] == 100)
                        after = full_records[handle]
                        after_start = next(i for i, tag in enumerate(after) if tag[0] == 100)
                        check(before[before_start:] == after[after_start:], "Full native backing subclass changed")
                        check(next(v for c, v in before if c == 330) == next(v for c, v in after if c == 330), "Backing common owner changed")
                    full += 1
                    print("PASS " + name)
            for handle, before in tables.items():
                expected = payload(before)
                for binary in (False, True):
                    name = f"stored-table-native-{item['file']}-{handle}-{binary}.dxf"
                    expected_names.add(name)
                    path = args.directory / name
                    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Wrong table transport")
                    doc = ezdxf.readfile(path)
                    check(doc.dxfversion == item["profile"], "Wrong source profile")
                    saved = [r for r in records(path).values() if r[0] == [0, "ACAD_TABLE"]]
                    check(len(saved) == 1, "TABLE lost, duplicated, or replaced with INSERT")
                    actual = payload(saved[0]); verify(expected, actual); checked += 1
                    for corrupt_code in (91, 142):
                        corrupted = copy.deepcopy(actual)
                        index = next(i for i, tag in enumerate(corrupted) if tag[0] == corrupt_code)
                        corrupted[index][1] += 1
                        try:
                            verify(expected, corrupted)
                        except ValueError:
                            negative += 1
                        else:
                            raise AssertionError("Corrupt TABLE dimension escaped exact packet gate")
                    print("PASS " + name)
    check({p.name for p in args.directory.glob("stored-table-native-*.dxf")} == expected_names, "Expected complete native TABLE corpus outputs")
    check(len(list(args.directory.glob("stored-table-full-*.dxf"))) == full == 4, "Expected four full native document outputs")
    print(f"PASS {checked} native TABLE packets, {full} full native outputs, and {negative} corruption controls; no regeneration claim")


if __name__ == "__main__":
    main()
