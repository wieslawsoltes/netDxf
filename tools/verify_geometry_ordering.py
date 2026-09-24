#!/usr/bin/env python3
"""Validate actual geometry comparison observations against a bit-derived order."""
import copy
import json
from pathlib import Path
import struct
import sys

PATTERNS = ('0000000000000000', '8000000000000000', '0000000000000001', '8000000000000001',
            '0000000000000002', '3ff0000000000000', '3ff0000000000001', 'bff0000000000000',
            '7fefffffffffffff', 'ffefffffffffffff', '7ff0000000000000', 'fff0000000000000',
            '7ff8000000000000', '7ff8000000001234', 'fff8000000000000', '7ff0000000000001')
SIGN, MASK = 1 << 63, (1 << 64) - 1


def require(condition, message):
    if not condition:
        raise ValueError(message)


def bits(value):
    return struct.pack('>d', value).hex()


def scalar_key(value):
    # IEEE binary64 encodings sorted as signed magnitude, without floating arithmetic.
    number = int(value, 16)
    magnitude = number & (SIGN - 1)
    if magnitude > 0x7ff0000000000000:
        return (0, 0)  # Double.CompareTo places the single NaN equality class first.
    if magnitude == 0:
        number = 0  # Both signed zeros belong to one equality class.
    return (1, (~number & MASK) if number & SIGN else number | SIGN)


def specifications():
    result = {}
    for prefix in (0, 3):
        for a, x in enumerate(PATTERNS):
            for b, y in enumerate(PATTERNS):
                head = [bits(17.)] * prefix
                result[f'bits/{prefix}/{a}/{b}'] = (head + [x, bits(5.)], head + [y, bits(-5.)])
    for size in (0, 1, 2, 3, 7, 16):
        for seed in range(4):
            a = [float((i * 17 + seed * 13) % 29 - 14) for i in range(size)]
            b = list(a)
            if size and seed:
                b[(seed - 1) % size] += -.125 if seed % 2 == 0 else .125
            result[f'finite/{size}/{seed}'] = ([bits(x) for x in a], [bits(x) for x in b])
    result['crossed'] = ([bits(1.), bits(100.)], [bits(2.), bits(-100.)])
    result['last'] = ([bits(1.), bits(2.), bits(-1.)], [bits(1.), bits(2.), bits(1.)])
    result['zero-tie'] = ([bits(0.), bits(-0.)], [bits(-0.), bits(0.)])
    # System.Double.NaN is a negative quiet NaN on the qualified .NET targets.
    # Accept its payload in this one symbolic input; all other corpus bits are exact.
    result['nan-tie'] = ([None, bits(0.)], ['7ff8000000001234', bits(-0.)])
    require(len(result) == 540, 'Specification inventory changed')
    return result


def flags(a, b):
    x, y = tuple(map(scalar_key, a)), tuple(map(scalar_key, b))
    return [x < y, x <= y, x > y, x >= y, x == y, x != y]


def check(record, expected):
    fields = {'name', 'left', 'right', 'vector', 'reverse', 'row', 'column', 'unchanged'}
    require(type(record) is dict and set(record) == fields, 'Record schema')
    require(type(record['name']) is str and record['name'] in expected, 'Unknown case')
    for key, wanted in zip(('left', 'right'), expected[record['name']]):
        actual = record[key]
        require(type(actual) is list and len(actual) == len(wanted), 'Input length')
        for value, original in zip(actual, wanted):
            require(type(value) is str and len(value) == 16 and all(c in '0123456789abcdef' for c in value), 'Binary64 encoding')
            require(value == original if original is not None else scalar_key(value) == (0, 0), 'Input corpus changed')
    for key in ('vector', 'reverse', 'row', 'column'):
        result = record[key]
        require(type(result) is list and len(result) == 6 and all(type(x) is bool for x in result), 'Boolean relation schema')
        a, b = record['left'], record['right']
        require(result == (flags(b, a) if key == 'reverse' else flags(a, b)), 'Wrong lexicographic/equality result: ' + key)
    require(record['unchanged'] is True, 'Source mutation')


def inventory(records, expected):
    require(type(records) is list and len(records) == len(expected), 'Incomplete/extra observations')
    require(all(type(r) is dict and type(r.get('name')) is str for r in records), 'Observation schema')
    require(len({r['name'] for r in records}) == len(records) and {r['name'] for r in records} == set(expected), 'Duplicate/missing case')
    for record in records:
        check(record, expected)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON key')
        result[key] = value
    return result


def reject(action):
    try:
        action()
    except (ValueError, KeyError, TypeError):
        return 1
    raise ValueError('Corruption was accepted')


def main(directory):
    expected = specifications()
    records = json.loads((directory / 'geometry-order.json').read_text(), object_pairs_hook=unique_object)
    inventory(records, expected)
    controls = 0
    for record in records:
        for field in ('vector', 'reverse', 'row', 'column'):
            for position in range(6):
                bad = copy.deepcopy(record)
                bad[field][position] = not bad[field][position]
                controls += reject(lambda: check(bad, expected))
        for field in record:
            bad = copy.deepcopy(record); del bad[field]
            controls += reject(lambda: check(bad, expected))
        bad = copy.deepcopy(record); bad['unchanged'] = False
        controls += reject(lambda: check(bad, expected))
        bad = copy.deepcopy(record); bad['left'].append(bits(0.))
        controls += reject(lambda: check(bad, expected))
    controls += reject(lambda: inventory(records[:-1], expected))
    controls += reject(lambda: inventory(records + records[:1], expected))
    controls += reject(lambda: inventory(records[1:] + records[1:2], expected))
    controls += reject(lambda: json.loads('{"name":1,"name":2}', object_pairs_hook=unique_object))
    print(f'PASS: {len(records)} exact vector/matrix comparison observations; {controls} corruption/schema/inventory controls rejected. '
          'Same-size value ordering is qualified, not native CAD or every GTE operation.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_geometry_ordering.py ARTIFACTS')
    main(Path(sys.argv[1]))
