#!/usr/bin/env python3
"""Check bounds from actual GVector executions against independent stable extrema."""
import copy
import json
import math
from pathlib import Path
import struct
import sys


def require(condition, message):
    if not condition:
        raise ValueError(message)


def hex64(value):
    return struct.pack('>d', value).hex()


def decode(value):
    return struct.unpack('>d', bytes.fromhex(value))[0]


def specifications():
    result = {}
    for size in (0, 1, 2, 3, 7, 16):
        for count in (1, 2, 5):
            for seed in range(3):
                result[f'finite/{size}/{count}/{seed}'] = [
                    [hex64(float(((r + 3) * (c + 5) * 17 + seed * 11) % 43 - 21)
                        + ((r + c) % 4) * .125) for c in range(size)] for r in range(count)]
    points = ((3., 5.), (-2., 7.), (9., -4.))
    orders = ((0, 1, 2), (0, 2, 1), (1, 0, 2), (1, 2, 0), (2, 0, 1), (2, 1, 0))
    for i, order in enumerate(orders):
        result[f'permutation/{i}'] = [[hex64(v) for v in points[j]] for j in order]
    result['shared'] = [[hex64(2.), hex64(-3.)] for _ in range(3)]
    patterns = ('fff0000000000000', 'ffefffffffffffff', '8000000000000001', '8000000000000000',
                '0000000000000000', '0000000000000001', '7fefffffffffffff', '7ff0000000000000',
                '7ff8000000000001', 'fff8000000000042')
    for a, x in enumerate(patterns):
        for b, y in enumerate(patterns):
            result[f'binary64/{a}/{b}'] = [[x, y], [y, x]]
    require(len(result) == 161, 'Specification inventory changed')
    return result


FLAGS = ('sourceUnchanged', 'objectsDetached', 'buffersDetached', 'repeat', 'isolation')
FIELDS = {'name', 'count', 'input', 'minimum', 'maximum', *FLAGS}


def check_record(record, expected):
    require(type(record) is dict and set(record) == FIELDS, 'Record schema')
    name = record['name']
    require(type(name) is str and name in expected, 'Unexpected observation')
    rows = expected[name]
    require(type(record['count']) is int and record['count'] == len(rows), 'Requested prefix count')
    require(record['input'] == rows, 'Input corpus differs from fixed specification')
    for flag in FLAGS:
        require(record[flag] is True, 'Failed identity/state assertion: ' + flag)
    low, high = [], []
    for column in zip(*rows):
        if math.isnan(decode(column[0])):
            low.append(column[0]); high.append(column[0])
        else:
            candidates = [value for value in column if not math.isnan(decode(value))]
            # Python min/max retain the first equal value, including signed-zero ties.
            low.append(min(candidates, key=decode)); high.append(max(candidates, key=decode))
    require(record['minimum'] == low and record['maximum'] == high, 'Incorrect exact extrema')
    return record


def check_document(document, expected):
    require(type(document) is dict and set(document) == {'schema', 'observations'}, 'Document schema')
    require(type(document['schema']) is int and document['schema'] == 1, 'Schema version')
    records = document['observations']
    require(type(records) is list and len(records) == len(expected), 'Observation count')
    names = [check_record(record, expected)['name'] for record in records]
    require(len(set(names)) == len(names) and set(names) == set(expected), 'Missing/duplicate observations')


def reject(call):
    try:
        call()
    except (ValueError, KeyError, TypeError, IndexError):
        return 1
    raise AssertionError('Corruption was accepted')


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON key')
        result[key] = value
    return result


def main(directory):
    path = directory / 'gvector-extremes.json'
    require({p.name for p in directory.glob('gvector-extremes*.json')} == {path.name}, 'Missing/extra bounds reports')
    require(path.stat().st_size <= 2_000_000, 'Bounds report too large')
    expected = specifications()
    document = json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique_object)
    check_document(document, expected)
    corruptions = 0
    for record in document['observations']:
        for flag in FLAGS:
            bad = copy.deepcopy(record); bad[flag] = False
            corruptions += reject(lambda: check_record(bad, expected))
        bad = copy.deepcopy(record); bad['count'] += 1
        corruptions += reject(lambda: check_record(bad, expected))
        for field in ('minimum', 'maximum'):
            bad = copy.deepcopy(record); bad[field].append('0000000000000000')
            corruptions += reject(lambda: check_record(bad, expected))
            for i in range(len(record[field])):
                bad = copy.deepcopy(record)
                bad[field][i] = f'{int(bad[field][i], 16) ^ 1:016x}'
                corruptions += reject(lambda: check_record(bad, expected))
        if record['input'][0]:
            bad = copy.deepcopy(record); bad['input'][0][0] = 'invalid'
            corruptions += reject(lambda: check_record(bad, expected))
    for mode in ('remove', 'append', 'duplicate'):
        bad = copy.deepcopy(document)
        if mode == 'remove': bad['observations'].pop()
        elif mode == 'append': bad['observations'].append(copy.deepcopy(bad['observations'][0]))
        else: bad['observations'][-1] = copy.deepcopy(bad['observations'][0])
        corruptions += reject(lambda: check_document(bad, expected))
    corruptions += reject(lambda: json.loads('{"schema":1,"schema":1}', object_pairs_hook=unique_object))
    print(f'PASS: {len(expected)} actual GVector bounds observations; exact coordinate bits, source/alias checks; '
          f'{corruptions} corrupted observations/inventory/JSON controls rejected. Geometry bounds only; no native AutoCAD claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_gvector_extremes.py ARTIFACTS')
    main(Path(sys.argv[1]))
