#!/usr/bin/env python3
"""Verify declared 360/361 child ownership; this gate does not qualify TABLE cell data."""
import argparse
import gzip
import hashlib
import json
import tempfile
from pathlib import Path

import ezdxf
from verify_mleader_inputs import records, check


PROFILES = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021", 2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}


def inspect(path, year, binary):
    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Wrong transport")
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == PROFILES[year], "Wrong profile")
    wire = records(path)
    dictionary = doc.rootdict["OWNERSHIP_TEST"]
    record = dictionary["ACAD_XREC_ROUNDTRIP"]
    check(record.dxf.owner == dictionary.dxf.handle, "Wrapper common owner changed")
    tags = wire[record.dxf.handle]
    start = tags.index([100, "AcDbXrecord"]) + 1
    payload = tags[start:]
    check(payload.pop(0) == [280, 1], "Wrapper cloning policy changed")
    content = next(value for code, value in payload if code == 360)
    geometry = next(value for code, value in payload if code == 361)
    check(content != geometry and content != record.dxf.handle and geometry != record.dxf.handle, "Ownership identities collapsed")
    check(payload == [[102, "ACAD_ROUNDTRIP_2008_TABLE_ENTITY"], [360, content], [70, 2], [90, 1],
                      [10, [0.0, 0.0, 0.0]], [90, 0], [90, 2], [361, geometry]],
          "Exact stored wrapper envelope changed")
    for handle, kind, subclass in ((content, "TABLECONTENT", "PrivateOwnershipTableContent"), (geometry, "TABLEGEOMETRY", "PrivateOwnershipTableGeometry")):
        check(wire[handle] == [[0, kind], [5, handle], [330, record.dxf.handle], [100, subclass], [90, 0]],
              "Child identity, owner, or structural test body changed")
    check(len(list(doc.modelspace())) == 0, "Structural ownership fixture gained entities")


def inspect_native(directory):
    repository = Path(__file__).resolve().parents[1]
    inventory = json.loads((repository / "tools/table_oracle/fixtures.json").read_text())["files"]
    expected = {f"declared-native-{item['file']}-{wrapper['handle']}-{kind}.dxf"
                for item in inventory for wrapper in item["wrappers"] for kind in ("text", "binary")}
    check({p.name for p in directory.glob("declared-native-*.dxf")} == expected, "Expected all16 extracted native envelopes")
    with tempfile.TemporaryDirectory() as temporary:
        for item in inventory:
            original = gzip.decompress((repository / "tests/fixtures/table-oracle" / (item["file"] + ".gz")).read_bytes())
            check(hashlib.sha256(original).hexdigest() == item["sha256"], "Native fixture bytes changed")
            source = Path(temporary) / item["file"]
            source.write_bytes(original)
            source_wire = records(source)
            for wrapper in item["wrappers"]:
                for binary in (False, True):
                    path = directory / f"declared-native-{item['file']}-{wrapper['handle']}-{'binary' if binary else 'text'}.dxf"
                    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Extracted native transport changed")
                    doc = ezdxf.readfile(path)
                    check(doc.dxfversion == item["profile"], "Extracted native profile changed")
                    wire = records(path)
                    target = doc.entitydb[wrapper["handle"]]
                    owner = doc.entitydb[target.dxf.owner]
                    check(owner.dxftype() == "DICTIONARY" and owner["NATIVE_WRAPPER"] is target, "Native wrapper registration changed")
                    before, after = source_wire[wrapper["handle"]], wire[wrapper["handle"]]
                    start_before = before.index([100, "AcDbXrecord"])
                    start_after = after.index([100, "AcDbXrecord"])
                    check(before[start_before:] == after[start_after:], "Native XRECORD payload or cloning policy changed")
                    for child in wrapper["children"]:
                        check(wire[child["handle"]] == source_wire[child["handle"]], "Native stored child body/identity/owner changed")
                    print("PASS " + path.name)
    print("PASS16 native owning envelopes and unchanged stored child records; actual source dependencies are retained without evaluation")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    names = {f"declared-ownership-AutoCad{year}-{kind}.dxf" for year in PROFILES for kind in ("text", "binary")}
    check({p.name for p in args.directory.glob("declared-ownership-AutoCad*.dxf")} == names, "Expected all 12 structural ownership fixtures")
    for year in PROFILES:
        for binary in (False, True):
            path = args.directory / f"declared-ownership-AutoCad{year}-{'binary' if binary else 'text'}.dxf"
            inspect(path, year, binary)
            print("PASS " + path.name)
    print("PASS 12 exact ownership envelopes and reciprocal child identities; TABLE cell/display semantics remain outside this gate")
    inspect_native(args.directory)


if __name__ == "__main__":
    main()
