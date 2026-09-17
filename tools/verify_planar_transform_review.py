#!/usr/bin/env python3
"""Independently check affine SOLID/TRACE OCS geometry and signed extrusion.

Expected world points are computed from fixed source geometry, Autodesk's OCS
convention via ezdxf, and separately specified matrices. No emitted expected-point
manifest or production transform helper is used. Numerical checks are relative
(2e-13), including the very small/large cases; this is not native visual QA.
"""
import argparse
import copy
import math
from pathlib import Path
import ezdxf
from ezdxf.math import OCS, Vec3
from verify_editable_table_styles import records
from verify_legacy_utilities import PROFILES
from verify_mleader_inputs import check

CORNERS = ((1, 2), (4, 3), (2, 7), (6, 8))


def operation(scenario):
    matrix = ((1., 0., 0.), (0., 1., 0.), (0., 0., 1.))
    table = {
        2: ((2, 0, 0), (0, 3, 0), (0, 0, 4)),
        3: ((1, .5, 0), (0, 2, 0), (0, 0, 3)),
        4: ((1, 0, 0), (0, 1, 0), (.5, -.25, 1)),
        5: ((-2, 0, 0), (0, 3, 0), (0, 0, 4)),
        6: ((2, 0, 0), (0, 3, 0), (0, 0, -4)),
        7: ((0, 0, 1), (0, 1, 0), (-1, 0, 0)),
        8: ((2, 0, 0), (0, 3, 0), (0, 0, 0)),
        9: ((0, -3, 0), (3, 0, 0), (0, 0, 3)),
        10: ((1e-150, 0, 0), (0, 1e-150, 0), (0, 0, 1e-150)),
        11: ((1e150, 0, 0), (0, 1e150, 0), (0, 0, 1e150)),
    }
    translation = (9, -4, 7) if scenario == 1 else (1e15, -1e15, 7) if scenario == 12 else (0, 0, 0)
    return table.get(scenario, matrix), Vec3(translation)


def multiply(matrix, value):
    return Vec3(*(math.fsum(a*b for a, b in zip(row, value)) for row in matrix))


def unit(value):
    value = Vec3(value); scale = max(abs(v) for v in value)
    check(scale > 0 and math.isfinite(scale), 'Invalid normal')
    return (value / scale).normalize()


def near(expected, actual, label):
    scale = max(abs(v) for v in expected)
    tolerance = max(8 * math.ulp(0.), scale * 2e-13)
    check(all(math.isfinite(a) and abs(e-a) <= tolerance for e, a in zip(expected, actual)), label)


def one(tags, code):
    values = [v for c, v in tags if c == code]
    check(len(values) == 1, f'Missing/repeated planar group {code}')
    return values[0]


def verify(items, scenario):
    selected = [(h, t) for h, t in items.items() if t[0][1] in ('SOLID', 'TRACE')]
    check(sorted(t[0][1] for _, t in selected) == ['SOLID', 'TRACE'], 'Exact planar entity inventory required')
    normal = Vec3(1, 2, 3).normalize() if scenario == 9 else Vec3(0, 0, 1)
    source = OCS(normal); matrix, translation = operation(scenario)
    points = [multiply(matrix, source.to_wcs((x, y, 7.25))) + translation for x, y in CORNERS]
    extrusion = multiply(matrix, normal * (0 if scenario == 4 else 2.5))
    expected_normal = unit(unit(multiply(matrix, source.ux)).cross(unit(multiply(matrix, source.uy))))
    for _, tags in selected:
        check(one(tags, 62) == 3 and one(tags, 60) == 1, 'Planar common state changed')
        actual_normal = Vec3(one(tags, 210)); check(abs(actual_normal.magnitude - 1) < 2e-13, 'Normal is not a unit vector')
        near(expected_normal, actual_normal, 'Incorrect transformed plane orientation')
        target = OCS(actual_normal)
        stored = [one(tags, c) for c in (10, 11, 12, 13)]
        check(all(v[2] == stored[0][2] for v in stored), 'Planar corners disagree on elevation')
        for expected, point in zip(points, stored): near(expected, target.to_wcs(point), 'Incorrect transformed world corner')
        near(extrusion, actual_normal * one(tags, 39), 'Incorrect signed extrusion')
        if scenario == 12: check(stored[0][2] == 14.25, 'Large in-plane translation changed elevation')
    return selected


def corruptions(items, scenario):
    total = 0
    for handle, tags in verify(items, scenario):
        for index, (code, value) in enumerate(tags):
            if code not in (10, 11, 12, 13, 210, 39, 62, 60): continue
            for component in range(3) if isinstance(value, list) else (None,):
                packet = copy.deepcopy(tags)
                if component is None:
                    changed = value * 1.125 if value else .125
                else:
                    changed = list(value); old = value[component]; changed[component] = old * 1.125 if old else .125
                packet[index] = [code, changed]; candidate = dict(items); candidate[handle] = packet
                try: verify(candidate, scenario)
                except ValueError: total += 1
                else: raise AssertionError(f'Accepted planar corruption group {code}, component {component}')
        candidate = dict(items); del candidate[handle]
        try: verify(candidate, scenario)
        except ValueError: total += 1
        else: raise AssertionError('Accepted missing planar entity')
    return total


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    wanted = {f'planar-review-{v}-{b}-{s}.dxf' for v in PROFILES for b in (False, True) for s in range(13)}
    check({p.name for p in directory.glob('planar-review-*.dxf')} == wanted, 'Exact planar file inventory required')
    total = 0
    for version, profile in PROFILES.items():
        for binary in (False, True):
            for scenario in range(13):
                path = directory / f'planar-review-{version}-{binary}-{scenario}.dxf'
                check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Planar transport changed')
                doc = ezdxf.readfile(path); check(doc.dxfversion == profile, 'Planar profile changed')
                total += corruptions(records(path), scenario)
                audit = doc.audit(); check(not audit.errors and not audit.fixes, 'Planar drawing needs repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: {len(wanted)} drawings, 312 planar entities; {total} corruptions rejected; zero audit errors/repairs')


if __name__ == '__main__':
    main()
