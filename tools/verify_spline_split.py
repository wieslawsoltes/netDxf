#!/usr/bin/env python3
"""Validate SPLINE subdivision with exact coefficient and one-sided curve oracles.

Retains absolute knot domains and handles degree+1 discontinuities separately.
Uses independently regenerated inputs and the Fraction single-insertion oracle.
"""
from __future__ import annotations
import argparse
from fractions import Fraction as F
import json
import math
from pathlib import Path
import ezdxf
from verify_spline_knot_insertion import (VERSIONS, require, subject, knot_coordinate, insert,
    coefficients, evaluate, packets, geometry, one, expected_record, check_record, rejects)


def broken_subject(degree):
    count = 2 * (degree + 1)
    return dict(degree=degree, points=[[float(i - 2), float(i % 3), float(i * i % 7)] for i in range(count)],
                weights=[1 + i % 4 * .25 for i in range(count)],
                knots=[0.] * (degree + 1) + [.5] * (degree + 1) + [1.] * (degree + 1))


def subdivide(source, parameter):
    degree = source['degree']; existing = source['knots'].count(parameter)
    data = insert(source, parameter, max(0, degree - existing))
    first = data['knots'].index(parameter)
    count = data['knots'].count(parameter)
    right_first = first if count == degree + 1 else first - 1
    before = dict(degree=degree, points=data['points'][:first], weights=data['weights'][:first],
                  knots=data['knots'][:first] + [parameter] * (degree + 1))
    after = dict(degree=degree, points=data['points'][right_first:], weights=data['weights'][right_first:],
                 knots=[parameter] * (degree + 1) + data['knots'][first + count:])
    return [before, after]


def part_shape(source, part, left_side, parameter):
    degree = part['degree']
    lo, hi = map(F, (part['knots'][degree], part['knots'][len(part['points'])]))
    scale = max(abs(c) for p in source['points'] for c in p)
    tolerance = F(max(math.ldexp(1., -1074), scale * 5e-14))
    for i in range(17):
        u = lo + (hi - lo) * F(i, 16)
        # A full multiplicity break uses the source's left-hand value for the
        # terminal point of the first piece, not the right-hand convention.
        expected = evaluate(source, u, left_limit=left_side and u == F(parameter))
        for a, b in zip(expected, evaluate(part, u)):
            require(abs(a - b) <= tolerance, 'Split changed one-sided source geometry')


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory', type=Path)
    root = parser.parse_args().directory
    wire_cases = [(d, broken, token) for d in (2, 3) for broken in (False, True)
                  for token in (('existing',) if broken else ('new', 'existing'))]
    expected_names = {f'spline-split-wire-{d}-{b}-{t}-{v}-{binary}.dxf'
                      for d, b, t in wire_cases for v in VERSIONS for binary in (False, True)}
    require({p.name for p in root.glob('spline-split-wire-*.dxf')} == expected_names, 'Missing or extra split fixtures')
    rows = json.loads((root / 'spline-split-numerics.json').read_text())
    require([(r['degree'], r['kind'], r['fraction']) for r in rows]
            == [(d, k, f) for d in range(1, 11) for k in range(8) for f in (.125, .5, .875)], 'Split numerical inventory differs')
    numerical_controls = 0
    for row in rows:
        source = subject(row['degree'], row['kind']); coefficients(row['source'], source)
        parameter = knot_coordinate(row['fraction'], row['kind'])
        require(row['parameter'] == parameter and len(row['parts']) == 2, 'Split request/result shape changed')
        expected = subdivide(source, parameter)
        for side in (0, 1):
            actual = row['parts'][side]; coefficients(actual, expected[side]); part_shape(source, actual, side == 0, parameter)
            for axis in range(3):
                changed = dict(actual); changed['points'] = [list(p) for p in actual['points']]
                changed['points'][-1][axis] = math.nextafter(changed['points'][-1][axis], math.inf)
                numerical_controls += rejects(lambda value: coefficients(value, expected[side]), changed)
    drawings = corruptions = 0
    for degree, broken, token in wire_cases:
        source = broken_subject(degree) if broken else subject(degree, 0)
        parameter = .125 if token == 'new' else .5
        expected_parts = subdivide(source, parameter)
        for side in (0, 1):
            part_shape(source, expected_parts[side], side == 0, parameter)
        for version, profile in VERSIONS.items():
            for binary in (False, True):
                path = root / f'spline-split-wire-{degree}-{broken}-{token}-{version}-{binary}.dxf'
                records = packets(path, binary); require(len(records) == 3, 'Wrong SPLINE split record inventory')
                before, left, right = records; coefficients(geometry(before), source)
                require(one(left, 5) != one(right, 5), 'Sibling split handles alias')
                source_handle, owner = one(before, 5), one(before, 330)
                for side, actual in enumerate((left, right)):
                    expected = expected_record(before, expected_parts[side])
                    validate = lambda value: check_record(value, expected, source_handle, owner)
                    validate(actual); coefficients(geometry(actual), expected_parts[side])
                    for i, (code, value) in enumerate(actual):
                        corruptions += rejects(validate, actual[:i] + actual[i + 1:])
                        corruptions += rejects(validate, actual[:i] + [actual[i]] + actual[i:])
                        if code in (5, 330):
                            continue
                        wrong = value + 1 if isinstance(value, (int, float)) else b'wrong' if isinstance(value, bytes) else str(value) + '_wrong'
                        corruptions += rejects(validate, actual[:i] + [(code, wrong)] + actual[i + 1:])
                doc = ezdxf.readfile(path); require(doc.dxfversion == profile, 'Wrong DXF profile')
                audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Split drawing requires repairs')
                drawings += 1
    print(f'PASS: {len(rows)} regenerated subdivision scenarios / {len(rows) * 34} one-sided curve samples; '
          f'{drawings} drawings; {corruptions} packet and {numerical_controls} numerical corruptions rejected; zero graph errors/repairs')


if __name__ == '__main__':
    main()
