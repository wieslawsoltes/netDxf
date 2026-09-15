#!/usr/bin/env python3
"""Qualify all 36 fourth-batch graph drawings and their 12 actual clone maps.

Use ezdxf's complete low-level records for public object envelopes and reference
packets; its high-level LAYER_FILTER reader omits the ordered group 8 names.
This checks stored data and graph lifecycle, not filter execution or VBA code.
"""
from __future__ import annotations

import argparse
import copy
import io
import json
from pathlib import Path
import re
import struct

import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler


PROFILES = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021",
            2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}
PATH_TYPES = {"ROOT": "DICTIONARY", "ROOT/FILTER": "LAYER_FILTER",
              "ROOT/POINTER": "OBJECT_PTR", "ROOT/POINTER/EXTENSION": "DICTIONARY",
              "ROOT/POINTER/EXTENSION/NOTE": "DICTIONARYVAR",
              "ROOT/SPATIAL": "SPATIAL_INDEX", "ROOT/PROJECT": "VBA_PROJECT",
              "ROOT/LINKS": "XRECORD", "ROOT/BUFFER": "IDBUFFER"}
NAMES = ["Missing layer", "missing layer", "Missing layer", "Ω中", r"literal \U+0041"]
LIGHT_NAMES = ["Stored light", "", r"literal \U+0042"]
CHUNKS = [b"", bytes(255 - index for index in range(127)), b"",
          bytes([0, 255, 65, 0, 128]), b""]
CLASS_METADATA = {
    "LAYER_FILTER": ("AcDbLayerFilter", "ObjectDBX Classes", 0),
    "OBJECT_PTR": ("CAseDLPNTableRecord", "", 1),
    "SPATIAL_INDEX": ("AcDbSpatialIndex", "ObjectDBX Classes", 0),
}


def check(condition, message):
    if not condition:
        raise ValueError(message)


def decode_once(value):
    return decode_dxf_unicode(value).encode("utf-16-le", "surrogatepass").decode("utf-16-le")


def exact(value):
    if isinstance(value, float):
        return struct.pack(">d", value)
    if isinstance(value, (tuple, list)):
        return tuple(exact(part) for part in value)
    return decode_once(value) if isinstance(value, str) else value


def handle(value):
    check(isinstance(value, str) and re.fullmatch(r"[0-9A-F]+", value) is not None
          and int(value, 16) > 0, "Invalid/noncanonical mapping handle: " + repr(value))
    return int(value, 16)


def load(path, year, binary):
    raw = path.read_bytes()
    check(raw.startswith(b"AutoCAD Binary DXF") == binary, path.name + ": actual transport differs")
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == PROFILES[year], path.name + ": actual profile differs")
    loader = binary_tags_loader(raw) if binary else ascii_tags_loader(io.StringIO(
        raw.decode("utf-8-sig" if year >= 2007 else "cp1252"), newline=None))
    records, classes, current, numeric_handles = {}, [], [], set()

    def finish():
        if not current:
            return
        if current[0] == (0, "CLASS"):
            classes.append(list(current))
            return
        if current[0][1] in ("SECTION", "ENDSEC", "EOF"):
            return
        end = next((i for i, tag in enumerate(current) if tag[0] in (100, 1001)), len(current))
        identities = [value for code, value in current[:end] if code in (5, 105)]
        check(len(identities) <= 1, path.name + ": duplicate identity marker")
        if identities:
            identity = identities[0]
            number = handle(identity)
            check(number not in numeric_handles, path.name + ": duplicate numeric database identity")
            numeric_handles.add(number)
            records[identity] = list(current)

    for tag in tag_compiler(loader):
        if tag.code == 0:
            finish()
            current = []
        current.append((tag.code, tag.value))
    finish()
    check(handle(doc.header["$HANDSEED"]) > max(numeric_handles), path.name + ": HANDSEED permits identity reuse")
    return doc, records, classes


