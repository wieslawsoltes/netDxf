#!/usr/bin/env python3
"""Check scalar formatting independently with Decimal and complete DXF records.

Decimal.from_float captures each exact IEEE value; ROUND_HALF_UP is an
independent implementation of the declared midpoint-away rounding contract.
Native fixture comparisons qualify the recorded currency/percentage examples,
not every AutoCAD formatting option or its runtime behavior.
"""
import argparse
import copy
from decimal import Decimal, localcontext, ROUND_HALF_UP
import gzip
import hashlib
import json
from math import gcd, isfinite
from pathlib import Path
import re
import struct
import tempfile
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata, FILES
from verify_mleader_inputs import check

BIT_INVENTORY = "9aebb135e22def6ecb41201134568ca20ee18bd7494185c301b901410e0c0740"
PROFILES = {2007: "AC1021", 2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}


def expected_numeric(bits, mode, precision):
    number = struct.unpack('>d', bytes.fromhex(bits))[0]
    check(isfinite(number), "Numeric oracle requires a finite IEEE value")
    with localcontext() as context:
        context.prec = 1200  # More than the full binary64 subnormal expansion.
        value = Decimal.from_float(abs(number))
        quantum = Decimal(1).scaleb(-precision)
        if mode == 1:
            exponent = value.adjusted() if value else 0
            mantissa = value.scaleb(-exponent).quantize(quantum, rounding=ROUND_HALF_UP)
            if mantissa >= 10:
                mantissa /= 10
                exponent += 1
            magnitude = mantissa
            text = f"{mantissa:.{precision}f}E{exponent:+03d}"
        elif mode in (2, 3):
            magnitude = value.quantize(quantum, rounding=ROUND_HALF_UP)
            if mode == 2:
                text = f"{magnitude:.{precision}f}"
            else:
                feet = int(magnitude // 12)
                inches = magnitude - feet * 12
                text = f"{feet}'-{inches:.{precision}f}\""
        else:
            denominator = 2 ** precision
            magnitude = (value * denominator).to_integral_value(rounding=ROUND_HALF_UP)
            whole, numerator = divmod(int(magnitude), denominator)
            common = gcd(numerator, denominator)
            fraction = f"{numerator // common}/{denominator // common}" if numerator else ""
            if mode == 4:
                feet, inches = divmod(whole, 12)
                text = f"{feet}'-{inches}" + (" " + fraction if fraction else "") + '"'
            else:
                text = fraction if whole == 0 and fraction else str(whole) + (" " + fraction if fraction else "")
        return ("-" if number < 0 and magnitude else "") + text



def validate_numeric(row):
    match = re.fullmatch(r'%lu([1-5])%pr([0-8])', row['expression'])
    check(match is not None, "Unsupported numeric matrix expression")
    check(row['actual'] == expected_numeric(row['bits'], *map(int, match.groups())), "Incorrect exact numeric output")


def verify_matrix(rows):
    check(len(rows) == 4500, "Expected all 4500 numeric results")
    keys = set()
    bits = set()
    for row in rows:
        check(set(row) == {"bits", "expression", "actual"}, "Unexpected matrix fields")
        check(re.fullmatch('[0-9A-F]{16}', row['bits']) is not None, "Wrong IEEE bit spelling")
        match = re.fullmatch(r'%lu([1-5])%pr([0-8])', row['expression'])
        check(match is not None, "Unexpected numeric matrix expression")
        key = row['bits'], row['expression']
        check(key not in keys, "Repeated numeric matrix case")
        keys.add(key)
        bits.add(row['bits'])
        validate_numeric(row)
    check(len(bits) == 100 and hashlib.sha256(('\n'.join(sorted(bits)) + '\n').encode()).hexdigest() == BIT_INVENTORY,
          "Seeded IEEE edge/random inventory changed")
    check(len(keys) == len(bits) * 45, "Incomplete format/precision cross product")


def display_positions(tags):
    return [i for i in range(len(tags) - 1) if tags[i][0] == 302 and tags[i + 1] == [304, 'ACVALUE_END']]


def refresh_expected(before):
    wanted = normalize_save_metadata(before)
    contents = [(key, tags) for key, tags in wanted.items() if tags[0] == [0, 'TABLECONTENT']]
    check(len(contents) == 1, "Expected one synthetic content object")
    key, tags = contents[0]
    indices = display_positions(tags)
    check(len(indices) == 4, "Expected four synthetic scalar frames")
    expected = ('$-2147483648.00', '0', 'text')
    for index, value in zip(indices, expected):
        check(tags[index] == [302, 'display'], "Source synthetic display changed")
        tags[index] = [302, value]
    return wanted, key


def exact_records(wanted, actual):
    check(list(wanted) == list(actual), "Physical record inventory/order changed")
    check(wanted == actual, "Unexpected stored record changed during display refresh")


def check_refresh(before, after):
    wanted, key = refresh_expected(before)
    actual = normalize_save_metadata(after)
    exact_records(wanted, actual)
    controls = 0
    for index in range(len(actual[key])):
        changed = dict(actual)
        changed[key] = copy.deepcopy(actual[key])
        value = changed[key][index][1]
        changed[key][index][1] = value + '_CORRUPT' if isinstance(value, str) else [999] if isinstance(value, list) else value + 1
        try:
            exact_records(wanted, changed)
        except ValueError:
            controls += 1
        else:
            raise AssertionError('Corrupted content field escaped the refresh gate')
    for other in actual:
        if other == key:
            continue
        changed = dict(actual)
        changed[other] = actual[other] + [[999, 'CORRUPT']]
        try:
            exact_records(wanted, changed)
        except ValueError:
            controls += 1
        else:
            raise AssertionError('Unrelated record corruption escaped the refresh gate')
    return controls


def native_values(items):
    result = {}
    for handle, tags in items.items():
        if tags[0] != [0, 'TABLECONTENT']:
            continue
        for index in display_positions(tags):
            expression = tags[index - 1]
            if expression[0] == 300 and isinstance(expression[1], str) and expression[1].startswith('%lu2%pr2'):
                check(tags[index - 2][0] == 94, 'Native unit field missing')
                result[handle, index] = tags[index - 3:index + 2]
    return result


def expected_inventory():
    return {f'value-format-{phase}-AutoCad{year}-{binary}.dxf' for year in PROFILES for binary in (False, True) for phase in ('before', 'after')} | {
        f'value-format-native-{file}-{binary}.dxf' for file in FILES if 'AC1018' not in file for binary in (False, True)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    check({p.name for p in args.directory.glob('value-format-*.dxf')} == expected_inventory(), 'Exact value-format drawing inventory required')
    rows = json.loads((args.directory / 'value-format-matrix.json').read_text(encoding='utf-8'))
    verify_matrix(rows)
    # Every actual numeric result must differ from a deliberately corrupted value.
    controls = 0
    for row in rows:
        changed = dict(row, actual=row['actual'] + '_CORRUPT')
        try:
            validate_numeric(changed)
        except ValueError:
            controls += 1
        else:
            raise AssertionError('Numeric corruption escaped the same validator')
    for year, profile in PROFILES.items():
        for binary in (False, True):
            paths = [args.directory / f'value-format-{phase}-AutoCad{year}-{binary}.dxf' for phase in ('before', 'after')]
            for path in paths:
                check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
                check(ezdxf.readfile(path).dxfversion == profile, 'Profile changed')
            controls += check_refresh(records(paths[0]), records(paths[1]))
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / 'tools/table_oracle/fixtures.json').read_text(encoding='utf-8'))['files']
    native_count = 0
    with tempfile.TemporaryDirectory() as directory:
        for item in manifest:
            if 'AC1018' in item['file']:
                continue
            raw = gzip.decompress((root / 'tests/fixtures/table-oracle' / (item['file'] + '.gz')).read_bytes())
            check(hashlib.sha256(raw).hexdigest() == item['sha256'], 'Native source hash changed')
            original_path = Path(directory) / item['file']
            original_path.write_bytes(raw)
            original = native_values(records(original_path))
            for binary in (False, True):
                path = args.directory / f"value-format-native-{item['file']}-{binary}.dxf"
                check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Native transport changed')
                check(ezdxf.readfile(path).dxfversion == item['profile'], 'Native profile changed')
                actual = native_values(records(path))
                # Normalized writer fields can shift offsets; preserve the physical handle and ordered numeric packets.
                normalize = lambda values: [(key[0], value) for key, value in values.items()]
                check(normalize(original) == normalize(actual), 'Native numeric value/format/display changed')
                for packet in actual.values():
                    scalar, units, expression, display, end = packet
                    check(scalar[0] in (91, 140), 'Native scalar kind changed')
                    wanted = f'{scalar[1]:.2f}'
                    if expression[1] == '%lu2%pr2%ps[,%]':
                        check(units == [94, 32], 'Native percentage unit changed')
                        wanted += '%'
                    elif expression[1] == '%lu2%pr2%ps[$,]':
                        check(units == [94, 16], 'Native currency unit changed')
                        wanted = '$' + wanted
                    else:
                        raise ValueError('Unexpected native numerical expression')
                    check(display == [302, wanted], 'Native producer example not reproduced')
                    native_count += 1
    check(native_count >= 8, 'Expected pinned currency and percentage examples in both transports')
    print(f'PASS: 4500 Decimal-checked results, 8 complete-record refresh pairs, {native_count} native numeric examples, {controls} corruption controls; native CAD runtime unqualified')


if __name__ == '__main__':
    main()
