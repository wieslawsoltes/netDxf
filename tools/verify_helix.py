#!/usr/bin/env python3
"""Verify typed HELIX exports with independent ezdxf (development only).

Usage: python tools/verify_helix.py artifacts/conformance
Checks both authored representations, not their mathematical consistency or native AutoCAD behavior.
"""
import argparse
import struct
from pathlib import Path
import ezdxf


def bits(values):
    return [struct.pack('<d', x) for x in values]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    files = sorted(args.artifacts.glob('helix-wire-*.dxf'))
    expected_names = {f'helix-wire-AutoCad{v}-{b}-{c}-{r}.dxf'
                      for v in (2007, 2010, 2013, 2018) for b in ('False', 'True')
                      for c in range(3) for r in ('False', 'True')}
    if {p.name for p in files} != expected_names:
        raise ValueError('Expected 48 HELIX profile/transport/constraint/handedness drawings')
    for path in files:
        doc = ezdxf.readfile(path)
        helices = list(doc.modelspace().query('HELIX'))
        if len(helices) != 2:
            raise ValueError(f'{path}: expected original and deep clone')
        _, _, _, _, constraint, handedness = path.stem.split('-')
        for h in helices:
            if h.dxf.major_release_number != 29 or h.dxf.maintenance_release_number != 63:
                raise ValueError(f'{path}: class version lost')
            for name, expected in {'axis_base_point': (17.25, -11.5, 23), 'start_point': (22.25, -11.5, 23),
                                   'axis_vector': (0, 0, 2), 'start_tangent': (3, 4, 5), 'end_tangent': (6, 7, 8)}.items():
                if bits(h.dxf.get(name)) != bits(expected):
                    raise ValueError(f'{path}: {name} changed')
            if bits((h.dxf.radius, h.dxf.turns, h.dxf.turn_height)) != bits((2.75, 3.125, -1.5)):
                raise ValueError(f'{path}: helix scalars changed')
            if h.dxf.constrain != int(constraint) or bool(h.dxf.handedness) != (handedness == 'True'):
                raise ValueError(f'{path}: constraint/handedness changed')
            if bits((component for point in h.control_points for component in point)) != bits((5, 0, 0, 5, 3, 1, 2, 5, 2, 0, 5, 3)):
                raise ValueError(f'{path}: stored spline was refitted')
            if bits(h.knots) != bits((0, 0, 0, 0, 1, 1, 1, 1)) or bits(h.weights) != bits((1, 1.125, 1.25, 1.375)):
                raise ValueError(f'{path}: spline knots/weights changed')
            if list(h.get_xdata('HELIX_TEST'))[0].value != 'after helix':
                raise ValueError(f'{path}: XData lost')
        definition = next(c for c in doc.classes if c.dxf.name == 'HELIX')
        if definition.dxf.cpp_class_name != 'AcDbHelix' or not definition.dxf.is_an_entity or definition.dxf.instance_count != 2:
            raise ValueError(f'{path}: invalid HELIX class')
        if tuple(doc.modelspace().query('LINE')[0].dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: following entity lost')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} audit errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 48 files / 96 HELIX entities; zero audit errors/repairs')


if __name__ == '__main__':
    main()
