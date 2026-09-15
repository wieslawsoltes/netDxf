#!/usr/bin/env python3
"""Qualify the fifth mixed ownership/clone/erasure graph and native TABLE packets.

All 48 emitted DXFs and 12 actual clone maps are mandatory. Native subclass tags
are extracted independently from hash-pinned source files. Their group 342 is
deliberately bound to a synthetic XRECORD to test generic semantic references;
this fixture does not qualify native TABLESTYLE compatibility or TABLE authoring.
"""
from __future__ import annotations

import argparse
import copy
import gzip
import hashlib
import io
import json
from pathlib import Path
import re
import struct

import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from verify_fourth_mixed_modules import (
    PROFILES, body, check, decode_once, dictionary_edges, exact, load, metadata, xdata,
)

REPO = Path(__file__).resolve().parent.parent
NAMES = ["Unresolved layer", "Unresolved layer", "unresolved layer"]
PATH_TYPES = {"/": "DICTIONARY", "SHARED": "XRECORD", "INDEX": "LAYER_INDEX",
              "POINTER": "OBJECT_PTR", "FILTER": "LAYER_FILTER", "LINKS": "XRECORD",
              **{f"INDEX/buffer/{i}": "IDBUFFER" for i in range(3)},
              "POINTER/extension": "DICTIONARY", "POINTER/extension/NOTE": "DICTIONARYVAR"}
DATA_TYPES = {"DATA": "DATATABLE", "DATA/hard/0": "XRECORD",
              "DATA/hard/2": "ACDBPLACEHOLDER", "DATA/soft/0": "DICTIONARYVAR"}
NATIVE_FILES = {2004: "sample_AC1018_ascii.dxf", 2007: "sample_AC1021_ascii.dxf",
                2010: "sample_AC1024_ascii.dxf", 2013: "acad_table_simple.dxf",
                2018: "acad_table_with_blk_ref.dxf"}
CLASS_METADATA = {"LAYER_INDEX": ("AcDbLayerIndex", "ObjectDBX Classes", 0),
                  "LAYER_FILTER": ("AcDbLayerFilter", "ObjectDBX Classes", 0),
                  "OBJECT_PTR": ("CAseDLPNTableRecord", "", 1),
                  "DATATABLE": ("AcDbDataTable", "ObjectDBX Classes", 0)}


def handle(value):
    check(isinstance(value, str) and re.fullmatch(r"[0-9a-fA-F]+", value) is not None,
          "Invalid mapping handle: " + repr(value))
    number = int(value, 16)
    check(number > 0, "Null mapping identity")
    return number


def raw_exact(value):
    """Keep the original native string spelling, including escaped text."""
    if isinstance(value, float):
        return struct.pack(">d", value)
    if isinstance(value, (tuple, list)):
        return tuple(raw_exact(part) for part in value)
    return value


def packet(record):
    start = next((i for i, tag in enumerate(record) if tag == (100, "AcDbBlockReference")), -1)
    check(start >= 0, "Native TABLE block-reference boundary missing")
    end = next((i for i in range(start, len(record)) if record[i][0] == 1001), len(record))
    return [(code, raw_exact(value)) for code, value in record[start:end]]


def native_packet(year, value):
    if year == 2000:
        check(value is None, "R2000 must remain graph-only")
        return None
    check(set(value) == {"file", "sha256", "sourceEntity", "sourceProfile", "emittedEntity",
                         "pointerCode", "pointerValue", "syntheticTarget"}, "Native sidecar fields differ")
    check(value["file"] == NATIVE_FILES[year] and value["sourceProfile"] == "AutoCad" + str(year),
          "Native source file/profile differs")
    check(value["syntheticTarget"] == "SHARED XRECORD; native TABLESTYLE semantics are not qualified",
          "Synthetic target qualification is missing")
    path = REPO / "tests/fixtures/table-oracle" / (value["file"] + ".gz")
    raw = gzip.decompress(path.read_bytes())
    inventory = json.loads((REPO / "tools/table_oracle/fixtures.json").read_text())["files"]
    pinned = next(item["sha256"] for item in inventory if item["file"] == value["file"])
    check(hashlib.sha256(raw).hexdigest() == value["sha256"] == pinned, "Native source hash differs")
    tags = [(tag.code, tag.value) for tag in tag_compiler(ascii_tags_loader(io.StringIO(
        raw.decode("utf-8-sig" if year >= 2007 else "cp1252"), newline=None)))]
    version = next(tags[i + 1][1] for i, tag in enumerate(tags[:-1]) if tag == (9, "$ACADVER"))
    check(version == PROFILES[year], "Pinned native file actual profile differs")
    records, current, section, tables = [], [], None, []
    for tag in tags:
        if tag[0] == 0:
            if current:
                records.append(current)
            current = []
        current.append(tag)
    records.append(current)
    for record in records:
        if record[0] == (0, "SECTION"):
            section = record[1][1]
        elif record[0] == (0, "ENDSEC"):
            section = None
        elif section == "ENTITIES" and record[0] == (0, "ACAD_TABLE"):
            tables.append(record)
    check(tables, "Pinned source has no native TABLE")
    source = tables[0]
    identity = next(v for c, v in source if c == 5)
    check(identity == value["sourceEntity"], "Native extraction selected another source entity")
    result = packet(source)
    pointer = next(v for c, v in result if c == 342 and int(v, 16))
    check(value["pointerCode"] == 342 and value["pointerValue"] == pointer,
          "Native semantic pointer source mapping differs")
    return result


