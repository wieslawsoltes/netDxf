#!/usr/bin/env python3
"""Independently verify LWPOLYLINE vertex packets and reversal with ezdxf.

Usage: python tools/verify_lwpolyline_integrity.py artifacts/conformance
Autodesk packet reference: GUID-748FC305-F3F2-4F74-825A-61F04D757A50.
The optional development dependency is never loaded by the production library.
"""
import argparse
import math
import re
from pathlib import Path
import ezdxf
from ezdxf.math import Vec2, bulge_to_arc


def point(a, b, fraction):
    start, end = Vec2(a[:2]), Vec2(b[:2])
    if a[4] == 0:
        return start.lerp(end, fraction)
    center, _, _, radius = bulge_to_arc(start, end, a[4])
    angle = (start - center).angle + 4 * math.atan(a[4]) * fraction
    return center + Vec2.from_angle(angle, radius)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    versions = (2000, 2004, 2007, 2010, 2013, 2018)
    expected = {f'lwpolyline-packet-AutoCad{v}-{b}-{order}.dxf'
                for v in versions for b in ('False', 'True') for order in range(8)}
    expected |= {f'lwpolyline-reverse-AutoCad{v}-{b}-{closed}.dxf'
                 for v in versions for b in ('False', 'True') for closed in ('False', 'True')}
    paths = sorted(args.artifacts.glob('lwpolyline-*.dxf'))
    if {p.name for p in paths} != expected:
        raise ValueError('Expected all 120 LWPOLYLINE packet/reversal version/transport fixtures')
    comparisons = 0
    for path in paths:
        doc = ezdxf.readfile(path)
        profile = re.search(r"AutoCad(20\d\d)-(False|True)-", path.name)
        profiles = {"2000":"AC1015", "2004":"AC1018", "2007":"AC1021", "2010":"AC1024", "2013":"AC1027", "2018":"AC1032"}
        if profile is None or doc.dxfversion != profiles[profile[1]]:
            raise ValueError(f'{path}: DXF header version does not match fixture profile')
        with path.open('rb') as stream:
            if stream.read(22).startswith(b'AutoCAD Binary DXF') != (profile[2] == 'True'):
                raise ValueError(f'{path}: transport does not match fixture profile')
        polylines = list(doc.modelspace().query('LWPOLYLINE'))
        if path.name.startswith('lwpolyline-packet-'):
            if len(polylines) != 1:
                raise ValueError(f'{path}: expected one packet polyline')
            polyline = polylines[0]
            expected_points = [(i * 3.0 - 0.125, i * i + 0.375, i + 0.25, i + 1.5, (i - 1) * 0.5) for i in range(3)]
            if list(polyline.get_points('xyseb')) != expected_points:
                raise ValueError(f'{path}: vertex coordinates, widths or bulges changed')
            if not polyline.closed or polyline.dxf.elevation != 2.5 or polyline.dxf.thickness != -0.75:
                raise ValueError(f'{path}: closed/elevation/thickness changed')
            if list(polyline.get_xdata('LW_PACKET')) != [(1000, 'after vertices'), (1070, 73)]:
                raise ValueError(f'{path}: following XData changed')
            lines = list(doc.modelspace().query('LINE'))
            if len(lines) != 1 or tuple(lines[0].dxf.start) != (91.0, -37.0, 0.0):
                raise ValueError(f'{path}: following entity changed')
        else:
            if len(polylines) != 2:
                raise ValueError(f'{path}: original and reversed polylines required')
            original, reversed_ = [list(p.get_points('xyseb')) for p in polylines]
            if polylines[0].closed != polylines[1].closed:
                raise ValueError(f'{path}: closed status changed')
            count = len(original)
            edges = count if polylines[0].closed else count - 1
            for index in range(edges):
                a, b = reversed_[index], reversed_[(index + 1) % count]
                candidates = [j for j in range(edges) if original[j][:2] == b[:2] and original[(j + 1) % count][:2] == a[:2]]
                if len(candidates) != 1:
                    raise ValueError(f'{path}: reversed edge not found')
                source = candidates[0]
                old_a, old_b = original[source], original[(source + 1) % count]
                if a[2] != old_a[3] or a[3] != old_a[2]:
                    raise ValueError(f'{path}: taper endpoints were not exchanged')
                for sample in range(17):
                    t = sample / 16
                    if math.dist(point(a, b, t), point(old_a, old_b, 1-t)) > 1e-10:
                        raise ValueError(f'{path}: reversed arc locus differs at {t}')
                    width = a[2] * (1-t) + a[3] * t
                    old_width = old_a[2] * t + old_a[3] * (1-t)
                    if abs(width - old_width) > 1e-12:
                        raise ValueError(f'{path}: taper geometry differs at {t}')
                    comparisons += 1
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} audit errors / {len(audit.fixes)} repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: {len(paths)} drawings / 144 polylines / {comparisons} edge-and-taper comparisons; zero audit errors/repairs')


if __name__ == '__main__':
    main()