def metadata(record):
    owners, reactors, extensions, blocks = [], [], [], set()
    index = 2
    while index < len(record) and record[index][0] not in (100, 1001):
        code, value = record[index]
        if code == 330:
            owners.append(value)
        elif code == 102:
            check(value in ("{ACAD_REACTORS", "{ACAD_XDICTIONARY") and value not in blocks,
                  "Unexpected/duplicate common metadata block")
            blocks.add(value)
            content = []
            index += 1
            while index < len(record) and record[index] != (102, "}"):
                content.append(record[index])
                index += 1
            check(index < len(record), "Unclosed common metadata block")
            if value == "{ACAD_REACTORS":
                check(all(code == 330 for code, _ in content), "Invalid common reactor packet")
                reactors.extend(target for _, target in content)
            else:
                check(len(content) == 1 and content[0][0] == 360, "Invalid common extension packet")
                extensions.append(content[0][1])
        else:
            raise ValueError("Unexpected common metadata tag: " + repr(record[index]))
        index += 1
    check(len(owners) == 1 and len(extensions) <= 1, "Ambiguous common owner/extension")
    return owners[0], reactors, extensions[0] if extensions else None


def body(record):
    start = next((i for i, tag in enumerate(record) if tag[0] in (100, 1001)), len(record))
    end = next((i for i in range(start, len(record)) if record[i][0] == 1001), len(record))
    return [(code, exact(value)) for code, value in record[start:end]]


def xdata(record):
    start = next((i for i, tag in enumerate(record) if tag[0] == 1001), len(record))
    return [(code, exact(value)) for code, value in record[start:]]


def dictionary_edges(record):
    tags = body(record)
    check(tags[:3] in ([(100, "AcDbDictionary"), (280, 0), (281, 1)],
                       [(100, "AcDbDictionary"), (280, 1), (281, 1)]), "Dictionary header differs")
    tags = tags[3:]
    check(len(tags) % 2 == 0, "Unpaired dictionary entry")
    edges = []
    for index in range(0, len(tags), 2):
        check(tags[index][0] == 3 and tags[index + 1][0] in (350, 360), "Invalid dictionary edge grammar")
        edges.append((tags[index][1], tags[index + 1][0], tags[index + 1][1]))
    check(len({name for name, _, _ in edges}) == len(edges), "Duplicate dictionary key")
    return edges


def mapping(path, year, binary):
    value = json.loads(path.read_text())
    check(set(value) == {"version", "binary", "pairs", "external", "light", "arbitrary"}, "Mapping sidecar fields differ")
    check(value["version"] == "AutoCad" + str(year) and value["binary"] is binary,
          "Mapping sidecar profile/transport differs")
    pairs = value["pairs"]
    types = dict(PATH_TYPES, **({"ROOT/LIGHTS": "LIGHTLIST"} if year >= 2007 else {}))
    check(len(pairs) == len(types) and {pair["path"] for pair in pairs} == set(types),
          "Mapping path inventory differs")
    for pair in pairs:
        check(set(pair) == {"path", "type", "source", "destination"}
              and pair["type"] == types[pair["path"]], "Mapping path/type differs")
        check(handle(pair["source"]) != handle(pair["destination"]), "Clone retained source identity")
    check(set(value["external"]) == {"source", "destination"}, "External mapping fields differ")
    check(set(value["light"]) == {"source", "destination"}, "LIGHT mapping fields differ")
    if year >= 2007:
        check(handle(value["light"]["source"]) != handle(value["light"]["destination"]),
              "External LIGHT retained source identity")
    else:
        check(value["light"] == {"source": None, "destination": None}, "LIGHT mapping violates profile gate")
    for phase in ("source", "destination"):
        identifiers = [handle(pair[phase]) for pair in pairs] + [handle(value["external"][phase])]
        if year >= 2007:
            identifiers.append(handle(value["light"][phase]))
        check(len(set(identifiers)) == len(identifiers), "Different mapping roles alias numerically")
    check(handle(value["external"]["source"]) != handle(value["external"]["destination"]),
          "External mapping did not change identity")
    check(value["arbitrary"] == next(pair["source"] for pair in pairs if pair["path"] == "ROOT/PROJECT"),
          "Arbitrary320 is not the original project identity")
    return value


