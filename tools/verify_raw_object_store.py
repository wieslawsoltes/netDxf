#!/usr/bin/env python3
"""Independently check raw-object transaction fixtures with optional ezdxf."""
from __future__ import annotations
import argparse
import struct
import io
import re
from pathlib import Path
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler

def check(condition, message):
    if not condition:
        raise ValueError(message)

def exact(a, b):
    return struct.pack("<d", a) == struct.pack("<d", b)

def value(tags, code):
    matches = [t.value for t in tags if t.code == code]
    check(len(matches) == 1, f"Expected one payload group {code}")
    return matches[0]

def xrecord_payloads(path):
    """Keep application100/101: ezdxf's high-level XRecord.tags truncates them."""
    data = path.read_bytes()
    tags = tag_compiler(binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF")
                        else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig", errors="replace"), newline=None)))
    records = []
    for tag in tags:
        if tag.code == 0: records.append([])
        if records: records[-1].append(tag)
    result = {}
    for record in records:
        if record[0].value != "XRECORD": continue
        marker = next(i for i, t in enumerate(record) if t.code == 100 and t.value == "AcDbXrecord")
        handle = next(t.value for t in record[:marker] if t.code == 5)
        payload = record[marker + 1:]
        if payload and payload[0].code == 280: payload = payload[1:]
        result[handle] = payload
    return result

def inspect(path):
    doc = ezdxf.readfile(path)
    profile = re.search(r"AutoCad(20\d\d)-(False|True)\.dxf$", path.name)
    versions = {"2000":"AC1015", "2004":"AC1018", "2007":"AC1021", "2010":"AC1024", "2013":"AC1027", "2018":"AC1032"}
    check(profile is not None and doc.dxfversion == versions[profile[1]], "DXF header version does not match fixture profile")
    with path.open("rb") as stream:
        check(stream.read(22).startswith(b"AutoCAD Binary DXF") == (profile[2] == "True"), "Transport does not match fixture profile")
    payloads = xrecord_payloads(path)
    source = doc.rootdict["NETDXF_OBJECT_TESTS"]
    clone = doc.rootdict["NETDXF_OBJECT_COPY"]
    check(set(source.keys()) == {"Placeholder", "Record", "VARIABLE", "Buffer", "Fallback", "Record alias"}, "Dictionary keys changed")
    check(source.dxf.hard_owned == 1, "Dictionary hard-owner flag changed")
    for dictionary in (source, clone):
        check(dictionary["Record"] is dictionary["Record alias"], "Dictionary aliases no longer share a target")
        for _, child in dictionary.items():
            check(child.dxf.owner == dictionary.dxf.handle, "Dictionary child owner changed")
        fallback = dictionary["Fallback"]
        check(fallback["missing"] is fallback["Default"], "Dictionary-wide fallback changed")
        check(fallback["Default"].dxftype() == "ACDBPLACEHOLDER", "Default placeholder missing")
        check(fallback["Default"].dxf.owner == fallback.dxf.handle, "Default ownership changed")
        record = dictionary["Record"]
        tags = payloads[record.dxf.handle]
        check([tag.code for tag in tags] == [1,10,70,90,160,290,310,330,320,100,101,102,280], "Ordered XRECORD payload fields changed")
        check(record.dxf.cloning == 1, "XRECORD cloning flag changed")
        check(value(tags, 1) == "raw application value", "XRECORD string changed")
        xyz = value(tags, 10)
        check(all(exact(a, b) for a, b in zip(xyz, (1.0000000000000002, -0.0, -13.25))), "XRECORD point bits changed")
        check(value(tags, 70) == -32768 and value(tags, 90) == 2147483647, "XRECORD integer changed")
        check(value(tags, 160) == -9223372036854775808, "XRECORD int64 changed")
        check(value(tags, 290) == 1, "XRECORD Boolean changed")
        check(value(tags, 310) == bytes(range(127)), "XRECORD binary changed")
        check(value(tags, 330) == dictionary["Placeholder"].dxf.handle, "Clone internal pointer did not remap")
        check(value(tags, 320) == "DEAD", "Arbitrary handle was incorrectly remapped")
        check(value(tags, 100) == "application subclass-like text", "Application100 payload changed")
        check(value(tags, 101) == "Embedded Object", "Application101 payload changed")
        check(value(tags, 102) == "{application payload", "XRECORD application marker changed")
        check(value(tags, 280) == 93, "Payload280 confused with cloning280")
        # DXF escape decoding is a separate explicit step in ezdxf's object API.
        decoded = decode_dxf_unicode(dictionary["VARIABLE"].dxf.value)
        decoded = decoded.encode("utf-16", "surrogatepass").decode("utf-16")
        check(decoded == "Żółć Ω 🧪 \\U+0041", "Portable variable Unicode changed")
    check(source.dxf.handle != clone.dxf.handle, "Clone root reused identity")
    check(source["Record"].dxf.handle != clone["Record"].dxf.handle, "Clone record reused identity")
    line = list(doc.modelspace().query("LINE"))[0]
    circle = list(doc.modelspace().query("CIRCLE"))[0]
    check(list(source["Buffer"].handles) == [line.dxf.handle, "0", circle.dxf.handle, line.dxf.handle], "IDBUFFER nulls/order/duplicates changed")
    if doc.dxfversion >= "AC1018":
        check(list(doc.modelspace().get_redraw_order()) == [(line.dxf.handle, "0"), (circle.dxf.handle, "FFFFFFFFFFFFFFFE")], "SORTENTSTABLE order/keys changed")
        check(doc.header["$SORTENTS"] & 16 == 16, "Regeneration draw-order flag missing")
    audit = doc.audit()
    check(not audit.errors and not audit.fixes, f"{len(audit.errors)} errors/{len(audit.fixes)} repairs")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    paths = sorted(args.directory.glob("object-store-*.dxf"))
    if not paths:
        parser.error("No object-store-*.dxf fixtures found")
    expected = {
        f"object-store-AutoCad{year}-{binary}.dxf"
        for year in (2000, 2004, 2007, 2010, 2013, 2018)
        for binary in ("False", "True")
    }
    missing = expected - {path.name for path in paths}
    if missing:
        parser.error("Missing conformance fixtures: " + ", ".join(sorted(missing)))
    for path in paths:
        inspect(path)
        print("PASS " + path.name)
    print(f"PASS ezdxf {ezdxf.__version__}: {len(paths)} object graph fixtures")

if __name__ == "__main__":
    main()
