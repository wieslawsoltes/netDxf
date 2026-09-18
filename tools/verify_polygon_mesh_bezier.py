#!/usr/bin/env python3
"""Independently verify open polynomial Bezier polygon-mesh samples and DXF packets.

Expected geometry uses exact Fraction tensor-product Bernstein polynomials, not
netDxf's separable de Casteljau algorithm. Sampling parameters are the actual
binary64 divisions. Component errors admit 2e-12 times the largest control
magnitude on that axis; corners/controls are exact. A separate extreme case
checks a tiny positive contribution with a relative 2e-13 bound. These are
synthetic mathematical checks, not native AutoCAD surface-fit qualification.
"""
from functools import lru_cache
from fractions import Fraction
import io
import itertools
import json
import math
from pathlib import Path
import re
import struct
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

PROFILES = dict(zip(('AutoCad2000', 'AutoCad2004', 'AutoCad2007', 'AutoCad2010', 'AutoCad2013', 'AutoCad2018'),
                    ('AC1015', 'AC1018', 'AC1021', 'AC1024', 'AC1027', 'AC1032')))
WIRE_SIZES = ((2, 2), (3, 5), (4, 4), (5, 3))
NUMERIC_SIZES = ((2, 2), (3, 5), (5, 3), (4, 4), (12, 7), (2, 64))
COMMON = [(100, 'AcDbEntity'), (67, 0), (8, '0'), (62, 256), (6, 'ByLayer'), (370, -1), (48, 1.), (60, 0)]


def require(ok, message):
    if not ok:
        raise ValueError(message)


def bits(value):
    return struct.pack('>d', value).hex().upper()


def number(value):
    require(isinstance(value, str) and re.fullmatch('[0-9A-F]{16}', value), 'Invalid numerical bit string')
    return struct.unpack('>d', bytes.fromhex(value))[0]


def controls(u, v, exponent=0):
    scale = math.ldexp(1., exponent)
    return [((i - 1.25) * scale, (j - .75) * scale, ((i*i % 7) - 3) * (j % 3 - 1) * scale)
            for j in range(v) for i in range(u)]


@lru_cache(maxsize=256)
def basis(count, samples, index):
    t = Fraction.from_float(index / (samples - 1.))
    return tuple(math.comb(count - 1, a) * t**a * (1-t)**(count - 1-a) for a in range(count))


@lru_cache(maxsize=64)
def expected_grid(u, v, pu, pv, exponent=0):
    points = controls(u, v, exponent)
    rational = [tuple(Fraction.from_float(x) for x in p) for p in points]
    output = []
    for j in range(pv):
        bv = basis(v, pv, j)
        for i in range(pu):
            bu = basis(u, pu, i)
            output.append(tuple(float(sum((rational[a+b*u][axis] * bu[a] * bv[b]
                                          for b in range(v) for a in range(u)), Fraction(0))) for axis in range(3)))
    return tuple(output)


def bounds(points):
    return tuple(max(abs(p[axis]) for p in points) * 2e-12 for axis in range(3))


def check_point(actual, expected, tolerance, exact=False):
    require(len(actual) == 3 and all(math.isfinite(x) for x in actual), 'Nonfinite/invalid sample')
    if exact:
        require(tuple(actual) == tuple(expected), 'Control/corner coordinate differs')
    else:
        require(all(abs(a-e) <= tol for a, e, tol in zip(actual, expected, tolerance)), 'Bezier coordinate differs')


