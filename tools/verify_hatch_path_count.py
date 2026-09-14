#!/usr/bin/env python3
"""Verify HATCH mixed boundary-count fixtures with development-only ezdxf."""
import argparse
from pathlib import Path
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    files = sorted(args.artifacts.glob('hatch-path-count-*.dxf'))
    names = {f'hatch-path-count-AutoCad{v}-{b}.dxf' for v in (2000, 2004, 2007, 2010, 2013, 2018) for b in ('False', 'True')}
    if {p.name for p in files} != names:
        raise ValueError('Expected twelve version/transport boundary files')
    for path in files:
        doc = ezdxf.readfile(path)
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 1 or len(hatches[0].paths) != 2:
            raise ValueError(f'{path}: HATCH or boundary count changed')
        hatch = hatches[0]
        outer, inner = hatch.paths
        if outer.path_type_flags != 3 or not outer.is_closed or [tuple(v) for v in outer.vertices] != [(0, 0, 0), (10, 0, 0), (10, 10, 0), (0, 10, 0)]:
            raise ValueError(f'{path}: outer polyline flags/vertices changed')
        if inner.path_type_flags != 0 or len(inner.edges) != 1:
            raise ValueError(f'{path}: inner edge path flags/count changed')
        arc = inner.edges[0]
        if tuple(arc.center) != (5, 5) or arc.radius != 1 or arc.start_angle != 0 or arc.end_angle != 360 or not arc.ccw:
            raise ValueError(f'{path}: inner arc changed')
        if list(hatch.seeds) != [(2, 3)] or hatch.dxf.elevation.z != 2.5 or list(hatch.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
            raise ValueError(f'{path}: adjacent HATCH data changed')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40) or tuple(lines[0].dxf.end) != (21, 31, 41):
            raise ValueError(f'{path}: following LINE consumed or changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 12 files / 24 paths; zero audit errors/repairs')


if __name__ == '__main__':
    main()
