#!/usr/bin/env python3
"""Verify real GTE equality observations using an independent binary64 model."""
import copy
import itertools
import json
from pathlib import Path
import sys

from verify_raw_line_geometry import require, reject

BITS = (
    '0000000000000000', '8000000000000000', '0000000000000001', '8000000000000001',
    '0000000000000002', '3ff0000000000000', '3ff0000000000001', 'bff0000000000000',
    '7fefffffffffffff', 'ffefffffffffffff', '7ff0000000000000', 'fff0000000000000',
    '7ff8000000000000', '7ff8000000001234', 'fff8000000000000', '7ff0000000000001',
)
IDENTITIES = set(itertools.product(('matrix', 'vector'), BITS, BITS))
BOOLS = ('eq', 'neq', 'typed', 'boxed', 'reverse', 'defaults')
FIELDS = {'kind', 'left', 'right', *BOOLS, 'leftHash', 'rightHash', 'members'}


def equivalent(left, right):
    # Double.Equals partitions NaN payloads and signed zeros into equivalence
    # classes. Every other finite/infinite binary64 representation is unique.
    x, y = int(left, 16), int(right, 16)
    magnitude = (1 << 63) - 1
    nan = lambda z: (z & 0x7ff0000000000000) == 0x7ff0000000000000 and z & 0xfffffffffffff != 0
    return x == y or (x & magnitude == 0 and y & magnitude == 0) or (nan(x) and nan(y))


def check_record(row):
    require(type(row) is dict and set(row) == FIELDS, 'Missing or unexpected observation fields')
    require((row['kind'], row['left'], row['right']) in IDENTITIES, 'Unexpected input identity')
    wanted = equivalent(row['left'], row['right'])
    for field in BOOLS:
        require(type(row[field]) is bool, 'Non-Boolean comparison result')
        require(row[field] == (not wanted if field == 'neq' else wanted), 'Incorrect comparison ' + field)
    for field in ('leftHash', 'rightHash'):
        require(type(row[field]) is int and -(1 << 31) <= row[field] < (1 << 31), 'Invalid runtime hash')
    # Hash equality is mandatory only for equal objects. Collisions between
    # unequal objects are legal; hash values are not cross-runtime identifiers.
    if wanted:
        require(row['leftHash'] == row['rightHash'], 'Equal values with different hashes')
    require(type(row['members']) is int and row['members'] == (1 if wanted else 2), 'HashSet cardinality')


def check(rows):
    require(type(rows) is list and len(rows) == len(IDENTITIES), 'Incomplete equality corpus')
    found = set()
    for row in rows:
        check_record(row)
        identity = (row['kind'], row['left'], row['right'])
        require(identity not in found, 'Duplicate observation')
        found.add(identity)
    require(found == IDENTITIES, 'Missing identities')


def unique_object(pairs):
    output = {}
    for key, value in pairs:
        require(key not in output, 'Duplicate JSON field')
        output[key] = value
    return output


def main(directory):
    path = directory / 'geometry-equality.json'
    rows = json.loads(path.read_text(encoding='utf-8'), object_pairs_hook=unique_object)
    check(rows)
    controls = 0
    for row in rows:
        for field in BOOLS:
            changed = dict(row); changed[field] = not changed[field]
            controls += reject(lambda: check_record(changed))
        for field in ('members', 'left', 'kind'):
            changed = dict(row); changed[field] = 99 if field == 'members' else 'invalid'
            controls += reject(lambda: check_record(changed))
        if equivalent(row['left'], row['right']):
            changed = dict(row); changed['leftHash'] ^= 1
            controls += reject(lambda: check_record(changed))
    controls += reject(lambda: check(rows[:-1]))
    controls += reject(lambda: check(rows + [rows[0]]))
    duplicated = copy.deepcopy(rows); duplicated[-1] = duplicated[0]
    controls += reject(lambda: check(duplicated))
    controls += reject(lambda: json.loads('{"eq":true,"eq":false}', object_pairs_hook=unique_object))
    print(f'PASS: {len(rows)} independently checked vector/matrix equality observations; '
          f'{controls} altered result, hash, identity and JSON/inventory controls rejected. '
          'This checks value equality and hashing contracts, not native CAD acceptance or ordering operators.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_geometry_value_equality.py ARTIFACTS')
    main(Path(sys.argv[1]))
