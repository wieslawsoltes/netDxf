#!/usr/bin/env python3
"""Independent exact collocation oracle for rational Bezier span extraction.

Source homogeneous polynomials are evaluated with full Cox-de Boor bases at
rational interior sites. A Bernstein collocation system determines coefficients;
no production blossom, local refinement or floating sampler is reused.
"""
from __future__ import annotations
import argparse
from fractions import Fraction as F
from functools import lru_cache
import json
import math
from pathlib import Path
import ezdxf
from verify_spline_knot_insertion import (VERSIONS, require, subject, coefficients, evaluate,
    packets, geometry, one, expected_record, check_record, rejects)
from verify_spline_split import broken_subject


def basis_subject(degree, kind):
    if kind < 9:
        return subject(degree, kind)
    if kind == 9:
        return broken_subject(degree)
    knots = ([0.] * (degree + 1) + [.25] * max(1, degree - 1) + [.5] * degree
             + [.75] * (degree + 1) + [1.] * (degree + 1)) if kind == 10 else (
                 [1.] * (degree + 1) + [math.nextafter(1., math.inf)] * (degree + 1))
    count = len(knots) - degree - 1
    return dict(degree=degree, points=[[float(i - 2), float(i * 7 % 11 - 4), float(i % 3 - 1)] for i in range(count)],
                weights=[1 + i % 4 * .25 for i in range(count)], knots=knots)


def basis_values(knots, degree, u, left=False):
    knots = list(map(F, knots))
    v = [F(int(a < u <= b if left else a <= u < b)) for a, b in zip(knots, knots[1:])]
    for p in range(1, degree + 1):
        v = [((u - knots[i]) * v[i] / (knots[i + p] - knots[i]) if knots[i + p] != knots[i] else F(0))
             + ((knots[i + p + 1] - u) * v[i + 1] / (knots[i + p + 1] - knots[i + 1])
                if knots[i + p + 1] != knots[i + 1] else F(0)) for i in range(len(v) - 1)]
    return v


def homogeneous(source, u, left=False):
    factors = [x * F(w) for x, w in zip(basis_values(source['knots'], source['degree'], u, left), source['weights'])]
    return [sum(x * F(point[a]) for x, point in zip(factors, source['points'])) for a in range(3)] + [sum(factors)]


def solve(matrix, right):
    # Exact Gaussian elimination with four homogeneous right-hand columns.
    n = len(matrix); width = len(right[0]); a = [list(row) + list(rhs) for row, rhs in zip(matrix, right)]
    for col in range(n):
        pivot = next((i for i in range(col, n) if a[i][col]), None)
        require(pivot is not None, 'Singular independent collocation matrix')
        a[col], a[pivot] = a[pivot], a[col]
        divisor = a[col][col]; a[col] = [v / divisor for v in a[col]]
        for row in range(n):
            if row != col and a[row][col]:
                factor = a[row][col]
                a[row] = [v - factor * x for v, x in zip(a[row], a[col])]
    return [row[n:n + width] for row in a]


@lru_cache(maxsize=None)
def bernstein_inverse(degree):
    n = degree + 1
    sites = [F(i + 1, degree + 2) for i in range(n)]
    matrix = [[F(math.comb(degree, i)) * u ** i * (1 - u) ** (degree - i) for i in range(n)] for u in sites]
    return solve(matrix, [[F(int(i == j)) for j in range(n)] for i in range(n)])


def spans(source):
    knots = source['knots']; p = source['degree']
    return [(knots[i], knots[i + 1]) for i in range(p, len(source['points'])) if knots[i] < knots[i + 1]]


def expected_spans(source):
    degree = source['degree']; inverse = bernstein_inverse(degree); result = []
    for a, b in spans(source):
        rhs = [homogeneous(source, F(a) + (F(b) - F(a)) * F(i + 1, degree + 2)) for i in range(degree + 1)]
        controls = [[sum(v * rhs[i][axis] for i, v in enumerate(row)) for axis in range(4)] for row in inverse]
        require(all(p[3] > 0 for p in controls), 'Nonpositive independent weight')
        result.append(dict(degree=degree, points=[[float(c / p[3]) for c in p[:3]] for p in controls],
                           weights=[float(p[3]) for p in controls], knots=[a] * (degree + 1) + [b] * (degree + 1)))
    return result


