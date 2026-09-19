#!/usr/bin/env python3
"""Check degree elevation by exact Bernstein-system reconstruction of the SOURCE.

The oracle does not use the production degree-elevation recurrence. It solves
for target-degree homogeneous Bernstein coefficients directly, then verifies
one-sided source/result geometry and complete selected physical SPLINE packets.
"""
from __future__ import annotations
import argparse
from fractions import Fraction as F
import json
import math
from pathlib import Path
import ezdxf
from spline_degree_oracle import (source_case, polynomial_segments, spans, evaluate, output_packet,
    packet_controls, coefficients, packets, geometry, one, require, rejects, check_record, VERSIONS)


def elevated(source, times):
    p = source['degree']; q = p + times
    parts = polynomial_segments(source, q)
    result = dict(degree=q, points=[], weights=[], knots=[])
    for i, part in enumerate(parts):
        lo = part['knots'][0]
        first = i == 0 or source['knots'].count(lo) == p + 1
        if not first:
            require(result['points'][-1] == part['points'][0] and result['weights'][-1] == part['weights'][0], 'Oracle continuous endpoints differ')
        result['points'].extend(part['points'][0 if first else 1:])
        result['weights'].extend(part['weights'][0 if first else 1:])
        result['knots'].extend([lo] * (q + 1 if first else q))
    result['knots'].extend([parts[-1]['knots'][-1]] * (q + 1))
    return result


def check_shape(source, result):
    scale = max(abs(v) for point in source['points'] for v in point)
    tolerance = F(max(math.ldexp(1.0, -1074), scale * 4e-14))
    samples = 0
    for span in spans(source):
        lo, hi = map(F, source['knots'][span:span + 2])
        for j in range(9):
            u = lo + (hi - lo) * F(j, 8)
            a = evaluate(source, u, left_limit=j == 8); b = evaluate(result, u, left_limit=j == 8)
            require(all(abs(x - y) <= tolerance for x, y in zip(a, b)), 'Degree elevation changed one-sided source shape')
            samples += 1
    return samples


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory', type=Path)
    root = parser.parse_args().directory
    names = {f'spline-degree-wire-{d}-{k}-2-{v}-{b}.dxf' for d in (2, 3) for k in (0, 8, 9)
             for v in VERSIONS for b in (False, True)} | {'spline-degree-numerics.json'}
    actual_names = {p.name for p in root.glob('spline-degree-*')}
    def inventory(value):
        require(value == names, 'Missing or extra elevation fixture')
    inventory(actual_names)
    inventory_controls = rejects(inventory, actual_names - {'spline-degree-numerics.json'})
    inventory_controls += rejects(inventory, actual_names | {'spline-degree-extra.dxf'})
    rows = json.loads((root / 'spline-degree-numerics.json').read_text())
    cases = [(d, k, t) for d in range(1, 10) for k in range(10) for t in dict.fromkeys((1, 10 - d))]
    require([(r['degree'], r['kind'], r['times']) for r in rows] == cases, 'Elevation numerical corpus differs')
    numerical_controls = samples = 0
    for row in rows:
        source = source_case(row['degree'], row['kind']); coefficients(row['source'], source)
        expected = elevated(source, row['times']); actual = row['result']; coefficients(actual, expected)
        samples += check_shape(source, actual)
        for key in ('points', 'weights', 'knots'):
            changed = json.loads(json.dumps(actual))
            if key == 'points':
                changed[key][1][0] = math.nextafter(changed[key][1][0], math.inf)
            else:
                changed[key][1] = math.nextafter(changed[key][1], math.inf)
            numerical_controls += rejects(lambda value: coefficients(value, expected), changed)
    drawings = corruptions = 0
    for degree in (2, 3):
        for kind in (0, 8, 9):
            source = source_case(degree, kind); expected = elevated(source, 2)
            for version, profile in VERSIONS.items():
                for binary in (False, True):
                    path = root / f'spline-degree-wire-{degree}-{kind}-2-{version}-{binary}.dxf'
                    records = packets(path, binary); require(len(records) == 2, 'Elevation wire inventory differs')
                    before, actual = records; coefficients(geometry(before), source)
                    packet = output_packet(before, expected); handle, owner = one(before, 5), one(before, 330)
                    check_record(actual, packet, handle, owner)
                    corruptions += packet_controls(actual, packet, handle, owner)
                    doc = ezdxf.readfile(path); require(doc.dxfversion == profile, 'Version changed')
                    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Graph errors or repairs')
                    drawings += 1
    print(f'PASS degree elevation: {len(rows)} target-degree polynomial systems / {samples} one-sided curve samples; '
          f'{drawings} drawings; {numerical_controls} numerical, {corruptions} packet and {inventory_controls} inventory corruptions rejected; zero audit errors/repairs')


if __name__ == '__main__':
    main()
