#!/usr/bin/env python3
"""Reproduce native-packet carriers and independent ezdxf HATCH source fixtures."""
from pathlib import Path
import gzip
import inspect
import io
import json
import os
import sys

# ezdxf registers some required CLASS definitions by iterating a Python set.
# Fix the interpreter seed, as well as ezdxf metadata, for reproducible bytes.
if os.environ.get("PYTHONHASHSEED") != "0":
    environment = dict(os.environ, PYTHONHASHSEED="0")
    os.execve(sys.executable, [sys.executable, *sys.argv], environment)

import ezdxf
from ezdxf.entities import Hatch
from ezdxf.entities.boundary_paths import EdgePath
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from ezdxf.lldxf.tagwriter import BinaryTagWriter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(ROOT / "tools"))
from verify_hatch_source_relations import VERSIONS, check, inventory, sha256, validate_document


def pairs(data):
    lines = data.decode("latin1").splitlines(keepends=True)
    check(len(lines) % 2 == 0, "Native text pairs are incomplete")
    return [(int(lines[i]), lines[i + 1].strip(), lines[i] + lines[i + 1]) for i in range(0, len(lines), 2)]


def native_packets(data):
    packets = []
    for code, value, raw in pairs(data):
        if code == 0:
            packets.append([])
        if packets:
            packets[-1].append((code, value, raw))
    result = {}
    for packet in packets:
        handles = [value for code, value, _ in packet if code == 5]
        if handles and handles[0] in ("8E", "24F"):
            result[handles[0]] = packet
    check(set(result) == {"8E", "24F"}, "Expected native source/HATCH packet pair")
    return result


def adapt_owner(packet, new_owner):
    depth = 0
    replacements = 0
    result = []
    for code, value, raw in packet:
        if code == 102:
            depth += 1 if value.startswith("{") else -1
        if code == 330 and depth == 0 and replacements == 0:
            check(value == "1F", "Native owner differs")
            raw = raw.replace(value, new_owner)
            replacements += 1
        result.append(raw)
    check(replacements == 1, "Native packet must have one owner adaptation")
    return "".join(result)



def add_wrong_owner_candidate(doc):
    doc.entitydb.handles.reset("400")
    block = doc.blocks.new("OTHER_SOURCE_BLOCK")
    line = block.add_line((20, 30, 0), (40, 50, 0))
    check(line.dxf.handle == "403", "Wrong-owner source handle changed")
    return line.dxf.handle

def native_carrier(year, data):
    doc = ezdxf.new("R" + str(year))
    doc.layers.new("Tavolo 3")
    doc.entitydb.handles.reset("300")
    following = doc.modelspace().add_line((1, 2, 3), (4, 5, 6))
    wrong_owner = add_wrong_owner_candidate(doc)
    stream = io.StringIO()
    doc.write(stream)
    text = stream.getvalue()
    packet_map = native_packets(data)
    common_owner = doc.modelspace().block_record_handle
    packet_text = "".join(adapt_owner(packet_map[handle], common_owner) for handle in ("8E", "24F"))
    # The carrier contains only one LINE, so insertion preserves the exact native
    # packets and puts the independently generated sentinel after both.
    needle = "  0\nLINE\n  5\n300\n"
    check(text.count(needle) == 1, "Carrier following LINE is ambiguous")
    result = text.replace(needle, packet_text + needle).encode("ascii")
    receipt = {"original_owner": "1F", "carrier_owner": common_owner, "source_handle": "8E", "hatch_handle": "24F", "following_line_handle": following.dxf.handle, "wrong_owner_source_handle": wrong_owner, "adaptation": "Only the entity-level owner group 330 changes from 1F to the carrier modelspace BLOCK_RECORD. Source references, reactors, original layer name, and all native entity body text pairs remain unchanged.", "packets": []}
    for handle in ("8E", "24F"):
        original = "".join(raw for _, _, raw in packet_map[handle]).encode("latin1")
        adapted = adapt_owner(packet_map[handle], common_owner).encode("ascii")
        receipt["packets"].append({"handle": handle, "original_packet_sha256": sha256(original), "adapted_packet_sha256": sha256(adapted), "original_bytes": len(original), "adapted_bytes": len(adapted)})
    return result, receipt


