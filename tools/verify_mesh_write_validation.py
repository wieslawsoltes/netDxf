#!/usr/bin/env python3
"""Check the MESH writer-preflight valid corpus using independent ezdxf.

Run: python tools/verify_mesh_write_validation.py artifacts/conformance
This is lexical/topology preservation, not a manifoldness or subdivision evaluator.
"""
import argparse
import struct
from pathlib import Path
import ezdxf


def bits(value):
    return struct.pack('<d', value)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    paths = sorted(args.artifacts.glob('mesh-write-validation-*.dxf'))
    names = {f'mesh-write-validation-AutoCad{v}-{b}.dxf' for v in (2010, 2013, 2018) for b in ('False', 'True')}
    if {p.name for p in paths} != names:
        raise ValueError('Expected exactly six supported profile/transport MESH fixtures')
    expected_vertices = ((1e-20, 0, 0), (2, 0, 0), (0, 3, 1), (5, 7, 9), (10, 11, 12))
    for path in paths:
        doc = ezdxf.readfile(path)
        meshes = list(doc.modelspace().query('MESH'))
        if len(meshes) != 1:
            raise ValueError(f'{path}: expected exactly one MESH')
        mesh = meshes[0]
        if len(mesh.vertices) != len(expected_vertices) or any(bits(a) != bits(b) for p, q in zip(mesh.vertices, expected_vertices) for a, b in zip(p, q)):
            raise ValueError(f'{path}: vertex values/order/precision changed')
        if [tuple(face) for face in mesh.faces] != [(0, 1, 2), (0, 1, 3, 2)]:
            raise ValueError(f'{path}: face topology changed')
        if [tuple(edge) for edge in mesh.edges] != [(0, 1), (1, 2), (2, 0)] or list(mesh.creases) != [0, 1.5, -1]:
            raise ValueError(f'{path}: edge or crease data changed')
        if mesh.dxf.subdivision_levels != 255 or mesh.dxf.blend_crease != 1:
            raise ValueError(f'{path}: subdivision metadata changed')
        if list(mesh.get_xdata('MESH_PROFILE_TEST'))[0].value != 'mesh metadata':
            raise ValueError(f'{path}: following XData changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 6 meshes / 12 faces; zero audit errors/repairs')


if __name__ == '__main__':
    main()
