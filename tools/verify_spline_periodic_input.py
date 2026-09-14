#!/usr/bin/env python3
"""Check periodic SPLINE exports using independent ezdxf (development only)."""
import argparse
import struct
from pathlib import Path
import ezdxf


def exact(actual, expected, context):
    if len(actual) != len(expected) or any(struct.pack('<d', a) != struct.pack('<d', b) for a, b in zip(actual, expected)):
        raise ValueError(context)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    expected_names = {f'spline-periodic-input-AutoCad{v}-{b}-{d}.dxf' for v in (2000, 2004, 2007, 2010, 2013, 2018) for b in ('False', 'True') for d in (1, 2, 3)}
    paths = sorted(args.artifacts.glob('spline-periodic-input-*.dxf'))
    if {p.name for p in paths} != expected_names:
        raise ValueError('Expected exactly 36 periodic profile/transport/degree outputs')
    for path in paths:
        doc = ezdxf.readfile(path)
        splines = list(doc.modelspace().query('SPLINE'))
        if len(splines) != 2:
            raise ValueError(f'{path}: original and clone required')
        degree = int(path.stem.rsplit('-', 1)[1])
        indices = [(i + 5 - degree) % 5 for i in range(5 + degree)]
        for spline in splines:
            if spline.dxf.degree != degree or spline.dxf.flags & 7 != 7:
                raise ValueError(f'{path}: periodic/closed/rational flags lost')
            expected = [(j*j, j%2*4-j, j) for j in indices]
            exact([c for p in spline.control_points for c in p], [float(c) for p in expected for c in p], f'{path}: cyclic control layout changed')
            exact(list(spline.weights), [1.0+j*0.25 for j in indices], f'{path}: cyclic weights changed')
            exact(list(spline.knots), [-3.25+i*0.125 for i in range(5+2*degree+1)], f'{path}: knots changed')
            if list(spline.get_xdata('PERIODIC_INPUT'))[0].value != 'standard periodic bit':
                raise ValueError(f'{path}: XData lost')
        line = list(doc.modelspace().query('LINE'))
        if len(line) != 1 or tuple(line[0].dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: following LINE changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} audit errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 36 files / 72 periodic splines; zero audit errors/repairs')


if __name__ == '__main__':
    main()
