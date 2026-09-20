#!/usr/bin/env python3
"""Check interval trimming with exact source-polynomial collocation, not blossoms.

Selected ordered SPLINE packets compare exactly except separately checked handles.
One-sided locus checks admit 8e-14 of the largest source coordinate, with a
smallest-subnormal floor. Synthetic inputs do not establish native CAD parity.
"""
from __future__ import annotations
import argparse
from fractions import Fraction as F
import json
import math
from pathlib import Path
import ezdxf
from verify_spline_bezier import basis_subject
from spline_degree_oracle import (bernstein_inverse, homogeneous_value, spans, evaluate, output_packet,
    packet_controls, coefficients, packets, geometry, one, require, rejects, check_record, VERSIONS)


def bounds(source, mode):
    a, b = source['knots'][source['degree']], source['knots'][len(source['points'])]
    def at(t):
        return (1 - t) * a + t * b
    return {0: (a, b), 1: (a, at(.5)), 2: (at(.5), b), 3: (at(.125), at(.875)), 4: (at(.375), at(.625))}[mode]


def restricted(source, start, end):
    p = source['degree']; inverse = bernstein_inverse(p)
    result = dict(degree=p, points=[], weights=[], knots=[])
    previous = None
    for span in spans(source):
        a = max(start, source['knots'][span]); b = min(end, source['knots'][span + 1])
        if not a < b:
            continue
        # Determine each restricted polynomial independently by p+1 exact values.
        samples = [homogeneous_value(source, F(a) + (F(b) - F(a)) * F(i, p), i == p) for i in range(p + 1)]
        controls = [[sum(c * sample[axis] for c, sample in zip(row, samples)) for axis in range(4)] for row in inverse]
        require(all(h[3] > 0 for h in controls), 'Nonpositive independent restricted weight')
        points = [[float(v / h[3]) for v in h[:3]] for h in controls]; weights = [float(h[3]) for h in controls]
        separate = previous is None or source['knots'].count(a) == p + 1
        if not separate:
            require(result['points'][-1] == points[0] and result['weights'][-1] == weights[0], 'Independent continuous endpoint mismatch')
        result['points'].extend(points[0 if separate else 1:]); result['weights'].extend(weights[0 if separate else 1:])
        result['knots'].extend([a] * (p + 1 if separate else p)); previous = span
    result['knots'].extend([end] * (p + 1))
    return result


def check_locus(source, result):
    tolerance = F(max(math.ldexp(1., -1074), max(abs(v) for p in source['points'] for v in p) * 8e-14))
    samples = 0
    for k in spans(result):
        a, b = map(F, result['knots'][k:k + 2])
        for i in range(9):
            u = a + (b - a) * F(i, 8)
            x = evaluate(source, u, left_limit=i == 8); y = evaluate(result, u, left_limit=i == 8)
            require(all(abs(v - w) <= tolerance for v, w in zip(x, y)), 'Trim changed one-sided locus')
            samples += 1
    return samples


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory', type=Path)
    root = parser.parse_args().directory
    cases = [(d, k, r) for d in range(1, 11) for k in (0, 2, 5, 8, 9, 10) for r in (1, 2, 3)]
    wires = [(d, k, r) for d in (2, 3) for k in (0, 8, 9) for r in (1, 2, 3)]
    names = {f'spline-trim-wire-{d}-{k}-{r}-{v}-{b}.dxf' for d, k, r in wires for v in VERSIONS for b in (False, True)} | {'spline-trim-numerics.json'}
    actual = {p.name for p in root.glob('spline-trim-*')}
    def inventory(value):
        require(value == names, 'Missing or extra trim output')
    inventory(actual)
    inventory_controls = rejects(inventory, actual - {'spline-trim-numerics.json'})
    inventory_controls += rejects(inventory, actual | {'spline-trim-extra.dxf'})
    rows = json.loads((root / 'spline-trim-numerics.json').read_text())
    require([(r['degree'], r['kind'], r['range']) for r in rows] == cases, 'Trim case inventory differs')
    samples = numerical_controls = 0
    for row in rows:
        source = basis_subject(row['degree'], row['kind']); coefficients(row['source'], source)
        a, b = bounds(source, row['range']); require((row['start'], row['end']) == (a, b), 'Trim range differs')
        expected = restricted(source, a, b); coefficients(row['result'], expected)
        samples += check_locus(source, row['result'])
        for key in ('points', 'weights', 'knots'):
            bad = json.loads(json.dumps(row['result']))
            if key == 'points':
                bad[key][1][0] = math.nextafter(bad[key][1][0], math.inf)
            else:
                bad[key][1] = math.nextafter(bad[key][1], math.inf)
            numerical_controls += rejects(lambda v: coefficients(v, expected), bad)
    drawings = packet_corruptions = 0
    for d, k, r in wires:
        source = basis_subject(d, k); a, b = bounds(source, r); expected = restricted(source, a, b)
        for version, profile in VERSIONS.items():
            for binary in (False, True):
                path = root / f'spline-trim-wire-{d}-{k}-{r}-{version}-{binary}.dxf'
                records = packets(path, binary); require(len(records) == 2, 'Trim wire inventory differs')
                before, result = records; coefficients(geometry(before), source)
                packet = output_packet(before, expected); handle, owner = one(before, 5), one(before, 330)
                check_record(result, packet, handle, owner)
                packet_corruptions += packet_controls(result, packet, handle, owner)
                doc = ezdxf.readfile(path); require(doc.dxfversion == profile, 'Trim profile differs')
                audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Trim graph needs repairs')
                drawings += 1
    print(f'PASS trim: {len(rows)} exact interval systems / {samples} one-sided samples / {drawings} drawings; '
          f'{numerical_controls} numerical, {packet_corruptions} packet and {inventory_controls} inventory corruptions rejected; zero audit errors/repairs')


if __name__ == '__main__':
    main()
