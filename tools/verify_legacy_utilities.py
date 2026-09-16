#!/usr/bin/env python3
"""Independently verify legacy unit text, DXF clock serials and RGB persistence.

Python Fraction/round supplies exact ties-to-even arithmetic independently of the
production BigInteger decomposition. This is a declared utility contract, not
native AutoCAD option/rounding or FIELD-expression qualification.
"""
import argparse
import calendar
import copy
import datetime as dt
from fractions import Fraction
import json
import math
from pathlib import Path
import struct
import ezdxf
from verify_mleader_inputs import check

TICKS = 864000000000
MIN_DAY = 1721426
MAX_DAY = 5373485
PROFILES = {'AutoCad2000': 'AC1015', 'AutoCad2004': 'AC1018', 'AutoCad2007': 'AC1021',
            'AutoCad2010': 'AC1024', 'AutoCad2013': 'AC1027', 'AutoCad2018': 'AC1032'}
KINDS = ('decimal', 'engineering', 'architectural', 'fractional', 'angle', 'dms')


def from_bits(bits):
    return struct.unpack('>d', bytes.fromhex(bits))[0]


def bits(value):
    return struct.pack('>d', value).hex().upper()


def decimal(value, places):
    digits = str(value).zfill(places + 1)
    return digits if places == 0 else digits[:-places] + '.' + digits[-places:]


def fraction(value, denominator):
    whole, remainder = divmod(value, denominator)
    if not remainder:
        return str(whole)
    part = Fraction(remainder, denominator)
    return f'{whole} {part.numerator}/{part.denominator}'


def formatted(row):
    p, kind, value = row['places'], row['kind'], from_bits(row['bits'])
    check(type(p) is int and 0 <= p <= 8 and kind in KINDS and math.isfinite(value), 'Invalid declared format input')
    scale = (2 ** p if kind in ('architectural', 'fractional') else
             (1 if p == 0 else 60 if p <= 2 else 3600 * 10 ** max(0, p - 4)) if kind == 'dms' else 10 ** p)
    number = round(abs(Fraction.from_float(value)) * scale)
    sign = '-' if value < 0 and number != 0 else ''
    if kind == 'dms':
        degrees, rest = divmod(number, scale)
        result = str(degrees) + 'd'
        if p > 0:
            minutes, seconds = (rest, 0) if p <= 2 else divmod(rest, 60 * 10 ** max(0, p - 4))
            result += str(minutes) + "'"
            if p > 2:
                result += decimal(seconds, max(0, p - 4)) + '"'
    elif kind == 'fractional':
        result = fraction(number, scale)
    elif kind in ('architectural', 'engineering'):
        feet, inches = divmod(number, 12 * scale)
        result = str(feet) + "'-" + (fraction(inches, scale) if kind == 'architectural' else decimal(inches, p)) + '"'
    else:
        result = decimal(number, p) + ('d' if kind == 'angle' else '')
    return sign + result


def check_format(row):
    check(row['actual'] == formatted(row), 'Incorrect exact unit text: ' + str((row['kind'], row['places'], row['bits'])))


def expected_serial(ticks):
    day, remainder = divmod(ticks, TICKS)
    result = float(MIN_DAY + day) + remainder / TICKS
    return min(result, math.nextafter(float(MAX_DAY), -math.inf))


def decoded_ticks(serial):
    day = math.floor(serial)
    return (day - MIN_DAY) * TICKS + round(Fraction.from_float(serial - day) * TICKS)


def check_calendar(row):
    ticks = int(row['ticks'])
    serial = from_bits(row['bits'])
    check(row['bits'] == bits(expected_serial(ticks)), 'Wrong encoded DXF clock serial')
    check(MIN_DAY <= serial < MAX_DAY, 'Clock serial outside representable Gregorian range')
    check(int(row['decodedTicks']) == decoded_ticks(serial), 'Wrong calendar fraction or day conversion')
    check(abs(int(row['decodedTicks']) - ticks) <= 1000, 'Clock resolution loss beyond binary64 day precision')


def check_elapsed(row):
    check(int(row['ticks']) == round(Fraction.from_float(from_bits(row['bits'])) * TICKS), 'Wrong elapsed tick rounding')


