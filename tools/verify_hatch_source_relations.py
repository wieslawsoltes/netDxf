#!/usr/bin/env python3
"""Require and inspect every HATCH source-closure output with ezdxf 1.4.4.

The native evidence is an entity-packet transplant from pinned LibreDWG test
drawings into a minimal carrier. This verifies stored references and geometry;
it does not claim native CAD execution, associative updates, or rendering.
"""
from pathlib import Path
import argparse
import copy
import gzip
import hashlib
import io
import json
import struct
import tempfile

import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "tests/fixtures/hatch-source-relations"
VERSIONS = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021", 2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}
BINARY_SIGNATURE = b"AutoCAD Binary DXF\r\n\x1a\x00"


def check(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def wire_records(data):
    """Typed physical entity packets, retaining duplicate references and counts."""
    binary = data.startswith(BINARY_SIGNATURE)
    loader = binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None))
    result = {}
    packet = []

    def flush():
        if not packet:
            return
        identity = [v for c, v in packet if c in (5, 105)]
        if not identity:
            return
        check(len(identity) == 1 and identity[0] not in result, "Duplicate physical identity")
        result[identity[0]] = list(packet)

    for tag in tag_compiler(loader):
        if tag.code == 0:
            flush()
            packet = []
        value = tuple(tag.value) if tag.code in (10, 11, 12, 13, 210) else tag.value
        packet.append((tag.code, value))
    flush()
    return result


def first(tags, code):
    return next(value for tag, value in tags if tag == code)


def owner(tags):
    depth = 0
    for code, value in tags:
        if code == 102:
            depth += 1 if value.startswith("{") else -1
        elif code == 330 and depth == 0:
            return value
        elif code == 100:
            break
    raise ValueError("Entity owner is missing")


def paths(tags):
    index = next(i for i, tag in enumerate(tags) if tag[0] == 91)
    count = tags[index][1]
    index += 1
    result = []

    def take(code):
        nonlocal index
        check(index < len(tags) and tags[index][0] == code, f"Expected HATCH group {code} at {index}")
        value = tags[index][1]
        index += 1
        return value

    for _ in range(count):
        start = index
        flags = take(92)
        if flags & 2:
            bulges = take(72)
            closed = take(73)
            check(bulges in (0, 1) and closed in (0, 1), "Invalid polyline flags")
            vertices = take(93)
            check(vertices >= 0, "Negative vertex count")
            for _ in range(vertices):
                take(10)
                if bulges:
                    take(42)
        else:
            edges = take(93)
            check(edges >= 0, "Negative edge count")
            for _ in range(edges):
                check(take(72) == 1, "Expected independently produced LINE edge")
                take(10)
                take(11)
        geometry = tags[start:index]
        source_count = take(97)
        check(source_count >= 0, "Negative source boundary count")
        sources = [take(330) for _ in range(source_count)]
        result.append({"geometry": geometry, "source_count": source_count, "sources": sources})
    check(tags[index][0] == 75, "Boundary/source packet did not end at HATCH style")
    return result


def inventory(data):
    wire = wire_records(data)
    hatches = {}
    source_handles = set()
    lines = {}
    for handle, tags in wire.items():
        kind = tags[0][1]
        if kind == "HATCH":
            boundaries = paths(tags)
            hatches[handle] = {"owner": owner(tags), "associative": first(tags, 71), "path_count": first(tags, 91), "paths": boundaries}
            source_handles.update(source for boundary in boundaries for source in boundary["sources"])
        elif kind == "LINE":
            lines[handle] = {"owner": owner(tags), "start": first(tags, 10), "end": first(tags, 11)}
    sources = {}
    for handle in sorted(source_handles):
        check(handle in wire, "Unresolved source boundary: " + handle)
        tags = wire[handle]
        check(tags[0] == (0, "LWPOLYLINE"), "Source boundary type changed")
        start = next(i for i, tag in enumerate(tags) if tag == (100, "AcDbPolyline")) + 1
        # Optional zero width/thickness/elevation tags may be omitted on export.
        geometry = [(c, v) for c, v in tags[start:] if c in (90, 70, 10, 40, 41, 42) and (c not in (40, 41, 42) or v != 0)]
        sources[handle] = {"owner": owner(tags), "type": "LWPOLYLINE", "geometry": geometry}
    for hatch in hatches.values():
        check(hatch["associative"] in (0, 1), "Invalid associative flag")
        for boundary in hatch["paths"]:
            for source in boundary["sources"]:
                check(sources[source]["owner"] == hatch["owner"], "Source belongs to a different block")
    hatch_owners = {h["owner"] for h in hatches.values()}
    lines = {h: v for h, v in lines.items() if v["owner"] in hatch_owners}
    return {"hatches": hatches, "sources": sources, "following_lines": lines}


