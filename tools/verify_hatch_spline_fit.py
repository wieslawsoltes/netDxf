#!/usr/bin/env python3
"""Independently verify HATCH spline fit/tangent exports (development-only ezdxf).

Run: python tools/verify_hatch_spline_fit.py artifacts/conformance
This inspects retained metadata and audit results, not spline fitting or rendering.
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
    files = sorted(args.artifacts.glob('hatch-spline-fit-*.dxf'))
    expected_names = {f'hatch-spline-fit-AutoCad{v}-{b}.dxf' for v in (2010, 2013, 2018) for b in ('False', 'True')}
    if {p.name for p in files} != expected_names:
        raise ValueError('Expected exactly six modern profile/transport fit fixtures')
    for path in files:
        doc = ezdxf.readfile(path)
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path}: original and clone required')
        for hatch in hatches:
            if len(hatch.paths) != 1 or len(hatch.paths[0].edges) != 2:
                raise ValueError(f'{path}: boundary changed')
            edge, line = hatch.paths[0].edges
            expected = ((0.0, 0.0), (5.000000000000001, 5.0), (10.0, 0.0))
            if len(edge.fit_points) != 3 or any(bits(a) != bits(b) for p, q in zip(edge.fit_points, expected) for a, b in zip(p, q)):
                raise ValueError(f'{path}: fit coordinates/order/precision lost')
            if tuple(edge.start_tangent) != (10, 20) or tuple(edge.end_tangent) != (10, -20):
                raise ValueError(f'{path}: tangent magnitude or direction lost')
            if edge.degree != 2 or edge.rational != 0 or edge.periodic != 0:
                raise ValueError(f'{path}: spline flags changed')
            if list(edge.knot_values) != [0, 0, 0, 1, 1, 1] or [tuple(p) for p in edge.control_points] != [(0, 0), (5, 10), (10, 0)]:
                raise ValueError(f'{path}: fit metadata regenerated control geometry')
            if tuple(line.start) != (10, 0) or tuple(line.end) != (0, 0):
                raise ValueError(f'{path}: following LINE edge changed')
            if hatch.dxf.elevation.z != 2.5 or list(hatch.seeds) != [(2, 3)] or list(hatch.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
                raise ValueError(f'{path}: adjacent HATCH metadata changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} audit errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 6 files / 12 spline fit packets; zero audit errors/repairs')


if __name__ == '__main__':
    main()
