#!/usr/bin/env python3
"""Verify knot-aware reversal with ezdxf's independent rational evaluator.

Usage: python tools/verify_spline_reversal.py artifacts/conformance
No dependency is loaded by the production library.
"""
import argparse
import math
from pathlib import Path
import ezdxf
from ezdxf.math import BSpline


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    paths = sorted(args.artifacts.glob('spline-reverse-*.dxf'))
    expected = {f'spline-reverse-AutoCad{v}-{b}-{f}.dxf' for v in (2000, 2004, 2007, 2010, 2013, 2018)
                for b in ('False', 'True') for f in range(3)}
    if {p.name for p in paths} != expected:
        raise ValueError('Expected all 36 version/transport/open-closed-periodic fixtures')
    evaluations = 0
    for path in paths:
        doc = ezdxf.readfile(path)
        entities = list(doc.modelspace().query('SPLINE'))
        if len(entities) != 2:
            raise ValueError(f'{path}: original/reversed pair required')
        curves = [BSpline(s.control_points, order=s.dxf.degree+1, knots=s.knots, weights=s.weights) for s in entities]
        # BSpline normalizes nonzero-origin knot domains. Compare fractions of
        # each evaluator's actual active interval, not the original wire units.
        spans = [(s.knots()[s.degree], s.knots()[s.count]) for s in curves]
        for i in range(33):
            t = i / 32
            before = curves[0].point(spans[0][0] + (spans[0][1]-spans[0][0]) * (1-t))
            after = curves[1].point(spans[1][0] + (spans[1][1]-spans[1][0]) * t)
            if math.dist(before, after) > 1e-10:
                raise ValueError(f'{path}: reversed locus differs at fraction {t}')
            evaluations += 1
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} audit errors / {len(audit.fixes)} repairs')
        print('PASS', path.name)
    print(f'PASS ezdxf {ezdxf.__version__}: {len(paths)} files / 72 splines / {evaluations} reversed-locus comparisons; zero audit errors/repairs')


if __name__ == '__main__':
    main()
