#!/usr/bin/env python3
"""Independent POLYGONMESH grid semantics and native packet provenance gate."""
from pathlib import Path
import argparse
import gzip
import hashlib
import io
import json
import tempfile
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "tests/fixtures/polygonmesh-cardinality"
VERSIONS = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021", 2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}
SIGNATURE = b"AutoCAD Binary DXF\r\n\x1a\x00"

def check(condition, message):
    if not condition:
        raise ValueError(message)

def records(data):
    if data.startswith(SIGNATURE):
        loader = binary_tags_loader(data)
    else:
        try:
            text = data.decode("utf-8-sig")
        except UnicodeDecodeError:
            text = data.decode("cp1252")
        loader = ascii_tags_loader(io.StringIO(text, newline=None))
    result, packet = [], []
    for tag in tag_compiler(loader):
        if tag.code == 0 and packet:
            result.append(packet)
            packet = []
        value = tuple(tag.value) if tag.code in range(10, 19) or tag.code == 210 else tag.value
        packet.append((tag.code, value))
    if packet:
        result.append(packet)
    return result

def first(tags, code, default=None):
    return next((v for c, v in tags if c == code), default)

def mesh_sequence(data):
    wire = records(data)
    starts = [i for i, p in enumerate(wire) if p[0] == (0, "POLYLINE") and first(p, 70, 0) & 16 and not first(p, 70, 0) & 64]
    check(len(starts) == 1, "Expected exactly one polygon grid")
    start = starts[0]
    end = start + 1
    while end < len(wire) and wire[end][0] == (0, "VERTEX"):
        end += 1
    check(end < len(wire) and wire[end][0] == (0, "SEQEND"), "Grid sequence has no SEQEND")
    return wire[start:end + 1]

def inspect(path, expected, shape, smooth, binary=None, version=None):
    data = path.read_bytes()
    if binary is not None:
        check(data.startswith(SIGNATURE) == binary, "Wrong output transport")
    sequence = mesh_sequence(data)
    parent, children = sequence[0], sequence[1:-1]
    check((first(parent, 71), first(parent, 72)) == tuple(shape), "Changed grid dimensions")
    check(first(parent, 75, 0) == smooth, "Changed surface type")
    check(bool(first(parent, 70) & 4) == bool(smooth), "Inconsistent surface flags")
    gridflags = 80 if smooth else 64
    controls = [v for v in children if first(v, 70, 0) == gridflags]
    samples = [v for v in children if first(v, 70, 0) == 72]
    check(len(controls) == shape[0] * shape[1], "Wrong selected grid coordinate count")
    check(len(controls) + len(samples) == len(children), "Unexpected child vertex type")
    check([first(v, 10) for v in controls] == [tuple(p) for p in expected], "Changed grid coordinates or physical order")
    if smooth:
        check(len(samples) == first(parent, 73) * first(parent, 74), "Writer generated sample count differs from surface density")
    else:
        check(not samples, "Ordinary grid has generated spline samples")
    doc = ezdxf.readfile(path)
    if version is not None:
        check(doc.dxfversion == version, "Changed declared output version")
    audit = doc.audit()
    check(not audit.errors and not audit.fixes, "Independent audit reported errors or repairs")
    return {"file": path.name, "sha256": hashlib.sha256(data).hexdigest(), "grid_vertices": len(controls), "generated_vertices": len(samples), "audit_errors": 0, "audit_fixes": 0}