def graph(doc, records, sidecar, phase):
    handles = {pair["path"]: pair[phase] for pair in sidecar["pairs"]}
    root, external = handles["ROOT"], sidecar["external"][phase]
    nod = doc.rootdict.dxf.handle
    check(records[nod][0] == (0, "DICTIONARY"), "NOD physical object differs")
    custom = [edge for edge in dictionary_edges(records[nod]) if edge[0].startswith("FOURTH_")]
    check(custom == [("FOURTH_EXTERNAL", 360, external), ("FOURTH_GRAPH", 360, root)],
          "NOD custom aliases/ownership differ")
    pointer, project = handles["ROOT/POINTER"], handles["ROOT/PROJECT"]
    extension, note = handles["ROOT/POINTER/EXTENSION"], handles["ROOT/POINTER/EXTENSION/NOTE"]
    edges = [(name, 360, handles["ROOT/" + name]) for name in ("FILTER", "POINTER", "SPATIAL", "PROJECT")]
    edges += [("PROJECT_ALIAS", 350, project)]
    edges += [(name, 360, handles["ROOT/" + name]) for name in ("LINKS", "BUFFER")]
    light = sidecar["light"][phase]
    types = dict(PATH_TYPES)
    if light is not None:
        types["ROOT/LIGHTS"] = "LIGHTLIST"
        edges.append(("LIGHTS", 360, handles["ROOT/LIGHTS"]))
    expected = {
        "ROOT": [(100, "AcDbDictionary"), (280, 0), (281, 1)] +
                [tag for name, code, target in edges for tag in ((3, name), (code, target))],
        "ROOT/FILTER": [(100, "AcDbFilter"), (100, "AcDbLayerFilter")] + [(8, name) for name in NAMES],
        "ROOT/POINTER": [],
        "ROOT/POINTER/EXTENSION": [(100, "AcDbDictionary"), (280, 1), (281, 1), (3, "NOTE"), (360, note)],
        "ROOT/POINTER/EXTENSION/NOTE": [(100, "DictionaryVariables"), (280, 0), (1, "extension-note")],
        "ROOT/SPATIAL": [(100, "AcDbIndex"), (40, exact(2451545.125)), (100, "AcDbSpatialIndex")],
        "ROOT/PROJECT": [(100, "AcDbVbaProject"), (90, 132)] + [(310, chunk) for chunk in CHUNKS],
        "ROOT/LINKS": [(100, "AcDbXrecord"), (280, 1), (340, project), (330, external), (320, sidecar["arbitrary"])],
        "ROOT/BUFFER": [(100, "AcDbIdBuffer")] + [(330, target) for target in (project, root, external, project, "0")],
    }
    if light is not None:
        expected["ROOT/LIGHTS"] = [(100, "AcDbLightList"), (90, 42), (90, 3)] + [
            tag for name in LIGHT_NAMES for tag in ((5, light), (1, name))]
    for path, identity in handles.items():
        check(identity in records and records[identity][:2] == [(0, types[path]), (5, identity)],
              phase + " " + path + ": physical identity/type differs")
        record = records[identity]
        check(body(record) == expected[path], phase + " " + path + ": exact ordered body differs")
        owner = nod if path == "ROOT" else pointer if path.endswith("/EXTENSION") else extension if path.endswith("/NOTE") else root
        reactors = [external] if path == "ROOT" else [pointer] if path == "ROOT/SPATIAL" else [handles["ROOT/FILTER"]] if path == "ROOT/PROJECT" else []
        check(metadata(record) == (owner, reactors, extension if path == "ROOT/POINTER" else None),
              phase + " " + path + ": common ownership/reactors/extension differ")
        app = "FOURTH_APP" if phase == "source" else "FOURTH_APP_COPY"
        expected_xdata = [(1001, app), (1005, project), (1005, external)] if path == "ROOT/POINTER" else []
        check(xdata(record) == expected_xdata, phase + " " + path + ": XData internal/external mapping differs")
    check(dictionary_edges(records[root]) == edges, "Graph owning aliases differ")
    check(dictionary_edges(records[extension]) == [("NOTE", 360, note)], "Extension entry differs")
    check(all(name not in doc.layers for name in NAMES), "Unresolved filter names unexpectedly resolved to layers")
    check(body(records[external]) == [(100, "DictionaryVariables"), (280, 0), (1, "external-survivor")]
          and records[external][:2] == [(0, "DICTIONARYVAR"), (5, external)]
          and metadata(records[external]) == (nod, [], None) and not xdata(records[external]),
          "External mapped object envelope differs")
    for name in (*CLASS_METADATA, "VBA_PROJECT", "LIGHTLIST"):
        actual = [identity for identity, record in records.items() if record[0] == (0, name)]
        check(actual == [handles[path] for path, kind in types.items() if kind == name],
              name + ": physical object inventory differs")
    closure = {root}
    owners = {}
    for identity, record in records.items():
        block = False
        for code, value in record[1:]:
            if code in (100, 1001):
                break
            if code == 102:
                block = value != "}"
            elif code == 330 and not block:
                owners[identity] = value
    while True:
        expanded = closure | {identity for identity, owner in owners.items() if owner in closure}
        if expanded == closure:
            break
        closure = expanded
    check(closure == set(handles.values()), "Actual ownership closure differs from mapping path inventory")
    light_entity(doc, records, light)
    appid(records, app)