def expected_packets(u, v, nested):
    head = [(0, 'POLYLINE')] + [tag for tag in COMMON if not nested or tag[0] != 67] + [(100, 'AcDbPolygonMesh'), (10, 0.), (20, 0.), (30, 0.),
           (71, u), (72, v), (73, 5), (74, 7), (70, 20), (75, 8), (210, 0.), (220, 0.), (230, 1.)]
    packets = [head]
    original = controls(u, v)
    generated = expected_grid(u, v, 5, 7)
    # DXF is M-major, N-fast. The model's control and generated arrays are U-fast.
    for group, nu, nv, points in ((80, u, v, original), (72, 5, 7, generated)):
        for i in range(nu):
            for j in range(nv):
                packets.append([(0, 'VERTEX'), (100, 'AcDbEntity'), (8, '0'), (62, 256),
                    (100, 'AcDbVertex'), (100, 'AcDbPolygonMeshVertex'),
                    *zip((10, 20, 30), points[i+j*nu]), (70, group), (40, 0.), (41, 0.)])
    packets.append([(0, 'SEQEND'), (100, 'AcDbEntity'), (8, '0')])
    return packets, bounds(original)


def records(data):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig')))
    result, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0][1] in ('POLYLINE', 'VERTEX', 'SEQEND'):
                result.append(current)
            current = []
        current.append((tag.code, cast_tag_value(tag.code, tag.value)))
    if current and current[0][1] in ('POLYLINE', 'VERTEX', 'SEQEND'):
        result.append(current)
    require(result and result[0][0] == (0, 'POLYLINE'), 'Missing selected parent')
    handles, parent, output = set(), None, []
    for index, record in enumerate(result):
        require([c for c, _ in record[:3]] == [0, 5, 330], 'Identity framing/order differs')
        require(sum(c == 5 for c, _ in record) == 1 and sum(c == 330 for c, _ in record) == 1, 'Repeated identities')
        handle, owner = int(record[1][1], 16), int(record[2][1], 16)
        require(handle > 0 and owner > 0 and handle not in handles, 'Invalid/duplicate identity')
        handles.add(handle)
        if index == 0:
            parent = handle
        else:
            require(owner == parent, 'Child owner differs from selected mesh')
        output.append([record[0]] + record[3:])
    return output