def provenance():
    receipts = []
    for row in json.loads((FIXTURES / "native-manifest.json").read_text()):
        source = ROOT / row["source"]
        data = source.read_bytes()
        check(hashlib.sha256(data).hexdigest() == row["compressedSourceSha256"], "Native compressed original hash changed")
        unpacked = gzip.decompress(data)
        check(hashlib.sha256(unpacked).hexdigest() == row["sourceSha256"], "Native uncompressed original hash changed")
        original = mesh_sequence(unpacked)
        carrier = (FIXTURES / row["carrier"]).read_bytes()
        check(hashlib.sha256(carrier).hexdigest() == row["carrierSha256"], "Native carrier hash changed")
        # The sole carrier edit rebinds the parent to the generated BLOCK_RECORD.
        original[0] = [(c, row["carrierOwner"] if c == 330 and v == row["nativeOwner"] else v) for c, v in original[0]]
        check(original == mesh_sequence(carrier), "Native entity packets changed beyond the declared owner mapping")
        check([first(v, 10) for v in original[1:-1]] == [tuple(p) for p in row["points"]], "Native expected coordinates differ from physical source")
        receipts.append({"carrier": row["carrier"], "source": row["source"], "physical_packets": len(original), "allowed_parent_owner_mapping": row["nativeOwner"] + "->" + row["carrierOwner"]})
    manifest = json.loads((FIXTURES / "manifest.json").read_text())
    check(manifest["producer_version"] == "1.4.4" and not manifest["native_cad"], "Producer provenance changed")
    for row in manifest["fixtures"]:
        check(hashlib.sha256((FIXTURES / row["file"]).read_bytes()).hexdigest() == row["sha256"], "Producer fixture hash changed")
    return receipts

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", nargs="?", type=Path, default=Path("artifacts/conformance"))
    args = parser.parse_args()
    check(ezdxf.__version__ == "1.4.4", "Independent oracle version must be pinned")
    native = provenance()
    results = []
    for year, version in VERSIONS.items():
        for binary in [False, True]:
            for smooth, mode in [(0, "plain"), (5, "quadratic"), (6, "cubic")]:
                points = [(i // 4 + 1e-20, i % 4, i * .125) for i in range(16)]
                for cycle in [0, 1]:
                    path = args.directory / f"polygonmesh-grid-AutoCad{year}-{binary}-{smooth}-{cycle}.dxf"
                    check(path.exists(), "Missing required output: " + str(path))
                    results.append(inspect(path, points, (4, 4), smooth, binary if cycle == 0 else not binary, version))
                path = args.directory / f"polygonmesh-producer-{version}-{'binary' if binary else 'ascii'}-{mode}.dxf"
                check(path.exists(), "Missing producer output: " + str(path))
                points = [(i // 4, i % 4, (i // 4) * (i % 4) * .125) for i in range(16)]
                results.append(inspect(path, points, (4, 4), smooth, not binary, version))
    for row in json.loads((FIXTURES / "native-manifest.json").read_text()):
        for binary in [False, True]:
            for cycle in [0, 1]:
                path = args.directory / f"polygonmesh-native-{row['carrier']}-{binary}-{cycle}.dxf"
                check(path.exists(), "Missing native output: " + str(path))
                results.append(inspect(path, row["points"], (3, 4), 0, binary if cycle == 0 else not binary, "AC1015" if row["carrier"].startswith("R2000") else "AC1032"))
    controls = []
    with tempfile.TemporaryDirectory() as temp:
        expected = [(i // 4, i % 4, (i // 4) * (i % 4) * .125) for i in range(16)]
        for binary in [False, True]:
            for fault in ["missing-grid", "surplus-grid", "wrong-dimension", "wrong-coordinate", "wrong-vertex-type", "missing-control"]:
                smooth = 5 if fault == "missing-control" else 0
                source = FIXTURES / f"AC1032-ascii-{'quadratic' if smooth else 'plain'}.dxf"
                doc = ezdxf.readfile(source)
                mesh = doc.modelspace().query("POLYLINE").first
                if fault in ("missing-grid", "missing-control"):
                    mesh.vertices.pop(0)
                elif fault == "surplus-grid":
                    mesh.append_vertex((99, 99, 99), {"flags": 64})
                elif fault == "wrong-dimension":
                    mesh.dxf.m_count = 5
                elif fault == "wrong-coordinate":
                    mesh.vertices[0].dxf.location = (99, 99, 99)
                else:
                    mesh.vertices[0].dxf.flags = 192
                path = Path(temp) / (fault + ".dxf")
                doc.saveas(path, fmt="bin" if binary else "asc")
                try:
                    inspect(path, expected, (4, 4), smooth, binary)
                except ValueError as error:
                    controls.append({"fault": fault, "binary": binary, "rejected": str(error)})
                else:
                    raise ValueError("Corruption escaped the independent gate: " + fault)
    receipt = {"oracle": "ezdxf 1.4.4", "outputs": len(results), "corruption_controls": controls, "native_provenance": native, "files": results}
    (args.directory / "independent-polygonmesh-cardinality.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(f"PolygonMesh: {len(results)} required outputs, {len(controls)} physical corruption controls, 2 native packet chains; all passed")

if __name__ == "__main__":
    main()
