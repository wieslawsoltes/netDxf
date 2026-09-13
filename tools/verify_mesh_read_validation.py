#!/usr/bin/env python3
"""Independent validation: python tools/verify_mesh_read_validation.py <artifact-directory>.
Requires optional development-only ezdxf 1.4.4. Reads files; never rewrites drawings.
"""
import argparse
from pathlib import Path
import struct


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    import ezdxf
    paths = sorted(args.artifacts.glob('mesh-validated-*.dxf'))
    if len(paths) != 6:
        raise ValueError(f'Expected six version/transport fixtures, got {len(paths)}')
    for path in paths:
        doc = ezdxf.readfile(path)
        meshes = list(doc.modelspace().query('MESH'))
        if len(meshes) != 1:
            raise ValueError(f'{path.name}: unexpected mesh count')
        mesh = meshes[0]
        assert [tuple(face) for face in mesh.faces] == [(0, 1, 2)]
        assert [tuple(edge) for edge in mesh.edges] == [(0, 2)]
        assert list(mesh.creases) == [1.25]
        assert mesh.dxf.blend_crease == 1 and mesh.dxf.subdivision_levels == 3
        assert struct.pack('<d', mesh.vertices[0][0]) == struct.pack('<d', 1e-20)
        assert [(t.code, t.value) for t in mesh.get_xdata('MESH_READ')] == [(1000, 'following XData')]
        assert doc.modelspace().query('LINE').first.dxf.start == (7, 8, 9)
        audit = doc.audit()
        assert not audit.errors and not audit.fixes, (path.name, audit.errors, audit.fixes)
        print(f'PASS {path.name}: topology, values, metadata and audit')
    print(f'Independent reader: ezdxf {ezdxf.__version__}; no AutoCAD execution claimed.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
