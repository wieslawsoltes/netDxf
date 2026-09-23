#!/usr/bin/env python3
"""Audit real GVector norms using independent 2,000-digit decimal calculations."""
import copy
from decimal import Decimal, localcontext
import json
import math
from pathlib import Path
import struct
import sys


def require(condition, message):
    if not condition: raise AssertionError(message)


def expected_inputs():
    expected = {}
    for exponent in (-1074, -1073, -1068, -1023, -1022, -1000, -500, 0, 100, 500, 1000, 1020):
        for signs in range(4):
            scale = math.ldexp(1.0, exponent)
            expected[f'scaled/{exponent}/{signs}'] = [(3. if signs % 2 == 0 else -3.) * scale,
                                                     (4. if signs < 2 else -4.) * scale, 0.]
    expected.update(empty=[], zero=[0., -0.], **{'overflow-length': [sys.float_info.max] * 2,
                                               'mixed': [1e300, -1e-300, 1e299, 0.]})
    return expected


def close(actual, wanted, label):
    if math.isinf(wanted):
        require(actual == wanted, label + ': overflow classification')
    else:
        require(math.isfinite(actual), label + ': nonfinite result')
        require((actual == 0.) == (wanted == 0.), label + ': zero/nonzero classification')
        if wanted != 0.: require((actual > 0) == (wanted > 0), label + ': sign')
        # A stated 32-ULP envelope covers double rounding in component scaling,
        # sum-of-squares, sqrt and rescaling; no geometric/global epsilon is used.
        require(abs(actual - wanted) <= 32 * max(math.ulp(wanted), math.ulp(actual)), label + ': numerical mismatch')


def check_record(record, expected):
    require(set(record) == {'name', 'input', 'length', 'normalized'}, 'Record fields')
    wanted = expected[record['name']]
    values = [float(v) for v in record['input']]
    require(len(values) == len(wanted) and
            all(struct.pack('>d', a) == struct.pack('>d', b) for a, b in zip(values, wanted)), 'Input bits differ')
    normalized = [float(v) for v in record['normalized']]
    require(len(normalized) == len(wanted), 'Normalized dimension')
    with localcontext() as context:
        context.prec = 2000
        decimals = [Decimal.from_float(v) for v in wanted]
        norm = sum((v * v for v in decimals), Decimal(0)).sqrt()
        close(float(record['length']), float(norm), 'Norm')
        for actual, value in zip(normalized, decimals):
            close(actual, float(value / norm) if norm else 0., 'Component')


def check(records):
    expected = expected_inputs()
    require(isinstance(records, list), 'Expected record list')
    names = [record['name'] for record in records]
    require(len(names) == len(set(names)) and set(names) == set(expected), 'Incomplete/duplicate/extra norm inventory')
    for record in records: check_record(record, expected)


def reject(function):
    try: function()
    except (AssertionError, ValueError, KeyError, TypeError): return 1
    raise AssertionError('Corrupted norm observation was accepted')


def main(directory):
    records = json.loads((directory / 'gvector-norms.json').read_text())
    check(records)
    expected = expected_inputs(); controls = 0
    for original in records:
        bad = copy.deepcopy(original); bad['length'] = '1' if float(bad['length']) == 0 else '0'
        controls += reject(lambda: check_record(bad, expected))
        if original['normalized']:
            bad = copy.deepcopy(original); bad['normalized'][0] = '999'
            controls += reject(lambda: check_record(bad, expected))
            bad = copy.deepcopy(original); bad['input'][0] = '123'
            controls += reject(lambda: check_record(bad, expected))
    controls += reject(lambda: check(records[:-1]))
    controls += reject(lambda: check(records + [records[0]]))
    extra = copy.deepcopy(records[0]); extra['name'] = 'unexpected'
    controls += reject(lambda: check(records + [extra]))
    print(f'PASS: {len(records)} independently specified GVector norms; 2,000-digit decimal reference; '
          f'{controls} actual-observation/inventory corruptions rejected. This is geometry-kernel, not native AutoCAD execution evidence.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_gvector_norms.py ARTIFACTS')
    main(Path(sys.argv[1]))