def mapping(path, year, binary):
    value = json.loads(path.read_text())
    check(set(value) == {"version", "binary", "pairs", "external", "line", "sourceArbitrary", "copyArbitrary", "native"},
          "Mixed map fields differ")
    check(value["version"] == "AutoCad" + str(year) and value["binary"] is binary,
          "Mixed map profile/transport differs")
    types = dict(PATH_TYPES, **(DATA_TYPES if year >= 2004 else {}))
    check(len(value["pairs"]) == len(types) and {p["path"] for p in value["pairs"]} == set(types),
          "Mixed map path inventory differs")
    for pair in value["pairs"]:
        check(set(pair) == {"path", "type", "source", "destination"} and pair["type"] == types[pair["path"]],
              "Mixed map path/type differs")
        check(handle(pair["source"]) != handle(pair["destination"]), "Clone retained source numeric identity")
    for name in ("external", "line"):
        check(set(value[name]) == {"source", "destination"}, "External map fields differ")
        check(handle(value[name]["source"]) != handle(value[name]["destination"]), "External map reused source identity")
    for phase, arbitrary in (("source", "sourceArbitrary"), ("destination", "copyArbitrary")):
        handles = {p["path"]: p[phase] for p in value["pairs"]}
        identities = [handle(h) for h in handles.values()] + [handle(value[k][phase]) for k in ("external", "line")]
        check(len(set(identities)) == len(identities), "Different mixed roles alias numerically")
        check(value[arbitrary] == [handles["SHARED"], handles["INDEX/buffer/0"]], "Arbitrary handle controls differ")
    native = native_packet(year, value["native"])
    return value, native


def expected_bodies(handles, external, line, arbitrary):
    h = handles
    edges = [(name, 360, h[name]) for name in ("SHARED", "INDEX")]
    edges += [("INDEX_ALIAS", 350, h["INDEX"])]
    edges += [(name, 360, h[name]) for name in ("POINTER", "FILTER", "LINKS")]
    if "DATA" in h:
        edges.append(("DATA", 360, h["DATA"]))
    result = {
        "/": [(100, "AcDbDictionary"), (280, 0), (281, 1)] +
             [tag for name, code, target in edges for tag in ((3, name), (code, target))],
        "SHARED": [(100, "AcDbXrecord"), (280, 1), (1, "shared source pointer target")],
        "INDEX": [(100, "AcDbIndex"), (40, exact(2451545.625)), (100, "AcDbLayerIndex")] +
                 [tag for i, name in enumerate(NAMES) for tag in ((8, name), (360, h[f"INDEX/buffer/{i}"]), (90, (6, 3, 0)[i]))],
        "FILTER": [(100, "AcDbFilter"), (100, "AcDbLayerFilter")] + [(8, name) for name in NAMES],
        "POINTER": [],
        "LINKS": [(100, "AcDbXrecord"), (280, 1), (340, h["SHARED"]), (330, external),
                  (320, arbitrary[0]), (329, arbitrary[1])],
        "INDEX/buffer/0": [(100, "AcDbIdBuffer")] + [(330, target) for target in
                            (h["SHARED"], h["SHARED"], "0", line, h["INDEX"], h.get("DATA", h["POINTER"]))],
        "INDEX/buffer/1": [(100, "AcDbIdBuffer"), (330, h["INDEX/buffer/0"]), (330, external), (330, "0")],
        "INDEX/buffer/2": [(100, "AcDbIdBuffer")],
        "POINTER/extension": [(100, "AcDbDictionary"), (280, 1), (281, 1), (3, "NOTE"), (360, h["POINTER/extension/NOTE"])],
        "POINTER/extension/NOTE": [(100, "DictionaryVariables"), (280, 0), (1, "mixed extension")],
    }
    if "DATA" in h:
        columns = [(1, "values", 93, [-2147483648, 0, 2147483647]),
                   (3, "values", 3, ["Ω中", r"literal \U+0041", ""]),
                   (6, "owned", 360, [h["DATA/hard/0"], "0", h["DATA/hard/2"]]),
                   (7, "owned", 350, [h["DATA/soft/0"], "0", "0"]),
                   (8, "pointers", 340, [h["SHARED"], h["SHARED"], h["INDEX"]]),
                   (9, "pointers", 330, [h["INDEX/buffer/0"], external, "0"]),
                   (5, "geometry", 331, [line, "0", line])]
        result["DATA"] = [(100, "AcDbDataTable"), (70, 2), (90, 7), (91, 3), (1, "Mixed Ω data")]
        for kind, name, code, values in columns:
            result["DATA"] += [(92, kind), (2, name)] + [(code, value) for value in values]
        result["DATA/hard/0"] = [(100, "AcDbXrecord"), (280, 1), (1, "hard owned row"), (340, h["SHARED"])]
        result["DATA/hard/2"] = []
        result["DATA/soft/0"] = [(100, "DictionaryVariables"), (280, 0), (1, "soft owned row")]
    return result