def canonical(value):
    """Keep exact floating-point values while accepting JSON list representation."""
    if isinstance(value, float):
        return ("double", struct.pack(">d", value).hex())
    if isinstance(value, dict):
        return {key: canonical(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [canonical(item) for item in value]
    return value


def compare(actual, expected):
    # Block-record handles may legitimately be remapped by the consuming writer.
    # Require every physical source/HATCH identity and the common modelspace owner.
    wanted = copy.deepcopy(expected)
    actual_owners = {v["owner"] for group in actual.values() for v in group.values()}
    check(len(actual_owners) == 1, "Expected all fixture geometry in one block")
    actual_owner = next(iter(actual_owners))
    for group in wanted.values():
        for entity in group.values():
            entity["owner"] = actual_owner
    check(canonical(actual) == canonical(wanted), "Stored HATCH source counts/order/duplicates, flags, identities, or geometry changed")


def validate_document(path, year, binary, expected):
    data = path.read_bytes()
    check(data.startswith(BINARY_SIGNATURE) == binary, f"{path.name}: actual transport signature differs")
    check(binary or not data.startswith(b"AutoCAD Binary DXF"), "Malformed binary signature")
    actual = inventory(data)
    compare(actual, expected)
    document = ezdxf.readfile(path)
    check(document.dxfversion == VERSIONS[year], f"{path.name}: DXF version differs")
    model = document.modelspace()
    check(all(entity.dxf.owner == model.block_record_handle for entity in model), "Actual modelspace owner differs")
    check({entity.dxf.handle for entity in model} == {handle for group in actual.values() for handle in group}, "Modelspace entity inventory differs")
    for hatch in model.query("HATCH"):
        expected_hatch = actual["hatches"][hatch.dxf.handle]
        check(hatch.dxf.associative == expected_hatch["associative"], "Independent typed associative flag differs")
        check([list(p.source_boundary_objects) for p in hatch.paths] == [p["sources"] for p in expected_hatch["paths"]], "Independent typed source list differs")
    following = list(model.query("LINE"))
    check(len(following) == 1 and tuple(following[0].dxf.start) == (1, 2, 3) and tuple(following[0].dxf.end) == (4, 5, 6), "Following LINE geometry differs")
    check(list(model)[-1].dxftype() == "LINE", "Following LINE is no longer after the HATCH packets")
    other_block = document.blocks.get("OTHER_SOURCE_BLOCK")
    other_entities = list(other_block)
    check(len(other_entities) == 1 and other_entities[0].dxftype() == "LINE" and other_entities[0].dxf.handle == "403", "Wrong-owner control block/source identity differs")
    other_line = other_entities[0]
    check(other_line.dxf.owner == other_block.block_record_handle and other_line.dxf.owner != model.block_record_handle, "Wrong-owner control source is in the wrong block")
    check(tuple(other_line.dxf.start) == (20, 30, 0) and tuple(other_line.dxf.end) == (40, 50, 0), "Wrong-owner control source geometry differs")
    audit = document.audit()
    check(not audit.errors and not audit.fixes, f"{path.name}: audit errors/repairs: {audit.errors + audit.fixes}")
    return actual


def native_provenance(original, fixture):
    """Compare extracted original packets without parsing unrelated native data."""
    compressed = (ROOT / original["gzip_path"]).read_bytes()
    check(sha256(compressed) == original["gzip_sha256"], "Pinned native gzip hash differs")
    data = gzip.decompress(compressed)
    check(sha256(data) == original["source_sha256"], "Pinned original drawing hash differs")
    lines = data.decode("latin1").splitlines(keepends=True)
    packets = []
    for i in range(0, len(lines), 2):
        code, value = int(lines[i]), lines[i + 1].strip()
        if code == 0:
            packets.append([])
        if packets:
            packets[-1].append((code, value, lines[i] + lines[i + 1]))
    selected = {}
    for packet in packets:
        handles = [v for c, v, _ in packet if c == 5]
        if handles and handles[0] in ("8E", "24F"):
            check(handles[0] not in selected, "Duplicate native packet identity")
            selected[handles[0]] = packet
    check(set(selected) == {"8E", "24F"}, "Original packet pair is missing")
    native_data = ("".join(raw for h in ("8E", "24F") for _, _, raw in selected[h]) + "  0\nEOF\n").encode("ascii")
    native_inventory = inventory(native_data)
    actual = inventory(fixture.read_bytes())
    actual["following_lines"] = {}
    compare(actual, native_inventory)
    receipt = original["packet_transplant"]
    actual_wire = wire_records(fixture.read_bytes())
    original_wire = wire_records(native_data)
    for packet in receipt["packets"]:
        handle = packet["handle"]
        raw = "".join(t[2] for t in selected[handle]).encode("ascii")
        check(sha256(raw) == packet["original_packet_sha256"], "Original packet hash differs")
        expected_tags = list(original_wire[handle])
        depth = 0
        for i, (code, value) in enumerate(expected_tags):
            if code == 102:
                depth += 1 if value.startswith("{") else -1
            elif code == 330 and depth == 0:
                check(value == receipt["original_owner"], "Native common owner differs")
                expected_tags[i] = (330, receipt["carrier_owner"])
                break
        check(canonical(actual_wire[handle]) == canonical(expected_tags), "Native entity body, reactors, source references, or declared owner adaptation differs")


def corruption_controls(path, expected, year):
    """Corrupt actual serialized output; exercise the complete independent reader."""
    controls = []
    text = path.read_bytes().decode("utf-8-sig").splitlines()
    pairs = [(int(text[i]), text[i + 1]) for i in range(0, len(text), 2)]
    start = next(i for i, pair in enumerate(pairs) if pair == (0, "HATCH"))
    end = next(i for i in range(start + 1, len(pairs)) if pairs[i][0] == 0)
    count_index = next(i for i in range(start, end) if pairs[i][0] == 97)
    for defect in ("dropped_duplicate_source", "wrong_associative_flag", "changed_source_identity"):
        bad = list(pairs)
        if defect == "dropped_duplicate_source":
            count = int(bad[count_index][1])
            check(count == 3 and bad[count_index + 1] == bad[count_index + 2], "Expected a duplicate source in actual output")
            bad[count_index] = (97, str(count - 1))
            del bad[count_index + 1]
        elif defect == "wrong_associative_flag":
            i = next(i for i in range(start, end) if bad[i][0] == 71)
            bad[i] = (71, str(1 - int(bad[i][1])))
        else:
            choices = set(expected["sources"]) - {bad[count_index + 1][1]}
            bad[count_index + 1] = (330, sorted(choices)[0])
        data = "".join(f"{code:3d}\n{value}\n" for code, value in bad).encode("utf-8")
        with tempfile.TemporaryDirectory(prefix="hatch-source-control-") as temporary:
            corrupted = Path(temporary) / (defect + ".dxf")
            corrupted.write_bytes(data)
            try:
                validate_document(corrupted, year, False, expected)
            except ValueError as error:
                controls.append({"defect": defect, "rejection": str(error), "sha256": sha256(data)})
            else:
                raise ValueError("Accepted serialized corruption control: " + defect)
    return controls


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    check(ezdxf.__version__ == "1.4.4", "Independent reader must be pinned ezdxf 1.4.4")
    manifest = json.loads((FIXTURES / "manifest.json").read_text())
    check(manifest["producer"]["name"] == "ezdxf" and manifest["producer"]["version"] == ezdxf.__version__, "Producer identity/version differs")
    check(sha256((FIXTURES / "generate.py").read_bytes()) == manifest["generator_sha256"], "Generator hash differs")
    check(sha256(Path(__file__).read_bytes()) == manifest["verifier_sha256"], "Verifier/generator helper hash differs")
    files = manifest["fixtures"]
    check(len(files) == 24 and {(r["kind"], r["year"], r["binary"]) for r in files} == {(k, y, b) for k in ("native", "producer") for y in VERSIONS for b in (False, True)}, "Mandatory fixture inventory differs")
    check(len(manifest["native_originals"]) == 6, "Native original inventory differs")
    outputs = []
    hatches = boundary_paths = source_references = 0
    control_source = None
    for source in files:
        fixture = FIXTURES / source["file"]
        check(sha256(fixture.read_bytes()) == source["sha256"], "Fixture hash differs: " + fixture.name)
        expected = validate_document(fixture, source["year"], source["binary"], source["inventory"])
        if source["kind"] == "native":
            original = next(r for r in manifest["native_originals"] if r["year"] == source["year"])
            native_provenance(original, fixture)
        for binary in (False, True):
            filename = f'hatch-source-{source["kind"]}-AutoCad{source["year"]}-{source["binary"]}-{binary}.dxf'
            path = args.artifacts / filename
            check(path.is_file(), "Missing mandatory output: " + filename)
            actual = validate_document(path, source["year"], binary, expected)
            if source["kind"] == "producer" and not binary:
                control_source = path, expected, source["year"]
            outputs.append({"file": filename, "sha256": sha256(path.read_bytes())})
            hatches += len(actual["hatches"])
            for hatch in actual["hatches"].values():
                boundary_paths += hatch["path_count"]
                source_references += sum(p["source_count"] for p in hatch["paths"])
    controls = corruption_controls(*control_source)
    check(len(outputs) == 48 and hatches == 72 and boundary_paths == 120 and source_references == 264 and len(controls) == 3, "Mandatory output/packet/control inventory differs")
    print(json.dumps({"independent_reader": "ezdxf " + ezdxf.__version__, "fixtures": 24, "native_originals": 6, "outputs": len(outputs), "hatches": hatches, "boundary_paths": boundary_paths, "source_references": source_references, "negative_controls": controls, "audit_errors": 0, "audit_fixes": 0, "native_cad_execution": False, "associative_geometry_update": False, "output_sha256": outputs}, sort_keys=True))


if __name__ == "__main__":
    main()
