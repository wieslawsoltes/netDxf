#!/usr/bin/env python3
"""Verify unordered scalar-edge imports through their canonical exports with ezdxf.
Primitive paths in this suite do not certify hatch area closure or rendering.
"""
import argparse
from pathlib import Path
import ezdxf

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    expected = {f'hatch-scalar-order-AutoCad{v}-{b}-{k}.dxf' for v in (2000, 2004, 2007, 2010, 2013, 2018)
                for b in ('False', 'True') for k in (1, 2, 3)}
    paths = sorted(args.artifacts.glob('hatch-scalar-order-*.dxf'))
    if {p.name for p in paths} != expected:
        raise ValueError('Expected 36 version/transport/edge-kind exports')
    for path in paths:
        doc = ezdxf.readfile(path)
        hatch = list(doc.modelspace().query('HATCH'))
        if len(hatch) != 1 or len(hatch[0].paths) != 1 or len(hatch[0].paths[0].edges) != 1:
            raise ValueError(f'{path}: path structure changed')
        h = hatch[0]; edge = h.paths[0].edges[0]; kind = int(path.stem.rsplit('-', 1)[1])
        if kind == 1:
            if tuple(edge.start) != (-0.125, 2.0000000000000004) or tuple(edge.end) != (10, 10):
                raise ValueError(f'{path}: line coordinates lost')
        else:
            if tuple(edge.center) != (1, 2) or edge.start_angle != 0 or edge.end_angle != 360 or not edge.ccw:
                raise ValueError(f'{path}: curve coordinates/angles/direction lost')
            if kind == 2 and edge.radius != 3:
                raise ValueError(f'{path}: arc radius lost')
            if kind == 3 and (tuple(edge.major_axis) != (3, 0) or edge.ratio != 0.5):
                raise ValueError(f'{path}: ellipse axes lost')
        if list(h.seeds) != [(2, 3)] or list(h.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
            raise ValueError(f'{path}: adjacent metadata lost')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 36 scalar-edge exports; zero audit errors/repairs (not area closure validation)')

if __name__ == '__main__':
    main()
