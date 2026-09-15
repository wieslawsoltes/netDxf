#!/usr/bin/env python3
"""Independently verify serialized APPID names and XData carrier membership.

These 36 drawings cover six profiles and both transports. The verifier checks
the exact registry name, binary payload, carrier identity, and absence of stale
names. In-memory callback atomicity and ownership use the conformance suite.
"""
from pathlib import Path
import argparse
import tempfile

import ezdxf

from verify_typed_container_inputs import wire_records

VERSIONS = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021",
            2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}
PREFIXES = ("appid-lifecycle-", "appid-carriers-", "appid-members-")


def check(condition, message):
    if not condition:
        raise ValueError(message)


def verify(path, year, binary, prefix):
    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary,
          "Physical transport differs")
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == VERSIONS[year], "Physical version differs")
    wire = wire_records(path)
    if prefix == "appid-lifecycle-":
        name = "QA_RENAMED"
        stale = {"QA_APP", "CALLER_ONLY"}
        lines = list(doc.modelspace().query("LINE"))
        check(len(lines) == 1 and len(doc.modelspace()) == 1,
              "Model-space geometry inventory differs")
        check(tuple(lines[0].dxf.start) == (0, 0, 0)
              and tuple(lines[0].dxf.end) == (1, 0, 0), "LINE geometry differs")
        vports = [item for item in doc.viewports if item.dxf.name == "QA_VIEW"]
        check(len(vports) == 2, "Repeated VPORT records were collapsed")
        expected = [(lines[0], b"\x63\x02"), (doc.layers.get("QA_LAYER"), b"\x01\x02"),
                    (doc.rootdict["QA_RECORD"], b"\x01\x02")]
        expected.extend((item, b"\x01\x02") for item in vports)
    elif prefix == "appid-carriers-":
        name = "RENAMED_VIEWPORT_DATA"
        stale = {"VIEWPORT_DATA"}
        check(set(doc.layouts.names()) == {"Model", "FIRST", "SECOND"},
              "Paper-space layout inventory differs")
        expected = []
        for layout_name in ("FIRST", "SECOND"):
            viewports = list(doc.layouts.get(layout_name).query("VIEWPORT"))
            check(len(viewports) == 1 and viewports[0].dxf.id == 1,
                  "Main layout VIEWPORT identity differs")
            expected.append((viewports[0], b"\x01\x02"))
    elif prefix == "appid-members-":
        name = "RENAMED_CARRIERS"
        stale = {"CARRIERS"}
        inserts = list(doc.modelspace().query("INSERT"))
        check(len(inserts) == 1 and len(inserts[0].attribs) == 1,
              "INSERT/ATTRIB inventory differs")
        check(inserts[0].dxf.name == "META" and inserts[0].attribs[0].dxf.tag == "TAG"
              and inserts[0].attribs[0].dxf.text == "value", "Attribute identity/value differs")
        expected = [(inserts[0].attribs[0], b"\x01\x02"),
                    (doc.blocks.get("META").block, b"\x03\x02"),
                    (doc.blocks.get("META").endblk, b"\x05\x02")]
    else:
        raise ValueError("Unknown fixture family")

    appids = [item.dxf.name for item in doc.appids]
    check(appids.count(name) == 1 and not stale.intersection(appids),
          "APPID table has stale or duplicated registry names")
    physical_appids = [record for record in wire.values() if record[0].value == "APPID"]
    check(sum(any(tag.code == 2 and tag.value == name for tag in record)
              for record in physical_appids) == 1, "Physical APPID table identity differs")
    wanted = {item.dxf.handle for item, _ in expected}
    actual = {handle for handle, record in wire.items()
              if any(tag.code == 1001 and tag.value == name for tag in record)}
    check(actual == wanted, "Physical XData carrier membership differs")
    for record in wire.values():
        check(not any(tag.code == 1001 and tag.value in stale for tag in record),
              "Stale XData application name retained")
    for item, payload in expected:
        check(list(item.get_xdata(name)) == [(1004, payload)],
              item.dxftype() + ": independent binary payload differs")
        record = wire[item.dxf.handle]
        start = next(index for index, tag in enumerate(record)
                     if tag.code == 1001 and tag.value == name)
        end = next((index for index in range(start + 1, len(record))
                    if record[index].code == 1001), len(record))
        check([(tag.code, tag.value) for tag in record[start + 1:end]] == [(1004, payload)],
              item.dxftype() + ": physical XData packet differs")
    audit = doc.audit()
    check(not audit.errors and not audit.fixes,
          "Independent audit altered the drawing: " + str(audit.errors + audit.fixes))


def negative_checks(artifacts):
    source = artifacts / "appid-lifecycle-AutoCad2018-False.dxf"
    data = source.read_bytes()
    # Neither corruption changes DXF structure; exact checks must detect both.
    corruptions = (data.replace(b"QA_RENAMED", b"QA_STALE", 1),
                   data.replace(b"6302", b"6202", 1))
    check(all(item != data for item in corruptions), "Negative controls did not change input")
    with tempfile.TemporaryDirectory() as temporary:
        path = Path(temporary) / source.name
        for altered in corruptions:
            path.write_bytes(altered)
            try:
                verify(path, 2018, False, "appid-lifecycle-")
            except (ValueError, ezdxf.DXFError):
                continue
            raise ValueError("Verifier accepted a corrupted APPID or binary payload")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    expected = {f"{prefix}AutoCad{year}-{binary}.dxf"
                for prefix in PREFIXES for year in VERSIONS for binary in (False, True)}
    actual = {path.name for prefix in PREFIXES for path in args.artifacts.glob(prefix + "*.dxf")}
    check(actual == expected, "Missing/extra APPID fixtures: " + str(actual ^ expected))
    for prefix in PREFIXES:
        for year in VERSIONS:
            for binary in (False, True):
                path = args.artifacts / f"{prefix}AutoCad{year}-{binary}.dxf"
                verify(path, year, binary, prefix)
                print("PASS", path.name)
    negative_checks(args.artifacts)
    print("36 APPID drawings, 120 exact XData carrier packets, and 2 negative controls passed.")


if __name__ == "__main__":
    main()
