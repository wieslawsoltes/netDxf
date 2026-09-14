#!/usr/bin/env python3
"""Evaluate authored HELIX splines with independent ezdxf (development only).

Usage: python tools/verify_helix_authoring.py artifacts/conformance
The spline evaluator uses the actual serialized knots; no knot reconstruction or
CAD application fitting is involved. Sampling checks supplement the analytic
error-bound tests, not a native AutoCAD appearance certificate.
"""
import argparse
import math
from pathlib import Path
import ezdxf
from ezdxf.math import BSpline


def definition(shape, t):
    initial = 0.0 if shape == 4 else 5.0
    final = 5.0 if shape == 0 else 0.0 if shape == 5 else 2.0
    pitch = 0.0 if shape == 2 else -1.5 if shape == 3 else 1.5
    omega = (-1.0 if shape == 1 else 1.0) * 2.0 * math.pi * 2.25
    radius = initial + (final - initial) * t
    c, s = math.cos(omega * t), math.sin(omega * t)
    return (radius * c, radius * s, 2.25 * pitch * t)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    files = sorted(args.artifacts.glob('helix-authored-*.dxf'))
    expected = {f'helix-authored-AutoCad{v}-{b}-{s}.dxf' for v in (2007, 2010, 2013, 2018)
                for b in ('False', 'True') for s in range(6)}
    if {p.name for p in files} != expected:
        raise ValueError('Expected exactly 48 authored HELIX profile/transport/shape drawings')
    evaluations, largest_error = 0, 0.0
    for path in files:
        doc = ezdxf.readfile(path)
        helices = list(doc.modelspace().query('HELIX'))
        if len(helices) != 2:
            raise ValueError(f'{path}: original and clone required')
        shape = int(path.stem.rsplit('-', 1)[1])
        for h in helices:
            if h.dxf.degree != 3 or h.dxf.turns != 2.25 or tuple(h.dxf.axis_vector) != (0, 0, 3):
                raise ValueError(f'{path}: analytic parameter fields changed')
            if tuple(h.dxf.start_point) != definition(shape, 0):
                raise ValueError(f'{path}: start point changed')
            initial, terminal = definition(shape, 0), definition(shape, 1)
            if not math.isclose(h.dxf.radius, math.hypot(terminal[0], terminal[1]), rel_tol=1e-12, abs_tol=1e-12):
                raise ValueError(f'{path}: terminal radius changed')
            if bool(h.dxf.handedness) != (shape != 1):
                raise ValueError(f'{path}: handedness changed')
            if h.dxf.turn_height != (0 if shape == 2 else -1.5 if shape == 3 else 1.5):
                raise ValueError(f'{path}: signed/zero pitch changed')
            curve = BSpline(h.control_points, order=4, knots=h.knots, weights=h.weights)
            for sample in range(257):
                t = sample / 256.0
                error = math.dist(tuple(curve.point(t)), definition(shape, t))
                if not math.isfinite(error) or error > 1e-5 + 1e-10:
                    raise ValueError(f'{path}: sampled deviation {error} exceeds authored tolerance at {t}')
                largest_error = max(largest_error, error)
                evaluations += 1
        if next(c for c in doc.classes if c.dxf.name == 'HELIX').dxf.instance_count != 2:
            raise ValueError(f'{path}: HELIX class count changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 48 files / 96 HELIX curves / {evaluations} evaluations; '
          f'max sampled error {largest_error:.17g}; zero audit errors/repairs')


if __name__ == '__main__':
    main()
