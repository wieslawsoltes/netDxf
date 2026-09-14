#!/usr/bin/env python3
"""Validate transformed SPLINE exports with independent ezdxf; development only."""
import argparse
from pathlib import Path
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    paths = sorted(parser.parse_args().artifacts.glob('spline-tangent-transform-*.dxf'))
    if len(paths) != 12:
        raise ValueError(f'Expected 12 version/transport fixtures, found {len(paths)}')
    for path in paths:
        doc = ezdxf.readfile(path)
        splines = list(doc.modelspace().query('SPLINE'))
        if len(splines) != 2:
            raise ValueError(f'{path}: expected original and clone')
        for spline in splines:
            if tuple(spline.dxf.start_tangent) != (4, -9, -20) or tuple(spline.dxf.end_tangent) != (-14, 33, -52):
                raise ValueError(f'{path}: tangent missing linear transform or incorrectly translated/normalized')
            if list(map(tuple, spline.control_points)) != [(103, -197, 295), (109, -185, 271), (117, -188, 299)]:
                raise ValueError(f'{path}: control points changed')
            if list(spline.weights) != [1, 0.75, 1] or list(spline.knots) != [0, 0, 0, 1, 1, 1]:
                raise ValueError(f'{path}: parameterization changed')
        if tuple(doc.modelspace().query('LINE').first.dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: following entity changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} fixes')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 12 files / 24 splines; zero audit errors/repairs')


if __name__ == '__main__':
    main()
