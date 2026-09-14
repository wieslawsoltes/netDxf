#!/usr/bin/env python3
"""Independently inspect SPLINE closure/periodicity flags and adjacent data."""
import argparse
from pathlib import Path
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    files = sorted(args.artifacts.glob('spline-flags-*.dxf'))
    if len(files) != 60:
        raise ValueError(f'Expected 60 profile/transport/kind drawings, found {len(files)}')
    for path in files:
        kind = int(path.stem.rsplit('-', 1)[1])
        doc = ezdxf.readfile(path)
        splines = list(doc.modelspace().query('SPLINE'))
        if len(splines) != 2:
            raise ValueError(f'{path}: original and clone required')
        expected = 4 | 32 | (1 if kind in (1, 4) else 0) | (2051 if kind == 2 else 0) | (1024 if kind >= 3 else 0)
        for spline in splines:
            if spline.dxf.flags != expected or bool(spline.closed) != (kind in (1, 2, 4)):
                raise ValueError(f'{path}: wrong closure or complete flag mask {spline.dxf.flags}')
            if bool(spline.dxf.flags & 2) != (kind == 2) or not spline.dxf.flags & 4:
                raise ValueError(f'{path}: wrong periodic/rational flags')
            if bool(len(spline.fit_points)) != (kind >= 3):
                raise ValueError(f'{path}: fit data changed')
            if tuple(spline.dxf.start_tangent) != (2, 3, 4) or tuple(spline.dxf.end_tangent) != (5, -6, 7):
                raise ValueError(f'{path}: tangent changed')
            if list(spline.get_xdata('SPLINE_FLAGS'))[0].value != kind:
                raise ValueError(f'{path}: XData changed')
            # Construct using the independent engine to validate control/weight/knot counts.
            curve = spline.construction_tool()
            if len(curve.control_points) != len(spline.control_points):
                raise ValueError(f'{path}: invalid construction geometry')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: following entity changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} audit errors / {len(audit.fixes)} repairs')
        print('PASS', path.name)
    print(f'PASS ezdxf {ezdxf.__version__}: 60 files / 120 splines; zero audit errors or repairs')


if __name__ == '__main__':
    main()