def appid(records, expected):
    matches = [record for record in records.values() if record[0] == (0, "APPID")
               and any(code == 2 and value.startswith("FOURTH_APP") for code, value in record)]
    check(len(matches) == 1 and [value for code, value in matches[0] if code == 2] == [expected],
          "Canonical APPID rename/registry inventory differs")
    return matches[0]


def light_entity(doc, records, identity):
    actual = [key for key, record in records.items() if record[0] == (0, "LIGHT")]
    check(actual == ([identity] if identity is not None else []), "External LIGHT physical inventory differs")
    check([entity.dxftype() for entity in doc.modelspace()] == (["LIGHT"] if identity is not None else []),
          "Unexpected modelspace entity inventory")
    if identity is None:
        return
    record = records[identity]
    check(record[:2] == [(0, "LIGHT"), (5, identity)]
          and metadata(record) == (doc.modelspace().block_record_handle, [], None)
          and not xdata(record), "External LIGHT identity/common metadata differs")
    check([value for code, value in body(record) if code == 1] == ["Actual entity light"],
          "External LIGHT stored name differs from independent list entry names")


def classes(doc, records, declarations, year, erased):
    metadata = dict(CLASS_METADATA)
    if year >= 2007:
        metadata["LIGHTLIST"] = ("AcDbLightList", "SCENEOE", 1025)
    else:
        check(not any((1, "LIGHTLIST") in record for record in declarations), "LIGHTLIST CLASS violates profile gate")
    for name, (cpp, application, flags) in metadata.items():
        matches = [record for record in declarations if (1, name) in record]
        check(len(matches) == 1, name + ": CLASS missing/duplicated")
        expected = [(0, "CLASS"), (1, name), (2, cpp), (3, application), (90, flags)]
        if year >= 2004:
            expected += [(91, 0 if erased else 1)]
        expected += [(280, 0), (281, 0)]
        check(matches[0] == expected, name + ": canonical CLASS metadata/count/profile differs")
        definition = doc.classes.get(name).dxf
        check((definition.cpp_class_name, definition.app_name, definition.flags,
               definition.was_a_proxy, definition.is_an_entity) == (cpp, application, flags, 0, 0),
              name + ": independent CLASS interpretation differs")
        check(sum(record[0] == (0, name) for record in records.values()) == (0 if erased else 1),
              name + ": CLASS differs from physical count")
    check(not any((1, "VBA_PROJECT") in record for record in declarations), "Unqualified VBA CLASS introduced")


