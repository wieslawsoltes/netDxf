#!/usr/bin/env python3
"""Independent RAY/XLINE affine oracle: exact Fraction origins, Decimal directions.

Source cases are independently reconstructed, not read from emitted expectations.
The entire selected entity packet is checked except handle/owner identities, which
receive independent drawing graph audits. Directions admit four ULP per component
and a 2e-15 squared-unit bound; origin components compare exactly as binary64.
"""
from decimal import Decimal, localcontext
from fractions import Fraction as F
import io
import math
import json
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from verify_direction_assignment import check_vector, require, rejected, from_bits, to_bits

PROFILES = {'AutoCad2000': 'AC1015', 'AutoCad2004': 'AC1018', 'AutoCad2007': 'AC1021',
            'AutoCad2010': 'AC1024', 'AutoCad2013': 'AC1027', 'AutoCad2018': 'AC1032'}
ZERO = (0., 0., 0.)
IDENTITY = ((1., 0., 0.), (0., 1., 0.), (0., 0., 1.))


def cases():
    o, d, n = (1., 2., 3.), (2., -3., 6.), (0., 0., 1.)
    def scale(x, y, z): return ((x, 0., 0.), (0., y, 0.), (0., 0., z))
    tiny, big = math.ldexp(1., -1000), math.ldexp(1., 1000)
    maximum, minimum = sys.float_info.max, math.ldexp(1., -1074)
    return [
        (IDENTITY, ZERO, o, d),
        (IDENTITY, (4., -5., 6.), o, d),
        (scale(3., 3., 3.), ZERO, o, d),
        (scale(2., 3., 4.), ZERO, o, d),
        (scale(-1., 1., 1.), ZERO, o, d),
        (scale(-2., -2., -2.), ZERO, o, d),
        (((0., -1., 0.), (1., 0., 0.), (0., 0., 1.)), ZERO, o, d),
        (((1., 2., 0.), (0., 1., .5), (0., 0., 1.)), ZERO, o, d),
        (scale(1., 2., 3.), ZERO, ZERO, (1., 0., 0.)),
        (scale(1., 1., 0.), ZERO, o, (2., -3., 0.)),
        (scale(0., 1., 1.), ZERO, o, (0., 3., 4.)),
        (scale(tiny, tiny, tiny), ZERO, o, d),
        (scale(minimum, minimum, minimum), ZERO, o, (1., 0., 0.)),
        (scale(big, big, big), ZERO, o, d),
        (scale(maximum, maximum, maximum), ZERO, ZERO, (1., 1., 1.)),
        (((maximum, -maximum, 0.), (0., 1., 0.), (0., 0., 1.)), (3., 4., 5.), (2., 2., 2.), (1., 1., 1.)),
        (((maximum, maximum, maximum), (maximum, -maximum, 0.), (0., 0., maximum)), (5., 7., 9.), ZERO, (1., 1., 1.)),
        (((minimum, -minimum, minimum), (0., minimum, 0.), (0., 0., minimum)), ZERO, ZERO, (1., 1., 0.)),
        (IDENTITY, (-1e300, 1e300, -1e300), (1e300, -1e300, 1e300), d),
        (((0., 0., 1.), (1., 0., 0.), (0., 1., 0.)), ZERO, o, d),
        (scale(1e-100, 1e100, 1.), ZERO, o, (1., 1., 1.)),
    ]


def expected(case):
    matrix, translation, origin, direction = case
    # The source constructors use the pre-existing ordinary-range binary64
    # inverse-length arithmetic. This pins the stored input, before our oracle's
    # independent exact dot products and high-precision normalization.
    inverse = 1. / math.sqrt(sum(v * v for v in direction))
    stored = tuple(v * inverse for v in direction)
    point = tuple(float(sum(F(a) * F(b) for a, b in zip(row, origin)) + F(t))
                  for row, t in zip(matrix, translation))
    image = [sum(F(a) * F(b) for a, b in zip(row, stored)) for row in matrix]
    with localcontext() as context:
        context.prec = 1500
        values = [Decimal(v.numerator) / Decimal(v.denominator) for v in image]
        length = sum(v * v for v in values).sqrt()
        unit = tuple(float(v / length) for v in values)
    return point, unit


def records(data):
    loader = (binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else
              ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'))))
    result, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0][1] in ('RAY', 'XLINE'): result.append(current)
            current = []
        current.append((tag.code, tag.value))
    if current and current[0][1] in ('RAY', 'XLINE'): result.append(current)
    return result


def check_record(selected, ray, profile, index, target):
    require(len(selected) == 1, 'Wrong selected entity count')
    # Exclude just entity/owner handles, not geometry or common metadata.
    require(sum(c == 5 for c, _ in selected[0]) == 1 and sum(c == 330 for c, _ in selected[0]) == 1, "Handle/owner framing changed")
    tags = [(c, v) for c, v in selected[0] if c not in (5, 330)]
    proxy = index in (0, 8)
    length_code = 92 if profile < 'AC1027' else 160
    wanted_codes = [0, 100, 67, 8, 62, 6, 370, 48, 60]
    if proxy: wanted_codes += [length_code, 310]
    wanted_codes += [100, 10, 20, 30, 11, 21, 31]
    require([c for c, _ in tags] == wanted_codes, 'Ordered entity packet changed')
    values = [v for _, v in tags]
    require(values[0] == ('RAY' if ray else 'XLINE') and values[1] == 'AcDbEntity', 'Entity schema changed')
    require(int(values[2]) == 0 and values[3] == 'INFINITE_AUDIT' and int(values[4]) == 3, 'Space/layer/color changed')
    require(values[5] == 'ByLayer' and int(values[6]) == -1 and float(values[7]) == 1.25 and int(values[8]) == 1,
            'Linetype/lineweight/scale/visibility changed')
    at = 9
    if proxy:
        require(int(values[at]) == 4, 'Proxy length changed')
        data = values[at + 1]
        if isinstance(data, str): data = bytes.fromhex(data)
        require(data == bytes((4, 3, 2, 1)), 'Proxy payload changed')
        at += 2
    require(values[at] == ('AcDbRay' if ray else 'AcDbXline'), 'Subclass changed')
    point = tuple(float(v) for v in values[at+1:at+4])
    direction = tuple(float(v) for v in values[at+4:at+7])
    require(point == target[0], 'WCS origin differs from exact Fraction result')
    check_vector(direction, target[1])



