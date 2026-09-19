"""Development-only polynomial oracle, independent of homogeneous corner cutting.

Sample each SOURCE homogeneous polynomial over Fraction at p+1 rational sites,
solve its Bernstein coefficient system exactly, then project once to binary64.
For elevation solve directly at the target degree, rather than elevating controls.
"""
from __future__ import annotations
from fractions import Fraction as F
from functools import lru_cache
from math import comb
import math
from verify_spline_knot_insertion import (subject, require, evaluate, coefficients,
    expected_record, check_record, one, rejects, geometry, packets, VERSIONS)
from verify_spline_split import broken_subject


def source_case(degree, kind):
    return broken_subject(degree) if kind == 9 else subject(degree, kind)


def spans(source):
    u, p, n = source['knots'], source['degree'], len(source['points'])
    return [k for k in range(p, n) if u[k] < u[k + 1]]


@lru_cache(maxsize=10)
def bernstein_inverse(degree):
    matrix = []
    for j in range(degree + 1):
        t = F(j, degree)
        matrix.append([F(comb(degree, i)) * t**i * (1 - t)**(degree - i) for i in range(degree + 1)]
                      + [F(i == j) for i in range(degree + 1)])
    for i in range(degree + 1):
        pivot = next(j for j in range(i, degree + 1) if matrix[j][i])
        matrix[i], matrix[pivot] = matrix[pivot], matrix[i]
        divisor = matrix[i][i]
        matrix[i] = [v / divisor for v in matrix[i]]
        for j in range(degree + 1):
            if i == j:
                continue
            factor = matrix[j][i]
            matrix[j] = [a - factor * b for a, b in zip(matrix[j], matrix[i])]
    return tuple(tuple(row[degree + 1:]) for row in matrix)


def homogeneous_value(source, u, left_limit):
    knots = list(map(F, source['knots'])); p = source['degree']; u = F(u)
    basis = [F(int(a < u <= b if left_limit else a <= u < b)) for a, b in zip(knots, knots[1:])]
    for order in range(1, p + 1):
        basis = [((u - knots[i]) * basis[i] / (knots[i + order] - knots[i])
                  if knots[i + order] != knots[i] else F(0))
                 + ((knots[i + order + 1] - u) * basis[i + 1] / (knots[i + order + 1] - knots[i + 1])
                    if knots[i + order + 1] != knots[i + 1] else F(0)) for i in range(len(basis) - 1)]
    terms = [b * F(w) for b, w in zip(basis, source['weights'])]
    return [sum(w * F(point[axis]) for w, point in zip(terms, source['points'])) for axis in range(3)] + [sum(terms)]


def polynomial_segments(source, target_degree=None):
    degree = source['degree'] if target_degree is None else target_degree
    inverse = bernstein_inverse(degree); result = []
    for k in spans(source):
        lo, hi = map(F, source['knots'][k:k + 2])
        samples = [homogeneous_value(source, lo + (hi - lo) * F(i, degree), i == degree) for i in range(degree + 1)]
        controls = [[sum(coefficient * sample[axis] for coefficient, sample in zip(row, samples))
                     for axis in range(4)] for row in inverse]
        require(all(h[3] > 0 for h in controls), 'Bernstein oracle produced a nonpositive weight')
        points = [[float(v / h[3]) for v in h[:3]] for h in controls]
        weights = [float(h[3]) for h in controls]
        result.append(dict(degree=degree, points=points, weights=weights,
                           knots=[float(lo)] * (degree + 1) + [float(hi)] * (degree + 1)))
    return result


def check_segments(source, actual, target_degree=None):
    expected = polynomial_segments(source, target_degree)
    require(len(actual) == len(expected), 'Incorrect Bézier output inventory')
    samples = 0
    tolerance = F(max(math.ldexp(1.0, -1074), max(abs(c) for p in source['points'] for c in p) * 4e-14))
    for item, want in zip(actual, expected):
        coefficients(item, want)
        lo, hi = map(F, (want['knots'][0], want['knots'][-1]))
        for j in range(9):
            u = lo + (hi - lo) * F(j, 8)
            before = evaluate(source, u, left_limit=j == 8)
            after = evaluate(item, u, left_limit=j == 8)
            require(all(abs(a - b) <= tolerance for a, b in zip(before, after)), 'Bézier projection changed source shape')
            samples += 1
    return expected, samples


def output_packet(before, expected):
    # New degree and endpoint-derived closure; other metadata remains as stored.
    result = expected_record(before, expected)
    closed = all(abs(a - b) < 1e-12 for a, b in zip(expected['points'][0], expected['points'][-1]))
    return [(code, expected['degree'] if code == 71 else ((value & ~1) | int(closed)) if code == 70 else value)
            for code, value in result]


def packet_controls(record, expected, handle, owner):
    controls = 0
    for i, (code, value) in enumerate(record):
        if code in (0, 5, 330):
            continue
        changed = list(record)
        replacement = math.nextafter(value, math.inf) if isinstance(value, float) else value + 1 if isinstance(value, int) else str(value) + '!'
        changed[i] = (code, replacement)
        for corrupted in (changed, record[:i] + record[i + 1:], record[:i] + [record[i]] + record[i:]):
            controls += rejects(lambda data: check_record(data, expected, handle, owner), corrupted)
    return controls
