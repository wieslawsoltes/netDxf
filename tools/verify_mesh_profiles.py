#!/usr/bin/env python3
"""Independently verify the 48 supported MESH output fixtures with ezdxf 1.4.4.

Usage: python tools/verify_mesh_profiles.py <conformance-artifact-directory>
The optional ezdxf dependency is development-only. No file is modified.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import struct


def bits(value: float) -> bytes:
    return struct.pack("<d", value)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    import ezdxf
    count = 0
    for family, identifier in (("AutoCad2010", "AC1024"), ("AutoCad2013", "AC1027"), ("AutoCad2018", "AC1032")):
        for binary in ("False", "True"):
            for placement in range(4):
                for level in (0, 2):
                    path = args.artifacts / f"mesh-profile-{family}-{binary}-{placement}-{level}.dxf"
                    document = ezdxf.readfile(path)
                    meshes = list(document.entitydb.query("MESH"))
                    if document.dxfversion != identifier or len(meshes) != 1:
                        raise ValueError(f"{path}: incorrect version or MESH count")
                    mesh = meshes[0]
                    expected = [(1e-20, 0.0, 0.0), (2.0, 0.0, 0.0), (0.0, 3.0, 1.0)]
                    actual = [tuple(float(v) for v in vertex) for vertex in mesh.vertices]
                    if len(actual) != len(expected) or any(bits(a) != bits(b) for av, bv in zip(actual, expected) for a, b in zip(av, bv)):
                        raise ValueError(f"{path}: coordinate bits changed")
                    if [list(face) for face in mesh.faces] != [[0, 1, 2]] or list(mesh.edges) != [(0, 1), (1, 2), (2, 0)]:
                        raise ValueError(f"{path}: topology changed")
                    if list(mesh.creases) != [0.0, 1.5, -1.0] or mesh.dxf.subdivision_levels != level:
                        raise ValueError(f"{path}: subdivision metadata changed")
                    if [(t.code, t.value) for t in mesh.get_xdata("MESH_PROFILE_TEST")] != [(1000, "mesh metadata")]:
                        raise ValueError(f"{path}: XData changed")
                    auditor = document.audit()
                    if auditor.errors or auditor.fixes:
                        raise ValueError(f"{path}: {len(auditor.errors)} errors, {len(auditor.fixes)} repairs")
                    count += 1
    print(f"PASS: ezdxf {ezdxf.__version__} verified {count} MESH drawings; zero errors or repairs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