def seeded_inputs():
    state, mask = 0x524159584C494E45, (1 << 64) - 1
    def next_integer():
        nonlocal state
        state ^= (state << 13) & mask
        state ^= state >> 7
        state ^= (state << 17) & mask
        return state
    def value(exponent):
        return f'{(next_integer() & 0x800fffffffffffff) | ((1023 + exponent) << 52):016X}'
    for _ in range(512):
        exponent = next_integer() % 1801 - 900
        yield {'matrix': [value(exponent) for _ in range(9)],
               'origin': [value(next_integer() % 101 - 50) for _ in range(3)],
               'translation': [value(exponent) for _ in range(3)]}


def check_numerical(directory):
    rows = json.loads((directory / 'infinite-affine-numerics.json').read_text())
    require(isinstance(rows, list) and len(rows) == 512, 'Numerical inventory changed')
    controls = 0
    for row, source in zip(rows, seeded_inputs()):
        m = [from_bits(v) for v in source['matrix']]
        matrix = [m[n:n+3] for n in (0, 3, 6)]
        origin = [from_bits(v) for v in source['origin']]
        translation = [from_bits(v) for v in source['translation']]
        point, direction = expected((matrix, translation, origin, (2., -3., 6.)))
        _, normal = expected((matrix, ZERO, ZERO, (0., 0., 1.)))
        def validate(candidate):
            require(set(candidate) == set(source) | {'resultOrigin', 'resultDirection', 'resultNormal'}, 'Numerical record shape changed')
            require(all(candidate[key] == value for key, value in source.items()), 'Seeded numerical input changed')
            require(tuple(from_bits(v) for v in candidate['resultOrigin']) == point, 'Exact numerical origin differs')
            check_vector([from_bits(v) for v in candidate['resultDirection']], direction)
            check_vector([from_bits(v) for v in candidate['resultNormal']], normal)
        validate(row)
        for key in ('resultOrigin', 'resultDirection', 'resultNormal'):
            for component in range(3):
                values = list(row[key]); number = from_bits(values[component])
                for _ in range(1 if key == 'resultOrigin' else 32): number = math.nextafter(number, math.inf)
                values[component] = to_bits(number)
                changed = dict(row); changed[key] = values
                controls += rejected(validate, changed)
    return controls


def main(directory):
    scenarios = cases(); targets = [expected(c) for c in scenarios]
    names = {f'infinite-affine-{v}-{b}-{ray}-{four}-{i}.dxf' for v in PROFILES
             for b in (False, True) for ray in (False, True) for four in (False, True) for i in range(len(scenarios))}
    names.add("infinite-affine-numerics.json")
    actual = {p.name for p in directory.glob('infinite-affine-*')}
    inventory = lambda n: require(n == names, 'Fixture inventory differs')
    inventory(actual)
    inventory_controls = rejected(inventory, actual - {next(iter(actual))}) + rejected(inventory, actual | {'infinite-affine-extra.dxf'})
    files = controls = 0
    for version, profile in PROFILES.items():
        for binary in (False, True):
            for ray in (False, True):
                for four in (False, True):
                    for i, target in enumerate(targets):
                        path = directory / f'infinite-affine-{version}-{binary}-{ray}-{four}-{i}.dxf'
                        data = path.read_bytes()
                        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
                        selected = records(data)
                        validate = lambda r: check_record(r, ray, profile, i, target)
                        validate(selected)
                        record = selected[0]
                        # Every non-identity physical tag is deleted and duplicated;
                        # every numeric geometry value is independently corrupted.
                        for at, (code, value) in enumerate(record):
                            if code in (5, 330): continue
                            controls += rejected(validate, [record[:at] + record[at+1:]])
                            controls += rejected(validate, [record[:at] + [record[at]] + record[at:]])
                            if code in (10, 20, 30):
                                changed = list(record); changed[at] = (code, math.nextafter(float(value), math.inf))
                                controls += rejected(validate, [changed])
                            if code in (11, 21, 31):
                                number = float(value)
                                for _ in range(32): number = math.nextafter(number, math.inf)
                                changed = list(record); changed[at] = (code, number)
                                controls += rejected(validate, [changed])
                        controls += rejected(validate, [])
                        document = ezdxf.readfile(path)
                        require(document.dxfversion == profile, 'Wrong DXF profile')
                        audit = document.audit()
                        require(not audit.errors and not audit.fixes, 'Drawing needs graph repairs')
                        files += 1
    numeric_controls = check_numerical(directory)
    print(f'PASS ezdxf {ezdxf.__version__}: {files} RAY/XLINE drawings; exact Fraction origins; '
          f'1500-digit Decimal directions; {controls} parsed-record corruptions and '
          f'{inventory_controls} inventory corruptions rejected; 512 seeded numerical scenarios, '
          f'{numeric_controls} numerical corruptions rejected; zero graph errors/repairs')


if __name__ == '__main__':
    if len(sys.argv) != 2: raise SystemExit('usage: verify_infinite_line_transforms.py ARTIFACT_DIRECTORY')
    main(Path(sys.argv[1]))