def check_shape(source, part):
    p = part['degree']; lo, hi = map(F, (part['knots'][p], part['knots'][len(part['points'])]))
    scale = max(abs(c) for point in source['points'] for c in point)
    tolerance = F(max(math.ldexp(1., -1074), scale * 8e-14))
    for i in range(17):
        u = lo + (hi - lo) * F(i, 16)
        for a, b in zip(evaluate(source, u, left_limit=i == 16), evaluate(part, u)):
            require(abs(a - b) <= tolerance, 'Extracted Bezier changed one-sided source locus')
    return 17


def result_packet(source, expected):
    record = expected_record(source, expected)
    # The existing writer determines closedness from endpoint distance. The
    # ordinary wire corpus is open; generated Bezier spans stay open too.
    return [(code, expected['degree'] if code == 71 else value) for code, value in record]


def verify_wires(root, prefix, cases, expected_fn):
    names = {f'{prefix}-{d}-{k}-{version}-{binary}.dxf' for d, k in cases for version in VERSIONS for binary in (False, True)}
    actual_names = {p.name for p in root.glob(prefix + '-*.dxf')}
    require(actual_names == names, 'Missing or extra basis-conversion drawings')
    inventory = rejects(lambda v: require(v == names, 'inventory mismatch'), actual_names - {min(actual_names)})
    inventory += rejects(lambda v: require(v == names, 'inventory mismatch'), actual_names | {prefix + '-unexpected.dxf'})
    drawings = corruptions = 0
    for degree, kind in cases:
        source = basis_subject(degree, kind); expected_parts = expected_fn(source)
        for version, profile in VERSIONS.items():
            for binary in (False, True):
                path = root / f'{prefix}-{degree}-{kind}-{version}-{binary}.dxf'
                records = packets(path, binary)
                require(len(records) == len(expected_parts) + 1, 'Wrong basis SPLINE record inventory')
                before = records[0]; coefficients(geometry(before), source)
                handles = [one(r, 5) for r in records]; require(len(set(handles)) == len(handles), 'Duplicate basis handle')
                for actual, expected in zip(records[1:], expected_parts):
                    target = result_packet(before, expected)
                    validate = lambda v: check_record(v, target, one(before, 5), one(before, 330))
                    validate(actual); coefficients(geometry(actual), expected)
                    for i, (code, value) in enumerate(actual):
                        corruptions += rejects(validate, actual[:i] + actual[i + 1:])
                        corruptions += rejects(validate, actual[:i] + [actual[i]] + actual[i:])
                        if code in (5, 330): continue
                        wrong = value + 1 if isinstance(value, (int, float)) else b'wrong' if isinstance(value, bytes) else str(value) + '_wrong'
                        corruptions += rejects(validate, actual[:i] + [(code, wrong)] + actual[i + 1:])
                doc = ezdxf.readfile(path); require(doc.dxfversion == profile, 'Wrong basis DXF version')
                audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Basis drawing requires graph repair')
                drawings += 1
    return drawings, corruptions, inventory


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory', type=Path)
    root = parser.parse_args().directory
    rows = json.loads((root / 'spline-bezier-numerics.json').read_text())
    require([(r['degree'], r['kind']) for r in rows] == [(d, k) for d in range(1, 11) for k in range(12)], 'Bezier numerical inventory')
    samples = numerical = pieces = 0
    for row in rows:
        source = basis_subject(row['degree'], row['kind']); coefficients(row['source'], source)
        expected = expected_spans(source); require(len(row['parts']) == len(expected), 'Bezier span inventory')
        for actual, wanted in zip(row['parts'], expected):
            coefficients(actual, wanted); samples += check_shape(source, actual); pieces += 1
            for key in ('weights', 'knots', 'points'):
                changed = json.loads(json.dumps(actual))
                if key == 'points': changed[key][0][0] = math.nextafter(changed[key][0][0], math.inf)
                else: changed[key][0] = math.nextafter(changed[key][0], math.inf)
                numerical += rejects(lambda v: coefficients(v, wanted), changed)
    drawings, corruptions, inventory = verify_wires(root, 'spline-bezier-wire', [(d, k) for d in (2, 3) for k in (0, 8, 10)], expected_spans)
    print(f'PASS: {len(rows)} independent Bernstein-collocation scenarios / {pieces} spans / {samples} one-sided samples; '
          f'{drawings} drawings; {corruptions} packet, {numerical} numerical and {inventory} inventory corruptions rejected; zero graph errors/repairs')


if __name__ == '__main__':
    main()