def native_table(doc, records, sidecar, native, phase):
    actual = [record for record in records.values() if record[0] == (0, "ACAD_TABLE")]
    if native is None or phase != "source":
        check(not actual, "Unexpected TABLE outside its pinned source graph")
        return
    info = sidecar["native"]
    check(len(actual) == 1 and actual[0][1] == (5, info["emittedEntity"]), "Native carrier identity differs")
    record = actual[0]
    check(packet(record) == native, "Native TABLE exact source payload differs")
    shared = next(p[phase] for p in sidecar["pairs"] if p["path"] == "SHARED")
    check(handle(info["pointerValue"]) == handle(shared), "Native pointer no longer targets synthetic shared identity")
    check(metadata(record) == (doc.modelspace().block_record_handle, [], None), "Native carrier owner differs")
    prefix = record[:next(i for i, tag in enumerate(record) if tag == (100, "AcDbBlockReference"))]
    check([(c, decode_once(v)) for c, v in prefix if c == 430] == [(430, "Book$Ω")], "TABLE color metadata edit differs")
    check([(c, v) for c, v in prefix if c == 8] == [(8, "FIFTH_TABLE_LAYER")], "TABLE layer metadata edit differs")
    check(xdata(record) == [(1001, "FIFTH_TABLE_EDITED"), (1000, "allowed common metadata edit")],
          "TABLE APPID common metadata edit differs")