def producer_doc(year):
    doc = ezdxf.new("R" + str(year))
    doc.entitydb.handles.reset("3A0")
    model = doc.modelspace()
    outer = [(0, 0), (10, 0), (10, 10), (0, 10)]
    inner = [(2, 2, .25), (8, 2, 0), (8, 8, -.125), (2, 8, 0)]
    a = model.add_lwpolyline(outer, close=True)
    b = model.add_lwpolyline(inner, format="xyb", close=True)
    for first_sources, second_sources in (([a, a, b], [b, a]), ([b, a, b], [a, a])):
        hatch = model.add_hatch(color=2)
        edge = hatch.paths.add_edge_path(flags=1)
        for start, end in zip(outer, outer[1:] + outer[:1]):
            edge.add_line(start, end)
        polyline = hatch.paths.add_polyline_path(inner, is_closed=True, flags=16)
        hatch.associate(edge, first_sources)
        hatch.associate(polyline, second_sources)
    model.add_line((1, 2, 3), (4, 5, 6))
    add_wrong_owner_candidate(doc)
    return doc


def native_binary(data, year):
    output = io.BytesIO()
    writer = BinaryTagWriter(output, dxfversion=VERSIONS[year], encoding="utf8" if year >= 2007 else "cp1252")
    writer.write_signature()
    for tag in tag_compiler(ascii_tags_loader(io.StringIO(data.decode("ascii"), newline=None))):
        writer.write_tag(tag)
    return output.getvalue()


def main():
    check(ezdxf.__version__ == "1.4.4", "Fixture producer requires pinned ezdxf 1.4.4")
    ezdxf.options.write_fixed_meta_data_for_testing = True
    source_manifest = json.loads((ROOT / "tests/fixtures/dimassoc/source-manifest.json").read_text())
    manifest = {"schema": 1, "generator_sha256": sha256(Path(__file__).read_bytes()), "verifier_sha256": sha256((ROOT / "tools/verify_hatch_source_relations.py").read_bytes()), "producer": {"name": "ezdxf", "version": ezdxf.__version__, "boundary_module_sha256": sha256(Path(inspect.getfile(EdgePath)).read_bytes()), "hatch_module_sha256": sha256(Path(inspect.getfile(Hatch)).read_bytes()), "metadata": "ezdxf fixed metadata option enabled; Python hash seed 0 fixes CLASS registration order"}, "native_repository": {"repository": source_manifest["repository"], "commit": source_manifest["commit"]}, "scope": "Native entity packets transplanted into independent minimal carrier documents and actual ezdxf-produced HATCH associations. Stored topology and geometry only; no native CAD execution, graphical rendering, curve evaluation, or associative update qualification.", "native_originals": [], "fixtures": []}
    for year in VERSIONS:
        original = next(item for item in source_manifest["sources"] if item["year"] == year)
        relative = "tests/fixtures/dimassoc/originals-gzip/" + original["gzip_file"]
        compressed = (ROOT / relative).read_bytes()
        raw = gzip.decompress(compressed)
        check(sha256(compressed) == original["gzip_sha256"] and sha256(raw) == original["source_sha256"], "Pinned original hash differs")
        native, receipt = native_carrier(year, raw)
        manifest["native_originals"].append({"year": year, "gzip_path": relative, "gzip_sha256": original["gzip_sha256"], "source_sha256": original["source_sha256"], "git_blob_sha": original["git_blob_sha"], "repository_path": original["repository_path"], "packet_transplant": receipt})
        for kind in ("native", "producer"):
            for binary in (False, True):
                if kind == "native":
                    data = native_binary(native, year) if binary else native
                else:
                    doc = producer_doc(year)
                    stream = io.BytesIO() if binary else io.StringIO()
                    doc.write(stream, fmt="bin" if binary else "asc")
                    data = stream.getvalue()
                    if not binary:
                        data = data.encode("ascii")
                filename = f'{kind}-R{year}-{"binary" if binary else "ascii"}.dxf'
                path = HERE / filename
                path.write_bytes(data)
                expected = inventory(data)
                validate_document(path, year, binary, expected)
                manifest["fixtures"].append({"file": filename, "kind": kind, "year": year, "binary": binary, "sha256": sha256(data), "bytes": len(data), "audit_errors": 0, "audit_fixes": 0, "inventory": expected, "handles": {"wrong_owner_source": "403", "same_owner_source": "300" if kind == "native" else "3A4"}})
    (HERE / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print("Generated and independently loaded 24 fixtures: six native packet pairs and 12 actual producer drawings; zero audit errors/repairs.")


if __name__ == "__main__":
    main()
