#!/usr/bin/env python3
"""Verify SPLINE clone exports with independent ezdxf (development only).

Run: python tools/verify_spline_clone_state.py artifacts/conformance
Checks stored geometry and metadata; does not certify the fitting algorithm.
"""
import argparse
import io
import math
import struct
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader


def bits(values):
    return b''.join(struct.pack('<d', float(value)) for value in values)


def vectors(values):
    return [tuple(float(v) for v in point) for point in values]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    paths = sorted(args.artifacts.glob('spline-clone-state-*.dxf'))
    expected_names = {f'spline-clone-state-AutoCad{v}-{b}-{k}.dxf'
                      for v in (2000, 2004, 2007, 2010, 2013, 2018)
                      for b in ('False', 'True') for k in range(3)}
    if {p.name for p in paths} != expected_names:
        raise ValueError('Expected all 36 version/transport/creation-mode fixtures')
    controls = [(1, 2, 3), (4.125, 6.75, 8.5), (9, 5, 2), (12, 3, 7)]
    fit = [(1, 2, 3), (4, 7, 8), (9, 5, 2), (12, 3, 7)]
    for path in paths:
        kind = int(path.stem.rsplit('-', 1)[1])
        data = path.read_bytes()
        tags = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('ascii')))
        records, active = [], None
        for tag in tags:
            if tag.code == 0:
                if active is not None:
                    records.append(active)
                active = [] if tag.value == 'SPLINE' else None
            elif active is not None:
                active.append(tag)
        if len(records) != 2:
            raise ValueError(f'{path}: missing raw spline pair')
        for record in records:
            for code in (13, 23, 33):
                values = [float(t.value) for t in record if t.code == code]
                if values != [0.0]:
                    raise ValueError(f'{path}: explicit zero tangent not retained on wire')
        doc = ezdxf.readfile(path)
        splines = list(doc.modelspace().query('SPLINE'))
        if len(splines) != 2:
            raise ValueError(f'{path}: missing clone pair')
        for spline in splines:
            if spline.dxf.degree != (3 if kind == 0 else 2):
                raise ValueError(f'{path}: changed degree')
            for key, value in (('knot_tolerance', .00125), ('control_point_tolerance', .0025), ('fit_tolerance', .00375)):
                if bits([spline.dxf.get(key)]) != bits([value]):
                    raise ValueError(f'{path}: clone reset {key}')
            if spline.dxf.invisible != 1 or spline.dxf.layer != 'SplineCloneLayer':
                raise ValueError(f'{path}: common entity state lost')
            # ezdxf's semantic validator discards a zero tangent; wire presence was checked above.
            if tuple(spline.dxf.start_tangent) != (2, -3, 5) or spline.dxf.end_tangent not in (None, (0, 0, 0)):
                raise ValueError(f'{path}: tangent state lost')
            if list(spline.get_xdata('SPLINE_CLONE_STATE'))[0].value != 'retain geometry; do not refit':
                raise ValueError(f'{path}: XData lost')
            cp = vectors(spline.control_points)
            if kind == 0:
                if len(cp) != 12 or cp[0] != fit[0] or cp[-1] != fit[-1] or vectors(spline.fit_points) != fit:
                    raise ValueError(f'{path}: fit/control representations changed')
                # Independently calculated first Bezier control plus the authored edit.
                for actual, expected in zip(cp[1], (16 / 9 + .125, 193 / 45 - .25, 53 / 9 + .5)):
                    if not math.isclose(actual, expected, rel_tol=1e-14, abs_tol=1e-14):
                        raise ValueError(f'{path}: clone refitted edited control point')
                weights = [1., .875] + [1.] * 10
                knots = [2.] * 4 + [2.75] * 4 + [3.5] * 4 + [5.] * 4
            else:
                expected_cp = controls if kind == 1 else controls[-2:] + controls
                if cp != expected_cp or len(spline.fit_points):
                    raise ValueError(f'{path}: control geometry changed')
                weights = [1., .875, .75, 1.] if kind == 1 else [.75, 1., 1., .875, .75, 1.]
                knots = [2., 2., 2., 5., 8., 8., 8.] if kind == 1 else [-1., .5, 2., 3.5, 5., 6.5, 8., 9.5, 11.]
            if bits(spline.weights) != bits(weights) or bits(spline.knots) != bits(knots):
                raise ValueError(f'{path}: weights or knot parameterization changed')
        a, b = splines
        if any(bits(x) != bits(y) for x, y in zip(a.control_points, b.control_points)):
            raise ValueError(f'{path}: different copies of stored geometry')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 36 files / 72 splines; zero audit errors/repairs')
    print('Zero end tangents are verified with the independent tag decoder; ezdxf drops them in its semantic model.')


if __name__ == '__main__':
    main()
