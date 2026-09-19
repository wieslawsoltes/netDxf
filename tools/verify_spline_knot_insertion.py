#!/usr/bin/env python3
"""Independent exact-rational knot insertion and physical SPLINE verification.

The oracle performs repeated SINGLE insertion over Fraction homogeneous controls;
production performs one multiple-insertion operation over its bounded Rational type.
Inputs are independently regenerated. Stored coefficients are compared exactly.
Shape checks evaluate full Cox-de Boor bases with Fraction and admit 4e-14 times
source coordinate magnitude (at least the smallest binary64 value).
"""
from __future__ import annotations
import argparse
from fractions import Fraction as F
import io
import json
import math
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

VERSIONS = {'AutoCad2000': 'AC1015', 'AutoCad2004': 'AC1018', 'AutoCad2007': 'AC1021',
            'AutoCad2010': 'AC1024', 'AutoCad2013': 'AC1027', 'AutoCad2018': 'AC1032'}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def knot_coordinate(value, kind):
    if kind == 1:
        return value * 4 + 2
    if kind == 2:
        return (2 * value - 1) * sys.float_info.max
    if kind == 3:
        return value * math.ldexp(1., -1020)
    return value


def subject(degree, kind):
    count = degree + 4
    points = [[float(i - 2), float(i * 7 % 11 - 4), float(i % 3 - 1)] for i in range(count)]
    weights = [1 + i % 4 * .25 for i in range(count)]
    if kind == 4:
        weights = [sys.float_info.max * (.5 + i % 4 * .125) for i in range(count)]
    if kind == 5:
        weights = [math.ldexp(1., -1070)] * count
    if kind in (6, 7):
        scale = math.ldexp(1., 990 if kind == 6 else -990)
        points = [[v * scale for v in point] for point in points]
    knots = [knot_coordinate(max(0., min(1., (i - degree) / 4.)), kind)
             for i in range(count + degree + 1)]
    if kind == 8:
        knots = [(i - degree) / 4. for i in range(len(knots))]
    return dict(degree=degree, points=points, weights=weights, knots=knots)


def insert(source, parameter, times):
    degree = source['degree']
    points = [[F(c) * F(w) for c in p] + [F(w)] for p, w in zip(source['points'], source['weights'])]
    knots, u = list(map(F, source['knots'])), F(parameter)
    for _ in range(times):
        span = max(i for i in range(len(points)) if knots[i] <= u)
        multiplicity = knots.count(u)
        output = [None] * (len(points) + 1)
        for i in range(span - degree + 1):
            output[i] = points[i]
        for i in range(span - multiplicity, len(points)):
            output[i + 1] = points[i]
        for i in range(span - degree + 1, span - multiplicity + 1):
            alpha = (u - knots[i]) / (knots[i + degree] - knots[i])
            output[i] = [(1 - alpha) * a + alpha * b for a, b in zip(points[i - 1], points[i])]
        require(all(p is not None for p in output), 'Oracle insertion left a control unset')
        points = output
        knots.insert(span + 1, u)
    return dict(degree=degree, points=[[float(v / p[3]) for v in p[:3]] for p in points],
                weights=[float(p[3]) for p in points], knots=list(map(float, knots)))


def coefficients(actual, expected):
    require(isinstance(actual, dict) and set(actual) == set(expected), 'Coefficient schema differs')
    require(actual['degree'] == expected['degree'], 'Degree differs')
    for key in ('points', 'weights', 'knots'):
        require(actual[key] == expected[key], f'Exact {key} differ')
    require(all(math.isfinite(x) for p in actual['points'] for x in p), 'Nonfinite output control')


def evaluate(source, u, left_limit=False):
    knots = list(map(F, source['knots'])); u = F(u); p = source['degree']
    end = left_limit or u == knots[len(source['points'])]
    values = [F(int(a < u <= b if end else a <= u < b)) for a, b in zip(knots, knots[1:])]
    for order in range(1, p + 1):
        new = []
        for i in range(len(values) - 1):
            left = (u - knots[i]) * values[i] / (knots[i + order] - knots[i]) if knots[i + order] != knots[i] else F(0)
            right = (knots[i + order + 1] - u) * values[i + 1] / (knots[i + order + 1] - knots[i + 1]) if knots[i + order + 1] != knots[i + 1] else F(0)
            new.append(left + right)
        values = new
    factors = [b * F(w) for b, w in zip(values, source['weights'])]
    total = sum(factors)
    require(total > 0, 'Positive-weight oracle denominator is not positive')
    return [sum(w * F(point[axis]) for w, point in zip(factors, source['points'])) / total for axis in range(3)]


def shape(source, actual):
    lo, hi = map(F, (source['knots'][source['degree']], source['knots'][len(source['points'])]))
    scale = max(abs(c) for p in source['points'] for c in p)
    tolerance = F(max(math.ldexp(1., -1074), scale * 4e-14))
    for i in range(17):
        u = lo + (hi - lo) * F(i, 16)
        for a, b in zip(evaluate(source, u), evaluate(actual, u)):
            require(abs(a - b) <= tolerance, 'Refinement changed the independently evaluated curve')


