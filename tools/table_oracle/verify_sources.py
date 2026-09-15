#!/usr/bin/env python3
"""Compare pinned-source flat TABLE literal values with the independent ACadSharp snapshots."""
import argparse
import hashlib
import json
from pathlib import Path

import ezdxf
from ezdxf.entities.acad_table import read_acad_table_content


def check(condition, message):
    if not condition:
        raise ValueError(message)


def inspect(source, snapshot, pinned):
    check(hashlib.sha256(source.read_bytes()).hexdigest() == pinned["sha256"], "Source bytes changed")
    check(snapshot["sha256"] == pinned["sha256"] and snapshot["passed"], "Snapshot did not read the pinned source strictly")
    doc = ezdxf.readfile(source)
    check(doc.dxfversion == pinned["profile"], "Source profile changed")
    tables = list(doc.modelspace().query("ACAD_TABLE"))
    check(len(tables) == len(snapshot["tables"]) == pinned["tables"], "Typed TABLE count changed")
    literal_values = 0
    for table in tables:
        result = next(item for item in snapshot["tables"] if item["handle"] == table.dxf.handle)
        tags = [(t.code, t.value) for t in table.xtags.get_subclass("AcDbTable")]
        starts = [i for i, (code, _) in enumerate(tags) if code == 171]
        prefix = tags[:starts[0]]
        rows = next(v for c, v in prefix if c == 91)
        columns = next(v for c, v in prefix if c == 92)
        check(len(result["cells"]) == rows and all(len(r) == columns for r in result["cells"]), "Typed grid dimensions changed")
        check(result["rowHeights"] == [v for c, v in prefix if c == 141], "Stored row heights changed")
        check(result["columnWidths"] == [v for c, v in prefix if c == 142], "Stored column widths changed")
        check(result["style"] == next(v for c, v in prefix if c == 342), "TABLESTYLE reference changed")
        check(result["block"] == next(v for c, v in prefix if c == 343), "Display-block reference changed")
        raw_text = read_acad_table_content(table)
        starts.append(len(tags))
        for i, (start, end) in enumerate(zip(starts, starts[1:])):
            packet = tags[start:end]
            actual = result["cells"][i // columns][i % columns]["contents"]
            if (301, "CELL_VALUE") not in packet:
                if pinned["profile"] == "AC1018" and actual and packet[0] == (171, 1):
                    check(actual[0]["value"] == raw_text[i // columns][i % columns], "Legacy literal text changed")
                    literal_values += 1
                continue
            begin = packet.index((301, "CELL_VALUE")) + 1
            finish = packet.index((304, "ACVALUE_END"), begin)
            value_tags = dict(packet[begin:finish])
            check(len(actual) == 1, "Flat cell value was omitted")
            value = actual[0]
            check(value["Flags"] == value_tags[93], "Stored value flags changed")
            for code, name in ((300, "Format"), (302, "FormattedValue")):
                if code in value_tags:
                    check(value[name] == value_tags[code], "Stored value display metadata changed")
            # Date, point, handle, formula, block, and multi-content semantics are deliberately unqualified.
            if value_tags[90] in (1, 2, 4) and not value_tags[93] & 1:
                kind, code = {1: ("Long", 91), 2: ("Double", 140), 4: ("String", 1)}[value_tags[90]]
                check(value["valueType"] == kind and value["value"] == value_tags[code], "Typed literal value changed")
                literal_values += 1
        check(result["content"]["handle"] == "0", "Reassess oracle: backing TABLECONTENT association changed")
    for wrapper in pinned["wrappers"]:
        record = doc.entitydb[wrapper["handle"]]
        payload = [[tag.code, list(tag.value) if not isinstance(tag.value, (str, int, float, bytes)) else tag.value] for tag in record.tags]
        check(payload == wrapper["payload"], "Pinned owning envelope changed")
        for child in wrapper["children"]:
            target = doc.entitydb[child["handle"]]
            check(target.dxftype() == child["kind"] and target.dxf.owner == wrapper["handle"], "Native ownership is not reciprocal")
    check(all(not item["found"] for item in snapshot["backingContents"]), "Reassess oracle: reader now retains backing TABLECONTENT")
    audit = doc.audit()
    check(not audit.errors and not audit.fixes, "Pinned source failed independent audit")
    return literal_values


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("snapshots", type=Path)
    parser.add_argument("source_roots", type=Path, nargs="+")
    args = parser.parse_args()
    manifest = json.loads(Path(__file__).with_name("fixtures.json").read_text())
    snapshots = {Path(item["path"]).name: item for item in json.loads(args.snapshots.read_text())}
    check(set(snapshots) == {item["file"] for item in manifest["files"]}, "Expected exactly five source snapshots")
    count = 0
    for pinned in manifest["files"]:
        paths = [p for root in args.source_roots for p in root.rglob(pinned["file"]) if p.is_file()]
        paths = [p for p in paths if hashlib.sha256(p.read_bytes()).hexdigest() == pinned["sha256"]]
        check(bool(paths), "Pinned fixture is missing: " + pinned["file"])
        values = inspect(paths[0], snapshots[pinned["file"]], pinned)
        count += values
        print(f"PASS {pinned['file']}: {pinned['tables']} typed grids, {values} literal values, exact native ownership packets")
    print(f"PASS five unchanged source files and {count} literal values; backing-object semantics and rendering remain unqualified")


if __name__ == "__main__":
    main()