def expect_reject(function, value):
    try:
        function(value)
    except (ValueError, AssertionError):
        return 1
    raise AssertionError('An intentionally corrupted utility output escaped verification')


def clock_ticks(year, month, day, hour=0, minute=0, second=0, subsecond=0):
    return (dt.date(year, month, day).toordinal() - 1) * TICKS + (hour * 3600 + minute * 60 + second) * 10000000 + subsecond


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    matrices = {'legacy-utility-formats.json', 'legacy-utility-calendar.json', 'legacy-utility-elapsed.json'}
    drawings = {f'legacy-utility-{version}-{binary}.dxf' for version in PROFILES for binary in (False, True)}
    check({p.name for p in directory.glob('legacy-utility-*')} == matrices | drawings, 'Exact utility fixture inventory required')
    formats = json.loads((directory / 'legacy-utility-formats.json').read_text())
    check(len(formats) == 4320, 'Incomplete unit matrix')
    identities = {(row['kind'], row['places'], row['bits']) for row in formats}
    input_bits = {row['bits'] for row in formats}
    check(len(input_bits) == 80 and identities == {(kind, p, value) for kind in KINDS for p in range(9) for value in input_bits}, 'Duplicate or missing unit combinations')
    negatives = 0
    for row in formats:
        check_format(row)
        candidate = dict(row, actual=row['actual'] + '_CORRUPT')
        negatives += expect_reject(check_format, candidate)
    dates = json.loads((directory / 'legacy-utility-calendar.json').read_text())
    wanted_dates = [0, 3155378975999999999, clock_ticks(1999,12,31,21,58,35), clock_ticks(1998,1,1,12), clock_ticks(1997,7,4,14,29,58)]
    for year in (1,4,99,100,400,1500,1582,1600,1700,1900,2000,2026,2400,9999):
        for month in (1,2,3,10,12):
            wanted_dates.append(clock_ticks(year,month,calendar.monthrange(year,month)[1],23,59,59,1234567))
    check([int(row['ticks']) for row in dates] == wanted_dates, 'Calendar source inventory differs')
    for row in dates:
        check_calendar(row)
        negatives += expect_reject(check_calendar, dict(row, decodedTicks=str(int(row['decodedTicks']) + 1)))
        negatives += expect_reject(check_calendar, dict(row, bits=bits(from_bits(row['bits']) + .5)))
    elapsed = json.loads((directory / 'legacy-utility-elapsed.json').read_text())
    check(len(elapsed) == 14, 'Elapsed matrix incomplete')
    for row in elapsed:
        check_elapsed(row)
        negatives += expect_reject(check_elapsed, dict(row, ticks=str(int(row['ticks']) + 1)))
    for version, profile in PROFILES.items():
        for binary in (False, True):
            path = directory / f'legacy-utility-{version}-{binary}.dxf'
            check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
            doc = ezdxf.readfile(path)
            check(doc.dxfversion == profile, 'DXF profile changed')
            check(doc.header['$TDCREATE'] == expected_serial(clock_ticks(9999,12,31,21,58,35,1234567)), 'Last-day clock serial changed')
            check(doc.header['$TDUCREATE'] == expected_serial(clock_ticks(100,3,1,1,2,3,7654321)), 'Early-century clock serial changed')
            check(round(Fraction.from_float(doc.header['$TDINDWG']) * TICKS) == 123456789, 'Elapsed header resolution changed')
            line, = doc.modelspace().query('LINE')
            if version != 'AutoCad2000':
                check(line.dxf.true_color == 0x12AB34, 'RGB encoding changed')
            else:
                check(not line.dxf.hasattr('true_color'), 'R2000 gained an unsupported true-color field')
                check(1 <= line.dxf.color <= 255, 'Palette fallback invalid')
            audit = doc.audit()
            check(not audit.errors and not audit.fixes, 'Utility drawing needs graph repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: 4320 exact formats, 75 calendar serials, 14 elapsed values, 12 DXF drawings; {negatives} corruptions rejected; no native formatter claim')


if __name__ == '__main__':
    main()