def validate_packets(actual, expected, tolerance, u, v):
    require(len(actual) == len(expected), 'Bezier packet inventory differs')
    for r, (packet, wanted) in enumerate(zip(actual, expected)):
        require(len(packet) == len(wanted), 'Packet length differs')
        sampled = u*v < r <= u*v+35
        for (code, value), (expected_code, expected_value) in zip(packet, wanted):
            require(code == expected_code, 'Tag order/code differs')
            if sampled and code in (10, 20, 30):
                require(math.isfinite(value) and abs(value-expected_value) <= tolerance[(code-10)//10], 'Physical sample differs')
            else:
                require(value == expected_value, 'Physical metadata/control differs')
        if sampled:
            n = r - u*v - 1
            if n//7 in (0, 4) and n % 7 in (0, 6):
                require(packet == wanted, 'Physical corner differs')


def rejected(fn, value):
    try:
        fn(value)
    except (ValueError, TypeError, KeyError, IndexError):
        return 1
    raise AssertionError('Corruption escaped the positive checker')


def validate_row(row, shape, expected):
    u, v, exponent, pu, pv = shape
    require(isinstance(row, dict) and set(row) == {'u', 'v', 'exponent', 'pu', 'pv', 'controls', 'points'}, 'Numerical row shape differs')
    require(tuple(row[k] for k in ('u', 'v', 'exponent', 'pu', 'pv')) == shape, 'Numerical scenario differs')
    original = controls(u, v, exponent)
    require(row['controls'] == [[bits(x) for x in p] for p in original], 'Input controls differ')
    require(len(row['points']) == pu*pv, 'Numerical sample inventory differs')
    tolerance = bounds(original)
    for n, (actual, wanted) in enumerate(zip(row['points'], expected)):
        check_point([number(x) for x in actual], wanted, tolerance,
                    exact=n % pu in (0, pu-1) and n//pu in (0, pv-1))


def validate_extreme(actual):
    values = tuple(number(x) for x in actual)
    t = Fraction.from_float(1 / 100.)
    expected = float(Fraction.from_float(sys.float_info.max / 4) * t**200)
    require(len(values) == 3 and math.isfinite(values[0]) and values[0] > 0,
            'Lost representable tiny-weight contribution')
    require(abs(values[0] - expected) <= expected*2e-13 and values[1:] == (0., 0.), 'Extreme contribution differs')


def main():
    directory = Path(sys.argv[1])
    inventory = {f'bezier-grid-{version}-{binary}-{shape}-{nested}.dxf': (version, binary, shape, nested)
                 for version, binary, shape, nested in itertools.product(PROFILES, (False, True), range(4), (False, True))}
    names = set(inventory) | {'bezier-grid-numerics.json'}
    def check_inventory(actual):
        require(actual == names, 'Exact Bezier fixture inventory differs')
    check_inventory({p.name for p in directory.glob('bezier-grid-*')})
    inventory_controls = rejected(check_inventory, names - {'bezier-grid-numerics.json'})
    inventory_controls += rejected(check_inventory, names | {'bezier-grid-extra.dxf'})
    packet_controls = sample_count = 0
    for name, (version, binary, shape, nested) in inventory.items():
        path = directory / name
        data = path.read_bytes()
        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
        u, v = WIRE_SIZES[shape]
        expected, tolerance = expected_packets(u, v, nested)
        packets = records(data)
        def check(actual):
            validate_packets(actual, expected, tolerance, u, v)
        check(packets)
        for r, packet in enumerate(packets):
            packet_controls += rejected(check, packets[:r]+packets[r+1:])
            for n, (code, value) in enumerate(packet):
                changed = list(packets); changed[r] = list(packet)
                changed[r][n] = (code, value+.125 if isinstance(value, (int, float)) else value+'!')
                packet_controls += rejected(check, changed)
                changed = list(packets); changed[r] = packet[:n]+packet[n+1:]
                packet_controls += rejected(check, changed)
        document = ezdxf.readfile(path)
        require(document.dxfversion == PROFILES[version], 'Profile differs')
        space = document.blocks['BEZIER_PATCH'] if nested else document.modelspace()
        require(len(space.query('POLYLINE')) == 1, 'Mesh placement differs')
        audit = document.audit()
        require(not audit.errors and not audit.fixes, 'Graph requires repairs')
        sample_count += 35
    corpus = json.loads((directory / 'bezier-grid-numerics.json').read_text())
    require(set(corpus) == {'rows', 'extreme'}, 'Numerical corpus shape differs')
    shapes = [(u, v, exponent, pu, pv) for (u, v), exponent, (pu, pv) in
              itertools.product(NUMERIC_SIZES, (-500, 0, 500), ((4, 5), (7, 4)))]
    require(len(corpus['rows']) == len(shapes), 'Numerical corpus length differs')
    numerical_controls = numerical_points = 0
    for row, shape in zip(corpus['rows'], shapes):
        u, v, exponent, pu, pv = shape
        expected = expected_grid(u, v, pu, pv, exponent)
        check = lambda actual: validate_row(actual, shape, expected)
        check(row)
        for axis in range(3):
            changed = dict(row); changed['points'] = [list(p) for p in row['points']]
            value = number(changed['points'][1][axis])
            delta = max(abs(p[axis]) for p in controls(u, v, exponent)) * .01
            changed['points'][1][axis] = bits(value+delta)
            numerical_controls += rejected(check, changed)
        changed = dict(row); changed['controls'] = row['controls'][1:]
        numerical_controls += rejected(check, changed)
        numerical_points += pu*pv
    validate_extreme(corpus['extreme'])
    numerical_controls += rejected(validate_extreme, [bits(0.), bits(0.), bits(0.)])
    print(f'PASS ezdxf {ezdxf.__version__}: {len(inventory)} drawings / {sample_count} physical samples; '
          f'{len(shapes)} Fraction scenarios / {numerical_points} numerical samples and one extreme contribution; '
          f'{packet_controls} packet, {numerical_controls} numerical and {inventory_controls} inventory corruptions rejected; '
          'zero graph errors/repairs')


if __name__ == '__main__':
    main()
