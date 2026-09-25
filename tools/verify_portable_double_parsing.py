#!/usr/bin/env python3
"""Independently check exported binary64 parser observations, not writer round trips."""
import copy
from fractions import Fraction
import json
import math
from pathlib import Path
import re
import struct
import sys


def require(condition, message):
    if not condition:
        raise ValueError(message)


def binary_value(bits):
    exponent, fraction = bits >> 52, bits & ((1 << 52) - 1)
    if exponent == 0:
        return Fraction(fraction, 1 << 1074)
    # The successor of max-finite is the conceptual 2**1024 rounding boundary.
    mantissa = (1 << 52) + fraction
    power = exponent - 1075
    return Fraction(mantissa << power) if power >= 0 else Fraction(mantissa, 1 << -power)


def boundary(name, lower, tail):
    midpoint = (binary_value(lower) + binary_value(lower + 1)) / 2
    places = midpoint.denominator.bit_length() - 1
    coefficient = midpoint.numerator * 5 ** places * 10 ** tail
    for delta in (-1, 0, 1):
        rounded = lower if delta < 0 or (delta == 0 and lower % 2 == 0) else lower + 1
        token = f'{coefficient + delta}e{-places-tail}'
        for sign in (0, 1):
            yield (f'{name}/{delta}/{sign}', ('-' if sign else '') + token,
                   None if rounded == 0x7ff0000000000000 else f'{rounded | sign << 63:016x}')


def specifications():
    rows = []
    for exponent in range(2047):
        rows.extend(boundary(f'exponent/{exponent}', exponent << 52 | exponent * 0x1f123bb5, 1))
    edges = (0, 1, 2, 0xffffffffffffe, 0xfffffffffffff, 0x10000000000000,
             0x3fefffffffffffff, 0x3ff0000000000000, 0x3ff0000000000001,
             0x7feffffffffffffe, 0x7fefffffffffffff)
    for i, lower in enumerate(edges):
        rows.extend(boundary(f'edge/{i}', lower, 1))
    for i in (0, 4, 7, 10):
        rows.extend(boundary(f'tail/{i}', edges[i], 1301))
    valid = ('0', '-0', '+0.0', ' \t-00.000e+23\t ', '.5', '1.', '  +1.5e+0\t',
             '4.9406564584124654e-324', '-1e-99999999999999999999', '1.7976931348623157e308',
             '9007199254740993', '3.1415926535897931', '0e' + '9' * 4000, '0.' + '0' * 99999 + '1e100000')
    for i, token in enumerate(valid):
        rows.append((f'literal/{i}', token, struct.pack('>d', float(token)).hex()))
    invalid = ('', ' ', '+', '-', '.', 'e1', '1e', '1e+', '--0', '1.2.3', '1,000', '0x1',
               'NaN', 'Infinity', '-Infinity', '1e309', '1e99999999999999999999', '1 2', '1e2x',
               '-0\0', '\u00a01', '1\u00a0', '\u0661', '1\u2003',
               '0e' + '9' * 4000 + 'x', '0.' + '0' * 99999 + 'x')
    rows.extend((f'invalid/{i}', token, None) for i, token in enumerate(invalid))
    require(len(rows) == 12412, 'Specification count')
    result = {name: (token, bits) for name, token, bits in rows}
    require(len(result) == len(rows), 'Duplicate specification')
    # Different implementation: CPython's decimal-to-binary converter checks the
    # integer-derived answers, with explicit DXF lexical and finite admission.
    grammar = re.compile(r'[ \t\v\f\r\n]*[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?[ \t\v\f\r\n]*\Z')
    for name, (token, expected) in result.items():
        value = float(token) if grammar.fullmatch(token) else float('nan')
        actual = struct.pack('>d', value).hex() if math.isfinite(value) else None
        require(actual == expected, 'Independent oracle mismatch: ' + name)
    return result


FIELDS = {'id', 'token', 'portableBits', 'selectedBits', 'codecBits'}


def check_record(record, expected):
    require(type(record) is dict and set(record) == FIELDS, 'Observation schema')
    name = record['id']
    require(type(name) is str and name in expected, 'Unknown observation')
    token, bits = expected[name]
    require(record['token'] == token, 'Input differs: ' + name)
    for field in ('portableBits', 'selectedBits', 'codecBits'):
        require(record[field] == bits, 'Incorrect result: ' + name + '/' + field)


def check_document(document, expected):
    require(type(document) is dict and set(document) == {'schema', 'observations'}, 'Document schema')
    require(type(document['schema']) is int and document['schema'] == 1, 'Schema version')
    rows = document['observations']
    require(type(rows) is list and len(rows) == len(expected), 'Observation count')
    for row in rows:
        check_record(row, expected)
    names = [row['id'] for row in rows]
    require(len(set(names)) == len(names) and set(names) == set(expected), 'Missing/duplicate observations')


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON key')
        result[key] = value
    return result


def reject(call):
    try:
        call()
    except (ValueError, TypeError, KeyError, IndexError):
        return 1
    raise ValueError('Corrupted evidence accepted')


def controls(document, expected):
    count = 0
    for record in document['observations']:
        for field in ('portableBits', 'selectedBits', 'codecBits'):
            bad = dict(record)
            bad[field] = '0000000000000000' if record[field] is None else f'{int(record[field], 16) ^ 1:016x}'
            count += reject(lambda: check_record(bad, expected))
        bad = dict(record); bad['token'] += '0'
        count += reject(lambda: check_record(bad, expected))
        bad = dict(record); bad['id'] += '/unexpected'
        count += reject(lambda: check_record(bad, expected))
    for mode in ('remove', 'append', 'duplicate'):
        bad = copy.deepcopy(document)
        if mode == 'remove': bad['observations'].pop()
        elif mode == 'append': bad['observations'].append(dict(bad['observations'][0]))
        else: bad['observations'][-1] = dict(bad['observations'][0])
        count += reject(lambda: check_document(bad, expected))
    count += reject(lambda: json.loads('{"schema":1,"schema":1}', object_pairs_hook=unique_object))
    return count


def main(directory):
    path = directory / 'portable-double.json'
    require({p.name for p in directory.glob('portable-double*.json')} == {path.name}, 'Report inventory')
    require(path.stat().st_size <= 32_000_000, 'Report too large')
    expected = specifications()
    document = json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique_object)
    check_document(document, expected)
    count = controls(document, expected)
    print(f'PASS: {len(expected)} actual binary64 input observations, portable/selected/DXF codec bits; '
          f'{count} corruption controls rejected. No native AutoCAD acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_portable_double_parsing.py ARTIFACTS')
    main(Path(sys.argv[1]))