def erased_graph(doc, records, sidecar, copied):
    deleted = {handle(pair["destination"]) for pair in sidecar["pairs"]}
    check(not deleted.intersection(handle(identity) for identity in records), "Erased ownership closure was resurrected")
    check(not any(record[0][1] in (*CLASS_METADATA, "VBA_PROJECT", "LIGHTLIST") for record in records.values()),
          "Erased envelope was written under a new handle")
    nod, external = doc.rootdict.dxf.handle, sidecar["external"]["destination"]
    custom = [edge for edge in dictionary_edges(records[nod]) if edge[0].startswith("FOURTH_")]
    check(custom == [("FOURTH_EXTERNAL", 360, external)], "Erased graph/blocker alias survives or external alias changed")
    check(records[external] == copied[1][external], "Erasure changed external survivor identity/payload/metadata")
    for identity, record in records.items():
        for code, value in record:
            if 330 <= code <= 369 or 390 <= code <= 399 or code in (480, 481, 1005):
                check(isinstance(value, str) and int(value, 16) not in deleted,
                      "Surviving exposed pointer targets erased closure: " + identity)
    check(handle(doc.header["$HANDSEED"]) >= handle(copied[0].header["$HANDSEED"]),
          "Erasure lowered the allocation seed")
    light = sidecar["light"]["destination"]
    light_entity(doc, records, light)
    if light is not None:
        check(records[light] == copied[1][light], "Erasure changed external LIGHT identity/payload/metadata")
    check(appid(records, "FOURTH_APP_COPY") == appid(copied[1], "FOURTH_APP_COPY"),
          "Erasure changed retained canonical APPID record")


def audit(doc, label):
    result = doc.audit()
    check(not result.errors and not result.fixes,
          f"{label}: {len(result.errors)} ezdxf audit errors/{len(result.fixes)} repairs")


def negative_controls(copied, sidecar):
    """Corrupt independently parsed records, preserving legal DXF tag types."""
    doc, records, _ = copied
    handles = {pair["path"]: pair["destination"] for pair in sidecar["pairs"]}
    for path, code, replacement in (
            ("ROOT/PROJECT", 310, None),
            ("ROOT/LINKS", 340, sidecar["arbitrary"])):
        changed = copy.deepcopy(records)
        record = changed[handles[path]]
        index = next(i for i, tag in enumerate(record) if tag[0] == code and (code != 310 or tag[1]))
        if code == 310:
            original = record[index][1]
            replacement = bytes([original[0] ^ 1]) + original[1:]
        record[index] = (code, replacement)
        try:
            graph(doc, changed, sidecar, "destination")
        except ValueError as error:
            check(str(error) == "destination " + path + ": exact ordered body differs",
                  "Negative control failed for an unrelated reason: " + str(error))
        else:
            raise ValueError("Negative control was accepted: " + path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    check(ezdxf.__version__ == "1.4.4", "Qualification requires pinned ezdxf 1.4.4")
    names = {f"fourth-mixed-AutoCad{year}-{binary}-{phase}.dxf"
             for year in PROFILES for binary in (False, True) for phase in ("source", "copy", "erased")}
    names |= {f"fourth-mixed-AutoCad{year}-{binary}-map.json" for year in PROFILES for binary in (False, True)}
    actual = {path.name for path in args.artifacts.glob("fourth-mixed-*") if path.is_file()}
    check(actual == names, "Missing/extra fourth mixed artifacts: " + repr(sorted(actual ^ names)))
    for year in PROFILES:
        for binary in (False, True):
            prefix = f"fourth-mixed-AutoCad{year}-{binary}"
            sidecar = mapping(args.artifacts / (prefix + "-map.json"), year, binary)
            phases = {phase: load(args.artifacts / (prefix + "-" + phase + ".dxf"), year, binary)
                      for phase in ("source", "copy", "erased")}
            for phase, key in (("source", "source"), ("copy", "destination")):
                doc, records, declarations = phases[phase]
                graph(doc, records, sidecar, key)
                classes(doc, records, declarations, year, False)
            if year >= 2007:
                check(body(phases["source"][1][sidecar["light"]["source"]]) ==
                      body(phases["copy"][1][sidecar["light"]["destination"]]),
                      "External LIGHT clone changed stored entity body")
            doc, records, declarations = phases["erased"]
            erased_graph(doc, records, sidecar, phases["copy"])
            classes(doc, records, declarations, year, True)
            for phase, (doc, _, _) in phases.items():
                audit(doc, prefix + "-" + phase)
                print("PASS", prefix + "-" + phase + ".dxf")
            negative_controls(phases["copy"], sidecar)
    print("PASS 36 drawings + 12 actual clone maps; exact envelopes, aliases, references and erased closures; "
          "24 negative corruption controls; zero ezdxf audit errors/repairs")


if __name__ == "__main__":
    main()