def graph(doc, records, sidecar, native, phase):
    h = {p["path"]: p[phase] for p in sidecar["pairs"]}
    nod, external, line = doc.rootdict.dxf.handle, sidecar["external"][phase], sidecar["line"][phase]
    custom = [edge for edge in dictionary_edges(records[nod]) if edge[0].startswith("FIFTH_")]
    check(custom == [("FIFTH_EXTERNAL", 360, external), ("FIFTH_GRAPH", 360, h["/"])], "Mixed NOD ownership differs")
    expected = expected_bodies(h, external, line, sidecar["sourceArbitrary"])
    if phase == "source":
        expected["LINKS"][2] = (340, "000" + h["SHARED"].lower())
    types = {p["path"]: p["type"] for p in sidecar["pairs"]}
    app = "FIFTH_APP" if phase == "source" else "FIFTH_APP_CLONE"
    for path, identity in h.items():
        check(identity in records and records[identity][:2] == [(0, types[path]), (5, identity)], phase + " " + path + ": identity/type differs")
        record = records[identity]
        check(body(record) == expected[path], phase + " " + path + ": exact ordered body differs")
        owner = nod if path == "/" else h["INDEX"] if path.startswith("INDEX/buffer/") else h["DATA"] if path.startswith("DATA/") else h["POINTER"] if path == "POINTER/extension" else h["POINTER/extension"] if path.endswith("/NOTE") else h["/"]
        reactors = [external] if path == "/" else [h["SHARED"]] if path == "INDEX" else [h["INDEX"]] if path == "SHARED" else []
        extension = h["POINTER/extension"] if path == "POINTER" else None
        check(metadata(record) == (owner, reactors, extension), phase + " " + path + ": owner/reactor/extension differs")
        wanted = [(1001, app), (1005, h["SHARED"]), (1005, external), (1004, bytes([0, 255, 7]))] if path in ("POINTER", "INDEX/buffer/1") else []
        check(xdata(record) == wanted, phase + " " + path + ": APPID/reference mapping differs")
    check(all(name not in doc.layers for name in NAMES), "Stored unresolved names were turned into layers")
    for name in ("LAYER_INDEX", "IDBUFFER", "DATATABLE", "LAYER_FILTER", "OBJECT_PTR", "ACDBPLACEHOLDER"):
        check({key for key, record in records.items() if record[0] == (0, name)} == {h[p] for p, kind in types.items() if kind == name}, name + ": actual object inventory differs")
    owners = {}
    for identity, record in records.items():
        in_block = False
        for code, value in record[2:]:
            if code in (100, 1001):
                break
            if code == 102:
                in_block = value != "}"
            elif code == 330 and not in_block:
                owners[identity] = value
    closure = {h["/"]}
    while True:
        expanded = closure | {identity for identity, owner in owners.items() if owner in closure}
        if expanded == closure:
            break
        closure = expanded
    check(closure == set(h.values()), "Actual mixed ownership closure differs from the clone map")
    arbitrary = sidecar["sourceArbitrary" if phase == "source" else "copyArbitrary"]
    check(body(records[external]) == [(100, "AcDbXrecord"), (280, 1), (1, "external survivor"), (320, arbitrary[0]), (329, arbitrary[1])], "External arbitrary controls differ")
    check(metadata(records[external]) == (nod, [], None), "External survivor ownership differs")
    check([key for key, record in records.items() if record[0] == (0, "LINE")] == [line], "External LINE inventory differs")
    check([(code, raw_exact(value)) for code, value in records[line] if code in (10, 11)] ==
          [(10, raw_exact((1.0, 2.0, 3.0))), (11, raw_exact((4.0, 5.0, 6.0)))], "External LINE geometry differs")
    appids = [v for r in records.values() if r[0] == (0, "APPID") for c, v in r if c == 2 and v.startswith("FIFTH_APP")]
    check(appids == [app], "Canonical APPID rename differs")
    native_table(doc, records, sidecar, native, phase)


def classes(records, declarations, year, erased):
    for name, (cpp, application, flags) in CLASS_METADATA.items():
        matches = [record for record in declarations if (1, name) in record]
        if name == "DATATABLE" and year == 2000:
            check(not matches, "R2000 emitted DATATABLE CLASS")
            continue
        expected = [(0, "CLASS"), (1, name), (2, cpp), (3, application), (90, flags)]
        if year >= 2004:
            expected.append((91, 0 if erased else 1))
        expected += [(280, 0), (281, 0)]
        check(matches == [expected], name + ": exact CLASS metadata/count differs")
        check(sum(record[0] == (0, name) for record in records.values()) == (0 if erased else 1), name + ": physical CLASS inventory differs")


def erased_graph(doc, records, sidecar, phase, before):
    deleted = {handle(p[phase]) for p in sidecar["pairs"]}
    if phase == "source" and sidecar["native"]:
        deleted.add(handle(sidecar["native"]["emittedEntity"]))
    check(not deleted.intersection(handle(identity) for identity in records), "Erased identity was resurrected")
    check(not any(record[0][1] in (*CLASS_METADATA, "IDBUFFER", "ACAD_TABLE", "ACDBPLACEHOLDER") for record in records.values()), "Erased envelope was reallocated")
    nod, external, line = doc.rootdict.dxf.handle, sidecar["external"][phase], sidecar["line"][phase]
    custom = [edge for edge in dictionary_edges(records[nod]) if edge[0].startswith("FIFTH_")]
    check(custom == [("FIFTH_EXTERNAL", 360, external)], "Erased graph/blocker alias survived")
    for identity in (external, line):
        check(records[identity] == before[1][identity], "Erasure changed external survivor identity/payload")
    for identity, record in records.items():
        for code, value in record:
            if 330 <= code <= 369 or 390 <= code <= 399 or code in (480, 481, 1005):
                check(int(value, 16) not in deleted, "Surviving semantic pointer targets erased closure: " + identity)
    check(handle(doc.header["$HANDSEED"]) >= handle(before[0].header["$HANDSEED"]), "Erasure lowered HANDSEED")


