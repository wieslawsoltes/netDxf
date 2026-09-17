#!/usr/bin/env python3
"""Check finite direction assignment using physical tags and Decimal arithmetic.

The 42 wire inputs and 512 xorshift inputs are generated independently here.
Normalization uses 1,100-digit Decimal square roots, not netDxf's float algorithm.
Numerical admission is four ULP per component plus a 2e-15 unit-length bound;
this does not assert native AutoCAD or universal correctly-rounded equivalence.
"""
from __future__ import annotations
import argparse
from decimal import Decimal, localcontext
import io
import json
import math
from pathlib import Path
import re
import struct
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

PROFILES = {'AutoCad2000': 'AC1015', 'AutoCad2004': 'AC1018', 'AutoCad2007': 'AC1021',
            'AutoCad2010': 'AC1024', 'AutoCad2013': 'AC1027', 'AutoCad2018': 'AC1032'}
EXPONENTS = (-1074, -1022, -1000, -600, -500, -50, 0, 50, 500, 600, 1000, 1021)
KINDS = ('ATTDEF', 'LINE', 'RAY', 'XLINE', 'ATTRIB')
MASK = (1 << 64) - 1


def require(condition, message):
    if not condition:
        raise ValueError(message)


def to_bits(value):
    return struct.pack('>d', value).hex().upper()


def from_bits(value):
    require(isinstance(value, str) and re.fullmatch(r'[0-9A-F]{16}', value), 'Invalid bit string')
    return struct.unpack('>d', bytes.fromhex(value))[0]


def ordered(value):
    bits = int(to_bits(value), 16)
    return (~bits & MASK) if bits >> 63 else bits | (1 << 63)


def normalized(source):
    with localcontext() as context:
        context.prec = 1100
        values = [Decimal.from_float(v) for v in source]
        length = sum(v * v for v in values).sqrt()
        return tuple(float(v / length) for v in values)


def check_vector(actual, expected, signed_zero=False):
    require(len(actual) == 3 and all(math.isfinite(v) for v in actual), 'Nonfinite direction')
    require(abs(math.fsum(v * v for v in actual) - 1) <= 2e-15, 'Non-unit direction')
    for a, e in zip(actual, expected):
        if a == e == 0:
            require(not signed_zero or to_bits(a) == to_bits(e), 'Zero sign changed')
        else:
            require(abs(ordered(a) - ordered(e)) <= 4, 'Direction exceeds four-ULP component bound')


def wire_inputs():
    result = []
    for exponent in EXPONENTS:
        s = math.ldexp(1.0, exponent)
        result.extend(((s, -2 * s, 3 * s), (3 * s, s, -2 * s), (-2 * s, 3 * s, s)))
    maximum, minimum = float.fromhex('0x1.fffffffffffffp1023'), math.ldexp(1.0, -1074)
    result.extend(((maximum, -minimum, 0.), (0., -maximum, maximum / 2), (-0., minimum, -0.),
                   (-minimum, 0., -0.), (0., -0., -maximum), (maximum, maximum, maximum)))
    return result


def random_inputs():
    state = 0x6e65744478664E31
    def next_value():
        nonlocal state
        state ^= (state << 13) & MASK
        state ^= state >> 7
        state ^= (state << 17) & MASK
        bits = state
        if bits & 0x7ff0000000000000 == 0x7ff0000000000000:
            bits ^= 0x0010000000000000
        return f'{bits:016X}'
    return [[next_value() for _ in range(3)] for _ in range(512)]


def selected_records(data):
    loader = (binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else
              ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'))))
    records, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0][1] in KINDS:
                records.append(current)
            current = []
        current.append((tag.code, tag.value))
    if current and current[0][1] in KINDS:
        records.append(current)
    return records


def one(record, code):
    values = [v for c, v in record if c == code]
    require(len(values) == 1, f'Missing/repeated group {code}')
    return values[0]


def triple(record, code):
    return tuple(float(one(record, c)) for c in (code, code + 10, code + 20))


