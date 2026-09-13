#!/usr/bin/env python3
"""Verify MESH Blend Crease exports with the optional ezdxf development dependency.

Usage: python tools/verify_mesh_blend.py <conformance-artifact-directory>
"""
from __future__ import annotations
import argparse
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    import ezdxf
    count = 0
    for version in ("AutoCad2010", "AutoCad2013", "AutoCad2018"):
        for binary in ("False", "True"):
            path = args.artifacts / f"mesh-blend-{version}-{binary}.dxf"
            document = ezdxf.readfile(path)
            meshes = list(document.entitydb.query("MESH"))
            if len(meshes) != 2:
                raise ValueError(f"{path}: original/clone count changed")
            for mesh in meshes:
                if mesh.dxf.blend_crease != 1 or mesh.dxf.subdivision_levels != 2:
                    raise ValueError(f"{path}: blend/subdivision data changed")
                if list(mesh.creases) != [1.5] or [list(f) for f in mesh.faces] != [[0, 1, 2]]:
                    raise ValueError(f"{path}: crease or face data changed")
                if [(t.code, t.value) for t in mesh.get_xdata("MESH_BLEND")] != [(1000, "after mesh")]:
                    raise ValueError(f"{path}: XData changed")
            auditor = document.audit()
            if auditor.errors or auditor.fixes:
                raise ValueError(f"{path}: audit required errors/repairs")
            count += 1
    print(f"PASS: ezdxf {ezdxf.__version__}, {count} files, original/clone blend flags preserved, no errors/repairs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
