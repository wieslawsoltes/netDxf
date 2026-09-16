#!/usr/bin/env python3
"""Compare the frozen helper corpus with exact Fraction binary64 results."""
import argparse
from fractions import Fraction
from functools import lru_cache
import gzip
import json
from pathlib import Path
import struct


def read(path):
    data = path.read_bytes()
    return json.loads(gzip.decompress(data) if path.suffix == '.gz' else data)


def bits(value):
    return struct.pack('>d', value).hex().upper()


def expected(item):
    if item['kind'] == 'ratio':
        return bits(float(Fraction(int(item['numerator']), int(item['denominator']))))
    knots = list(map(Fraction.from_float, item['knots']))
    parameter = Fraction.from_float(item['parameter'])

    @lru_cache(None)
    def basis(index, degree):
        if degree == 0:
            return Fraction(int(knots[index] <= parameter < knots[index + 1]))
        return ((parameter - knots[index]) / (knots[index + degree] - knots[index]) * basis(index, degree - 1)
                + (knots[index + degree + 1] - parameter) / (knots[index + degree + 1] - knots[index + 1]) * basis(index + 1, degree - 1))

    first, degree = item['first'], item['degree']
    indices = range(first, first + degree + 1)
    factors = [basis(index, degree) * Fraction.from_float(item['weights'][index]) for index in indices]
    denominator = sum(factors)
    return [bits(float(sum(factor * Fraction.from_float(item['controls'][first + index][axis])
                           for index, factor in enumerate(factors)) / denominator)) for axis in range(3)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('cases', type=Path)
    parser.add_argument('outputs', type=Path)
    args = parser.parse_args()
    cases, outputs = read(args.cases), read(args.outputs)
    actual = {item['name']: item for item in outputs}
    if len(cases) != 403 or len(actual) != len(outputs) or set(actual) != {item['name'] for item in cases}:
        raise ValueError('Frozen exact-helper case inventory changed')
    failed = []
    for item in cases:
        wanted = expected(item)
        if actual[item['name']].get('bits') != wanted:
            failed.append({'name': item['name'], 'expected': wanted, 'actual': actual[item['name']]})
    print(json.dumps({'passed': not failed, 'cases': len(cases), 'bit_exact_matches': len(cases) - len(failed), 'failures': failed}))
    if failed:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
