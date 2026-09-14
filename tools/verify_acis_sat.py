#!/usr/bin/env python3
"""Independent exact-envelope and known-producer geometry check for all 48 SAT exports."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import ezdxf
from ezdxf.acis import api
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.tools import crypt


def check(condition, message):
    if not condition:
        raise AssertionError(message)


def packet(path):
    data = path.read_bytes()
    tags = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None))
    result = []
    current = None
    active = False
    for code, value in tags:
        if code == 0:
            if current is not None:
                result.append(current)
            current = {"type": value, "chunks": [], "version": [], "history": []} if value in ("BODY", "REGION", "3DSOLID") else None
            active = False
        if current is None:
            continue
        if code == 100:
            active = value == "AcDbModelerGeometry"
        elif code >= 1000:
            active = False
        elif active and code in (1, 3):
            current["chunks"].append([code, value])
        elif active and code == 70:
            current["version"].append(int(value))
        elif code == 350:
            current["history"].append(value)
    check(len(result) == 1, f"{path.name}: expected one ACIS entity")
    return result[0]


def decoded(chunks):
    lines = []
    for code, text in chunks:
        if code == 1:
            lines.append(text)
        else:
            check(code == 3 and lines, "Invalid SAT continuation")
            lines[-1] += text
    return list(crypt.decode(lines))


def geometry(entity):
    bodies = api.load_dxf(entity)
    meshes = [mesh for body in bodies for mesh in api.mesh_from_body(body)]
    return (len(bodies), [[list(v) for v in mesh.vertices] for mesh in meshes],
            [[list(f) for f in mesh.faces] for mesh in meshes])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path, nargs="?", default=Path("artifacts/conformance"))
    args = parser.parse_args()
    sources = Path(__file__).resolve().parent.parent / "tests" / "fixtures" / "acis-sat"
    manifest = json.loads((sources / "manifest.json").read_text())
    check(len(manifest["sources"]) == 24, "Expected all 24 producer sources")
    expected_sources = {f"independent-acis-{kind}-R{year}-{transport}.dxf" for kind in ("body", "region", "3dsolid") for year in (2000, 2004, 2007, 2010) for transport in ("ascii", "binary")}
    check({fixture["filename"] for fixture in manifest["sources"]} == expected_sources, "Producer profile/type/transport matrix incomplete")
    profiles = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021", 2010: "AC1024"}
    expected_paths = set()
    for fixture in manifest["sources"]:
        source = sources / fixture["filename"]
        check(hashlib.sha256(source.read_bytes()).hexdigest() == fixture["sha256"], "Producer provenance hash changed")
        check(source.read_bytes().startswith(b"AutoCAD Binary DXF") == fixture["binary"], "Source transport changed")
        original = packet(source)
        check(original["chunks"] == fixture["chunks"], "Producer chunk manifest changed")
        check(decoded(original["chunks"]) == fixture["lines"], "Producer SAT line manifest changed")
        year = int(source.stem.split("-R")[1].split("-")[0])
        check(fixture["version"] == profiles[year], "Manifest profile differs from filename")
        before = ezdxf.readfile(source)
        check(before.dxfversion == profiles[year], "Source profile differs from filename")
        check(len(before.modelspace()) == 2, "Producer entity inventory changed")
        old = before.modelspace().query(fixture["entity"])[0]
        old_geometry = geometry(old)
        check(old_geometry[0] == fixture["bodies"], "Producer body count changed")
        check([len(m) for m in old_geometry[1]] == fixture["vertices"], "Producer vertex counts changed")
        check([len(m) for m in old_geometry[2]] == fixture["faces"], "Producer face counts changed")
        check(any(code == 3 for code, _ in original["chunks"]), "Missing continuation probe")
        check(any(r"\U+0041" in text for _, text in original["chunks"]), "Missing Unicode-like literal probe")
        for binary in (False, True):
            target = args.artifacts / (source.stem + ("-roundtrip-binary.dxf" if binary else "-roundtrip-ascii.dxf"))
            expected_paths.add(target.name)
            check(target.is_file(), "Missing " + target.name)
            check(target.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Wrong target transport")
            actual = packet(target)
            check(actual == original, f"{target.name}: exact modeler envelope/chunks/history changed")
            check(decoded(actual["chunks"]) == fixture["lines"], "Decoded SAT payload changed")
            after = ezdxf.readfile(target)
            check(after.dxfversion == profiles[year], "Wrong target profile")
            check(len(after.modelspace()) == 2, "Target entity inventory changed")
            audit = after.audit()
            check(not audit.errors and not audit.fixes, "Independent audit has errors or repairs")
            new = after.modelspace().query(fixture["entity"])[0]
            check(new.dxf.handle == old.dxf.handle, "ACIS handle changed")
            check(new.dxf.owner == old.dxf.owner == after.modelspace().block_record_handle == before.modelspace().block_record_handle, "ACIS owner identity changed")
            check(geometry(new) == old_geometry, "Known producer geometry changed")
            check(list(new.sat) == list(old.sat), "Independent SAT decode changed")
            check(new.get_xdata("QA_ACIS_SAT") == old.get_xdata("QA_ACIS_SAT"), "Following XData changed")
            check(new.dxf.color == old.dxf.color == 5, "Common color changed")
            for name in ("color_name", "shadow_mode"):
                check(new.dxf.hasattr(name) == old.dxf.hasattr(name) and new.dxf.get(name) == old.dxf.get(name), "Common optional metadata changed: " + name)
            old_line, new_line = before.modelspace().query("LINE")[0], after.modelspace().query("LINE")[0]
            check(new_line.dxf.handle == old_line.dxf.handle, "Following LINE handle changed")
            check(new_line.dxf.owner == old_line.dxf.owner == new.dxf.owner, "Following LINE owner identity changed")
            check(new_line.dxf.start == old_line.dxf.start and new_line.dxf.end == old_line.dxf.end, "Following LINE changed")
    actual_paths = {path.name for path in args.artifacts.glob("independent-acis-*-roundtrip-*.dxf")}
    check(actual_paths == expected_paths and len(expected_paths) == 48, "Missing or unexpected SAT exports")
    print("Verified all 48 SAT exports: exact encoded chunks, decoded lines, history presence, known cube/planar geometry, common metadata, XData and following LINE; zero independent audit errors or repairs.")


if __name__ == "__main__":
    main()