def check_records(records, wanted):
    require([r[0][1] for r in records] == list(KINDS), 'Selected physical record inventory differs')
    for record in records:
        kind = record[0][1]
        check_vector(triple(record, 11 if kind in ('RAY', 'XLINE') else 210), wanted)
        require(str(one(record, 8)) == '0', 'Layer changed')
        if kind in ('ATTRIB', 'ATTDEF'):
            require(one(record, 2) == 'DIR', 'Attribute tag changed')
            require(one(record, 1) == ('attribute' if kind == 'ATTRIB' else 'definition'), 'Text changed')
        else:
            require(triple(record, 10) == {'LINE': (1, 2, 3), 'RAY': (7, 8, 9), 'XLINE': (-1, -2, -3)}[kind], 'WCS origin changed')
        if kind == 'LINE':
            require(triple(record, 11) == (4, 5, 6) and float(one(record, 39)) == 2, 'LINE endpoint/thickness changed')


def rejected(function, value):
    try:
        function(value)
    except ValueError:
        return 1
    raise AssertionError('An actual-output corruption escaped the positive validator')


def check_row(row, expected_input, wanted):
    require(isinstance(row, dict) and set(row) == {'input', 'result'}, 'Wrong numerical row shape')
    require(row['input'] == expected_input and len(row['result']) == 3, 'Seeded input or result shape differs')
    check_vector([from_bits(v) for v in row['result']], wanted, signed_zero=True)


def check_inventory(actual, expected):
    require(actual == expected, 'Direction output inventory differs')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    inputs = wire_inputs()
    expected = {f'direction-assignment-{version}-{binary}-{i}.dxf' for version in PROFILES
                for binary in (False, True) for i in range(len(inputs))}
    expected.add('direction-assignment-numerics.json')
    actual = {p.name for p in directory.glob('direction-assignment-*')}
    check_inventory(actual, expected)
    inventory_controls = rejected(lambda v: check_inventory(v, expected), actual - {next(iter(actual))})
    inventory_controls += rejected(lambda v: check_inventory(v, expected), actual | {'direction-assignment-extra.dxf'})
    wanted = [normalized(v) for v in inputs]
    controls = files = 0
    for version, profile in PROFILES.items():
        for binary in (False, True):
            for i, target in enumerate(wanted):
                path = directory / f'direction-assignment-{version}-{binary}-{i}.dxf'
                data = path.read_bytes()
                require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
                records = selected_records(data)
                check_records(records, target)
                for r, record in enumerate(records):
                    first_code = 11 if record[0][1] in ('RAY', 'XLINE') else 210
                    for code in (first_code, first_code + 10, first_code + 20):
                        at = next(n for n, (c, _) in enumerate(record) if c == code)
                        for operation in ('change', 'nan', 'missing', 'duplicate'):
                            changed = [list(tags) for tags in records]
                            if operation == 'change':
                                changed[r][at] = (code, float(record[at][1]) + .125)
                            elif operation == 'nan':
                                changed[r][at] = (code, math.nan)
                            elif operation == 'missing':
                                del changed[r][at]
                            else:
                                changed[r].insert(at, changed[r][at])
                            controls += rejected(lambda v: check_records(v, target), changed)
                    controls += rejected(lambda v: check_records(v, target), records[:r] + records[r+1:])
                document = ezdxf.readfile(path)
                require(document.dxfversion == profile, 'DXF profile changed')
                audit = document.audit()
                require(not audit.errors and not audit.fixes, 'Direction output requires graph repairs')
                files += 1
    rows = json.loads((directory / 'direction-assignment-numerics.json').read_text())
    expected_inputs = random_inputs()
    require(len(rows) == len(expected_inputs), 'Wrong numerical corpus length')
    numerical_controls = 0
    for row, input_bits in zip(rows, expected_inputs):
        target = normalized([from_bits(v) for v in input_bits])
        check_row(row, input_bits, target)
        for component in range(3):
            result = list(row['result'])
            corrupted = from_bits(result[component])
            for _ in range(32):
                corrupted = math.nextafter(corrupted, math.inf)
            result[component] = to_bits(corrupted)
            numerical_controls += rejected(lambda v: check_row(v, input_bits, target), {'input': input_bits, 'result': result})
    print(f'PASS ezdxf {ezdxf.__version__}: {files} direction drawings, {files * 5} physical vectors, '
          f'{controls} actual-record corruptions; {len(rows)} seeded Decimal scenarios / {len(rows) * 3} component checks; '
          f'{numerical_controls} numerical and {inventory_controls} inventory corruptions rejected; zero graph errors/repairs')


if __name__ == '__main__':
    main()
