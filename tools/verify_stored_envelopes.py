#!/usr/bin/env python3
"""Qualify 60 stored SPATIAL_INDEX/VBA_PROJECT outputs against pinned inputs.

The inputs contain public-schema-authored envelopes. No native spatial-index
evaluation, VBA interpretation, or macro execution is part of this verifier.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import struct

import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "tests/fixtures/stored-envelopes"
PROFILES = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021",
            2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}
PAYLOAD = bytes((index * 37 + 11) % 256 for index in range(300))
INDEPENDENT_CHUNKS = [b"", PAYLOAD[:127], PAYLOAD[127:130], b"",
                      PAYLOAD[130:257], PAYLOAD[257:], b""]


def check(value, message):
    if not value:
        raise ValueError(message)


def decode_once(value):
    return decode_dxf_unicode(value).encode("utf-16-le", "surrogatepass").decode("utf-16-le")


def exact(value):
    if isinstance(value, float):
        return struct.pack(">d", value)
    if isinstance(value, str):
        return decode_once(value)
    if isinstance(value, bytes):
        return value
    if hasattr(value, "xyz"):
        return tuple(exact(float(part)) for part in value)
    return value


def values(tags):
    return [(tag.code, exact(tag.value)) for tag in tags]


def read(path, year, binary):
    data = path.read_bytes()
    check(data.startswith(b"AutoCAD Binary DXF") == binary,
          path.name + ": transport differs from filename")
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == PROFILES[year], path.name + ": profile differs from filename")
    encoding = "utf-8-sig" if year >= 2007 else "cp1252"
    loader = binary_tags_loader(data) if binary else ascii_tags_loader(
        io.StringIO(data.decode(encoding), newline=None))
    records, current = {}, []

    def finish():
        if not current or current[0].value in ("SECTION", "ENDSEC", "EOF", "CLASS"):
            return
        end = next((i for i, tag in enumerate(current) if tag.code in (100, 1001)), len(current))
        identities = [tag.value for tag in current[:end] if tag.code in (5, 105)]
        check(len(identities) <= 1, path.name + ": duplicate record identity")
        if identities:
            handle = identities[0]
            check(handle not in records, path.name + ": duplicate database handle " + handle)
            records[handle] = list(current)

    for tag in tag_compiler(loader):
        if tag.code == 0:
            finish()
            current = []
        current.append(tag)
    finish()
    return doc, records


def payload(record):
    first = next(i for i, tag in enumerate(record) if tag.code == 100)
    end = next((i for i in range(first, len(record)) if record[i].code == 1001), len(record))
    return record[first:end]


def metadata(record):
    owners, reactors, extensions = [], [], []
    index = 1
    while index < len(record) and record[index].code not in (100, 1001):
        tag = record[index]
        if tag.code == 330:
            owners.append(tag.value)
        elif tag.code == 102:
            name = tag.value
            content = []
            index += 1
            while index < len(record) and not (record[index].code == 102 and record[index].value == "}"):
                content.append(record[index])
                index += 1
            check(index < len(record), "Unclosed common metadata group")
            if name == "{ACAD_REACTORS":
                check(all(t.code == 330 for t in content), "Invalid reactor content")
                reactors.extend(t.value for t in content)
            elif name == "{ACAD_XDICTIONARY":
                check(len(content) == 1 and content[0].code == 360, "Invalid extension attachment")
                extensions.append(content[0].value)
            else:
                raise ValueError("Unexpected private common metadata in qualified envelope")
        index += 1
    check(len(owners) == 1 and len(extensions) <= 1, "Ambiguous owner/extension metadata")
    return owners[0], reactors, extensions[0] if extensions else None


def xdata(record):
    result, active = {}, None
    for tag in record:
        if tag.code == 1001:
            active = decode_once(tag.value)
            check(active not in result, "Repeated XData APPID")
            result[active] = []
        elif active is not None:
            check(1000 <= tag.code <= 1071, "Envelope payload crossed into XData")
            result[active].append((tag.code, exact(tag.value)))
    return result


def spatial(record, timestamp):
    check(record[0].value == "SPATIAL_INDEX", "Wrong spatial object type")
    check(values(payload(record)) == [(100, "AcDbIndex"), (40, struct.pack(">d", timestamp)),
                                     (100, "AcDbSpatialIndex")], "Spatial timestamp bits or subclass boundaries changed")


def vba(record, chunks):
    check(record[0].value == "VBA_PROJECT", "Wrong VBA object type")
    tags = payload(record)
    check([tag.code for tag in tags] == [100, 90] + [310] * len(chunks),
          "VBA count/chunk framing or empty physical boundaries changed")
    check(tags[0].value == "AcDbVbaProject", "VBA subclass changed")
    actual = [tag.value for tag in tags[2:]]
    check(actual == chunks and tags[1].value == sum(map(len, chunks)), "VBA exact chunks/count changed")
    check(all(len(chunk) <= 127 for chunk in actual) and len(actual) <= 262144
          and sum(map(len, actual)) <= 16 * 1024 * 1024, "VBA admission constraints violated")


def classes(doc, records, year, source=False):
    count = sum(record[0].value == "SPATIAL_INDEX" for record in records.values())
    if count:
        definition = doc.classes.get("SPATIAL_INDEX")
        check(definition.dxf.cpp_class_name == "AcDbSpatialIndex"
              and definition.dxf.app_name == "ObjectDBX Classes"
              and definition.dxf.flags == 0
              and definition.dxf.was_a_proxy == 0 and definition.dxf.is_an_entity == 0,
              "SPATIAL_INDEX CLASS identity/flags changed")
        if not source:
            check(definition.dxf.instance_count == count if year >= 2004
                  else not definition.dxf.hasattr("instance_count"), "Spatial CLASS physical count/profile differs")
    check(not any(item.dxf.name == "VBA_PROJECT" for item in doc.classes),
          "Unqualified VBA CLASS declaration was introduced")


def geometry(doc, start, end):
    lines = list(doc.modelspace().query("LINE"))
    check(len(doc.modelspace()) == 1 and len(lines) == 1, "Following geometry inventory changed")
    check(tuple(lines[0].dxf.start) == start and tuple(lines[0].dxf.end) == end,
          "Following geometry values changed")
    return lines[0].dxf.handle


def audit(doc, label):
    result = doc.audit()
    check(not result.errors and not result.fixes,
          f"{label}: {len(result.errors)} audit errors/{len(result.fixes)} repairs")


def independent(doc, records, entry, source_doc, source_records):
    kind = entry["kind"]
    graph = doc.rootdict["QA_STORED_ENVELOPES"]
    check(doc.rootdict.dxf.handle == entry["root"] and graph.dxf.handle == entry["owner"],
          "Independent root/owner identity changed")
    check(metadata(records[entry["owner"]]) == (entry["root"], [], None),
          "Independent graph common owner changed")
    expected_names = {target["name"] for target in entry["targets"]} | {"FOLLOWING"}
    check(set(graph.keys()) == expected_names, "Independent dictionary inventory changed")
    check(values(payload(records[entry["owner"]])) == values(payload(source_records[entry["owner"]])),
          "Owner dictionary names, pointer strengths or flags changed")
    check(geometry(doc, (1, 2, 3), (8, 13, 21)) == entry["line"], "Following LINE identity changed")
    check("QA_STORED_ENVELOPES" in doc.appids, "Independent APPID missing")
    expected_type = "SPATIAL_INDEX" if kind == "spatial" else "VBA_PROJECT"
    check(sum(r[0].value == expected_type for r in records.values()) == len(entry["targets"]),
          "Independent physical envelope count changed")
    check(not any(r[0].value == ("VBA_PROJECT" if kind == "spatial" else "SPATIAL_INDEX")
                  for r in records.values()), "Unrelated envelope type introduced")
    for index, target in enumerate(entry["targets"]):
        handle = target["handle"]
        item = graph[target["name"]]
        check(item.dxf.handle == handle, "Independent envelope identity changed")
        record = records[handle]
        check(metadata(record) == (entry["owner"], [entry["owner"]], target["extension"]),
              "Independent owner/reactor/extension mapping changed")
        check(xdata(record) == xdata(source_records[handle]), "Independent XData changed")
        tags = xdata(record)["QA_STORED_ENVELOPES"]
        check([code for code, _ in tags] == [1000, 1005, 1070]
              and tags[1][1] == entry["line"] and tags[2][1] == index, "Independent XData reference/index changed")
        if kind == "spatial":
            timestamp = (2451544.5000000005, -0.0)[index]
            check(target["timestamp_hex"] == struct.pack(">d", timestamp).hex(), "Pinned timestamp expectation changed")
            spatial(record, timestamp)
        else:
            chunks = (INDEPENDENT_CHUNKS, [], [b"", b""])[index]
            check(target["chunks"] == [chunk.hex() for chunk in chunks], "Pinned chunk expectation changed")
            vba(record, chunks)
            check(item.data == b"".join(chunks), "Independent VBA reader disagrees with raw data")
        extension = item.get_extension_dict().dictionary
        original = source_doc.entitydb[handle].get_extension_dict().dictionary
        check(extension.dxf.handle == target["extension"] and extension.dxf.owner == handle
              and set(extension.keys()) == {"NOTE"}, "Independent extension ownership/inventory changed")
        check(values(payload(records[extension.dxf.handle])) == values(payload(source_records[extension.dxf.handle])),
              "Extension dictionary payload changed")
        note, old_note = extension["NOTE"], original["NOTE"]
        check(note.dxf.handle == old_note.dxf.handle and note.dxf.owner == extension.dxf.handle,
              "Extension NOTE identity/owner changed")
        check(values(payload(records[note.dxf.handle])) == values(payload(source_records[old_note.dxf.handle])),
              "Extension NOTE payload changed")
    following = graph["FOLLOWING"]
    check(following.dxf.handle == entry["following"] and following.dxf.owner == entry["owner"],
          "Following object identity/owner changed")
    check(values(payload(records[entry["following"]])) == values(payload(source_records[entry["following"]])),
          "Following object payload changed")


def authored(doc, records, year):
    graph = doc.rootdict["STORED_AUTHORED"]
    expected = {"INDEX", "VBA"}
    check(set(graph.keys()) == expected, "Authored envelope inventory/profile changed")
    check(metadata(records[graph.dxf.handle]) == (doc.rootdict.dxf.handle, [], None),
          "Authored graph common owner changed")
    check(values(payload(records[graph.dxf.handle])) == [
        (100, "AcDbDictionary"), (280, 1), (281, 1),
        (3, "INDEX"), (360, graph["INDEX"].dxf.handle),
        (3, "VBA"), (360, graph["VBA"].dxf.handle)],
        "Authored graph flags, ordered keys or hard-owner links changed")
    check(sum(r[0].value == "SPATIAL_INDEX" for r in records.values()) == 1
          and sum(r[0].value == "VBA_PROJECT" for r in records.values()) == 1,
          "Authored physical object counts changed")
    line = geometry(doc, (2, 3, 5), (7, 11, 13))
    check("STORED_AUTHORED" in doc.appids, "Authored APPID missing")
    for name in expected:
        item = graph[name]
        record = records[item.dxf.handle]
        extension = item.get_extension_dict().dictionary
        check(metadata(record) == (graph.dxf.handle, [graph.dxf.handle], extension.dxf.handle),
              "Authored owner/reactor/extension mapping changed")
        check(extension.dxf.owner == item.dxf.handle and set(extension.keys()) == {"NOTE"},
              "Authored extension ownership/inventory changed")
        note = extension["NOTE"]
        check(note.dxftype() == "DICTIONARYVAR" and note.dxf.owner == extension.dxf.handle,
              "Authored extension variable type/owner changed")
        check(values(payload(records[note.dxf.handle])) == [(100, "DictionaryVariables"), (280, 0),
                                                           (1, r"stored \U+0041")],
              "Authored literal escape or extension schema changed")
        check(xdata(record) == {"STORED_AUTHORED": [(1005, line), (1000, "inert bytes")]},
              "Authored XData reference/order changed")
        if name == "INDEX":
            spatial(record, -1234.125)
        else:
            chunks = [bytes(range(127)), b"", bytes([0, 255, 17])]
            vba(record, chunks)
            check(item.data == b"".join(chunks), "Authored VBA raw/reader disagreement")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    manifest = json.loads((FIXTURES / "manifest.json").read_text())
    check(manifest["producer"] == "ezdxf 1.4.4 plus explicit public-schema tag patch",
          "Unexpected independent producer declaration")
    sources = manifest["sources"]
    expected_sources = {f"independent-stored-{kind}-R{year}-{transport}.dxf": (kind, year, transport == "binary")
                        for kind in ("spatial", "vba") for year in PROFILES
                        for transport in ("ascii", "binary")}
    check(len(sources) == 24 and {entry["filename"] for entry in sources} == set(expected_sources),
          "Missing, duplicate or unexpected source manifest entries")
    check({path.name for path in FIXTURES.glob("independent-stored-*.dxf")} == set(expected_sources),
          "Missing or extra pinned source files")
    expected_outputs = {f"{Path(name).stem}-roundtrip-{transport}.dxf"
                        for name in expected_sources for transport in ("ascii", "binary")}
    expected_outputs |= {f"stored-authored-R{year}-{transport}.dxf"
                         for year in PROFILES for transport in ("ascii", "binary")}
    actual = {path.name for pattern in ("independent-stored-*.dxf", "stored-authored-*.dxf")
              for path in args.artifacts.glob(pattern)}
    check(actual == expected_outputs, "Missing/extra stored-envelope outputs: " + str(sorted(actual ^ expected_outputs)))
    for entry in sources:
        kind, year, binary = expected_sources[entry["filename"]]
        check(entry["kind"] == kind and entry["version"] == PROFILES[year]
              and entry["binary"] is binary, "Manifest filename/profile/transport disagreement")
        check(len(entry["targets"]) == (2 if kind == "spatial" else 3)
              and [target["name"] for target in entry["targets"]] == [f"ITEM_{i}" for i in range(2 if kind == "spatial" else 3)],
              "Pinned target count/names changed")
        source = FIXTURES / entry["filename"]
        check(hashlib.sha256(source.read_bytes()).hexdigest() == entry["sha256"], "Source fixture hash changed")
        original, before = read(source, year, binary)
        independent(original, before, entry, original, before)
        classes(original, before, year, source=True)
        audit(original, source.name)
        for output_binary in (False, True):
            name = f"{source.stem}-roundtrip-{'binary' if output_binary else 'ascii'}.dxf"
            saved, wire = read(args.artifacts / name, year, output_binary)
            independent(saved, wire, entry, original, before)
            classes(saved, wire, year)
            audit(saved, name)
            print("PASS", name)
    for year in PROFILES:
        for binary in (False, True):
            name = f"stored-authored-R{year}-{'binary' if binary else 'ascii'}.dxf"
            doc, wire = read(args.artifacts / name, year, binary)
            authored(doc, wire, year)
            classes(doc, wire, year)
            audit(doc, name)
            print("PASS", name)
    print(f"PASS ezdxf {ezdxf.__version__}: 60 stored-envelope drawings, 24 pinned sources; "
          "exact bytes/timestamp bits/chunk boundaries, profiles, identities and graphs; zero audit changes")


if __name__ == "__main__":
    main()