def packets(path, binary):
    data = path.read_bytes()
    require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    loader = binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    result, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0] == (0, 'SPLINE'):
                result.append(current)
            current = []
        value = cast_tag_value(tag.code, tag.value)
        if tag.code in (310, 1004):
            value = bytes(tag.value) if isinstance(tag.value, (bytes, bytearray)) else bytes.fromhex(str(tag.value))
        current.append((tag.code, value))
    if current and current[0] == (0, 'SPLINE'):
        result.append(current)
    return result


def one(record, code):
    values = [v for c, v in record if c == code]
    require(len(values) == 1, f'Missing or repeated group {code}')
    return values[0]


def geometry(record):
    points = []
    for i, (code, value) in enumerate(record):
        if code == 10:
            require([c for c, _ in record[i:i + 3]] == [10, 20, 30], 'Bad control framing')
            points.append([float(v) for _, v in record[i:i + 3]])
    return dict(degree=one(record, 71), points=points, weights=[v for c, v in record if c == 41],
                knots=[v for c, v in record if c == 40])


def expected_record(source, expected):
    records = []
    inserted = False
    for code, value in source:
        if code in (5, 330, 92, 160, 310):
            continue
        if code in (40, 10, 20, 30, 41):
            if not inserted:
                records.extend((40, k) for k in expected['knots'])
                for point, weight in zip(expected['points'], expected['weights']):
                    records.extend(zip((10, 20, 30), point)); records.append((41, weight))
                inserted = True
            continue
        records.append((code, value))
    return records


def check_record(actual, expected, source_handle, owner):
    handle = one(actual, 5)
    require(isinstance(handle, str) and int(handle, 16) > 0 and handle != source_handle, 'Result identity framing/uniqueness')
    require(one(actual, 330) == owner, 'Owner changed')
    require([(c, v) for c, v in actual if c not in (5, 330)] == expected, 'Ordered refined packet differs')


def rejects(function, value):
    try:
        function(value)
    except ValueError:
        return 1
    raise AssertionError('Corrupted output escaped its positive validator')


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory', type=Path)
    root = parser.parse_args().directory
    names = {f'spline-knot-wire-{d}-{repeat}-{d - 1 if repeat else d}-{v}-{b}.dxf'
             for d in (2, 3) for repeat in (False, True) for v in VERSIONS for b in (False, True)}
    require({p.name for p in root.glob('spline-knot-wire-*.dxf')} == names, 'Missing or extra knot wire fixtures')
    path = root / 'spline-knot-numerics.json'
    rows = json.loads(path.read_text())
    require([(r['degree'], r['kind']) for r in rows] == [(d, k) for d in range(1, 11) for k in range(9)], 'Wrong numerical inventory')
    numerical_controls = 0
    for row in rows:
        source = subject(row['degree'], row['kind']); coefficients(row['source'], source)
        parameter = knot_coordinate(.125, row['kind'])
        require(row['parameter'] == parameter and row['times'] == row['degree'], 'Numerical request changed')
        expected = insert(source, parameter, row['times'])
        coefficients(row['result'], expected); shape(source, row['result'])
        for axis in range(3):
            changed = dict(row['result']); changed['points'] = [list(p) for p in changed['points']]
            changed['points'][1][axis] = math.nextafter(changed['points'][1][axis], math.inf)
            numerical_controls += rejects(lambda value: coefficients(value, expected), changed)
    corruptions = drawings = 0
    for degree in (2, 3):
        source = subject(degree, 0)
        for repeat in (False, True):
            times = degree - 1 if repeat else degree; parameter = .5 if repeat else .125
            expected_geometry = insert(source, parameter, times)
            for version, profile in VERSIONS.items():
                for binary in (False, True):
                    path = root / f'spline-knot-wire-{degree}-{repeat}-{times}-{version}-{binary}.dxf'
                    records = packets(path, binary); require(len(records) == 2, 'Wrong SPLINE record count')
                    before, actual = records
                    coefficients(geometry(before), source)
                    expected = expected_record(before, expected_geometry)
                    handle, owner = one(before, 5), one(before, 330)
                    validate = lambda value: check_record(value, expected, handle, owner)
                    validate(actual); coefficients(geometry(actual), expected_geometry)
                    for i, (code, value) in enumerate(actual):
                        corruptions += rejects(validate, actual[:i] + actual[i + 1:])
                        corruptions += rejects(validate, actual[:i] + [actual[i]] + actual[i:])
                        if code in (5, 330):
                            continue
                        wrong = value + 1 if isinstance(value, (int, float)) else b'wrong' if isinstance(value, bytes) else str(value) + '_wrong'
                        corruptions += rejects(validate, actual[:i] + [(code, wrong)] + actual[i + 1:])
                    doc = ezdxf.readfile(path); require(doc.dxfversion == profile, 'Wrong DXF profile')
                    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'DXF requires graph repairs')
                    drawings += 1
    print(f'PASS: {len(rows)} regenerated exact-rational scenarios / {len(rows) * 17} curve samples; '
          f'{drawings} text/binary drawings; {corruptions} packet and {numerical_controls} numerical corruptions rejected; zero graph errors/repairs')


if __name__ == '__main__':
    main()