def negative_controls(source, copied, sidecar, native):
    """Mutate real independently parsed output records with legal DXF values."""
    doc, records, _ = copied
    h = {p["path"]: p["destination"] for p in sidecar["pairs"]}
    changes = [("INDEX", 90, lambda tag: (90, 7)),
               ("INDEX/buffer/0", 330, lambda tag: (330, h["/"])),
               ("LINKS", 320, lambda tag: (320, h["SHARED"])),
               ("LINKS", 340, lambda tag: (340, sidecar["external"]["destination"])),
               ("POINTER", 1005, lambda tag: (1005, sidecar["external"]["destination"]))]
    if "DATA" in h:
        changes += [("DATA", 360, lambda tag: (340, tag[1])),
                    ("DATA/hard/0", 330, lambda tag: (330, h["INDEX"]))]
    count = 0
    for path, code, mutate in changes:
        changed = copy.deepcopy(records)
        record = changed[h[path]]
        index = next(i for i, tag in enumerate(record) if tag[0] == code)
        record[index] = mutate(record[index])
        try:
            graph(doc, changed, sidecar, native, "destination")
        except ValueError as error:
            check(str(error).startswith("destination " + path + ":"), "Corruption rejected for unrelated reason: " + str(error))
            count += 1
        else:
            raise ValueError("Accepted actual output corruption: " + path + "/" + str(code))
    if native:
        doc, records, _ = source
        changed = copy.deepcopy(records)
        record = changed[sidecar["native"]["emittedEntity"]]
        index = next(i for i, tag in enumerate(record) if tag == (100, "AcDbTable")) + 1
        index = next(i for i in range(index, len(record)) if record[i][0] == 91)
        record[index] = (91, record[index][1] + 1)
        try:
            native_table(doc, changed, sidecar, native, "source")
        except ValueError as error:
            check(str(error) == "Native TABLE exact source payload differs", "Native corruption failed for unrelated reason")
            count += 1
        else:
            raise ValueError("Accepted native TABLE payload corruption")
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    check(ezdxf.__version__ == "1.4.4", "Qualification requires pinned ezdxf 1.4.4")
    phases = ("source", "copy", "source-erased", "copy-erased")
    expected = {f"fifth-mixed-AutoCad{year}-{binary}-{phase}.dxf" for year in PROFILES for binary in (False, True) for phase in phases}
    expected |= {f"fifth-mixed-AutoCad{year}-{binary}-map.json" for year in PROFILES for binary in (False, True)}
    actual = {path.name for path in args.artifacts.glob("fifth-mixed-*") if path.is_file()}
    check(actual == expected, "Missing/extra fifth mixed artifacts: " + repr(sorted(actual ^ expected)))
    negatives = 0
    for year in PROFILES:
        for binary in (False, True):
            prefix = f"fifth-mixed-AutoCad{year}-{binary}"
            sidecar, native = mapping(args.artifacts / (prefix + "-map.json"), year, binary)
            drawings = {phase: load(args.artifacts / (prefix + "-" + phase + ".dxf"), year, binary) for phase in phases}
            for phase, key in (("source", "source"), ("copy", "destination")):
                doc, records, declarations = drawings[phase]
                graph(doc, records, sidecar, native, key)
                classes(records, declarations, year, False)
                doc, records, declarations = drawings[phase + "-erased"]
                erased_graph(doc, records, sidecar, key, drawings[phase])
                classes(records, declarations, year, True)
            negatives += negative_controls(drawings["source"], drawings["copy"], sidecar, native)
            for phase in phases:
                audit = drawings[phase][0].audit()
                check(not audit.errors and not audit.fixes,
                      f"{prefix}-{phase}: {len(audit.errors)} ezdxf audit errors/{len(audit.fixes)} repairs")
                print("PASS", prefix + "-" + phase + ".dxf")
    print(f"PASS 48 drawings + 12 actual clone maps; exact pinned TABLE packets, mixed ownership/ref maps, "
          f"APPID rename and erased closures; {negatives} actual parsed-output corruption controls; "
          "zero ezdxf audit errors/repairs")


if __name__ == "__main__":
    main()
