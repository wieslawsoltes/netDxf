#!/usr/bin/env python3
"""Independent analytic ellipse sampling checks; 32 ULP of the semi-major axis.

Fixed-count sampling evidence, not a chord-error or native CAD oracle.
"""
import io
import json
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

SCALES = (1e-310, 1e-200, 1.0, 1e200, sys.float_info.max)
ANGLES = ((0, 0), (0, 90), (90, 270), (180, 270), (270, 90), (25, 215))
PROFILES = dict(zip(('AutoCad2000', 'AutoCad2004', 'AutoCad2007', 'AutoCad2010', 'AutoCad2013', 'AutoCad2018'),
                    ('AC1015', 'AC1018', 'AC1021', 'AC1024', 'AC1027', 'AC1032')))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def expected(scale, sweep, rotation):
    diameter = SCALES[scale]
    a, b = diameter * .5, (diameter * .25) * .5
    start, end = ANGLES[sweep]
    full = start == end
    if full:
        lo, hi = 0., math.tau
    else:
        def eccentric(degrees):
            if degrees % 90 == 0:
                return degrees / 180 * math.pi
            p = math.atan2(math.sin(math.radians(degrees)), .25 * math.cos(math.radians(degrees)))
            return p % math.tau
        lo, hi = eccentric(start), eccentric(end)
        if hi < lo:
            hi += math.tau
    c, s = math.cos(rotation * math.pi / 4), math.sin(rotation * math.pi / 4)
    points = []
    for i in range(17):
        p = lo + (hi - lo) * i / (17 if full else 16)
        x, y = math.cos(p), math.sin(p) * (b / a)
        points.append(((x * c - y * s) * a, (x * s + y * c) * a))
    return points, max(32 * math.ulp(a), 32 * math.ulp(0.))


def check_points(points, wanted, bound):
    require(len(points) == 17, 'Sample count differs')
    for p, e in zip(points, wanted):
        require(len(p) == 2, 'Point dimension differs')
        require(all(math.isfinite(v) and abs(v - t) <= bound for v, t in zip(p, e)), 'Ellipse position differs')


def selected(data):
    tags = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    records, current = [], []
    for tag in tags:
        if tag.code == 0:
            if current and current[0] == (0, 'LWPOLYLINE'):
                records.append(current)
            current = []
        current.append((tag.code, tag.value))
    require(len(records) == 1, 'Expected exactly one sampled LWPOLYLINE')
    return records[0]


def check_record(record, sweep):
    def values(code):
        return [float(v) for c, v in record if c == code]
    require(values(90) == [17] and values(70) == [1 if sweep == 0 else 0], 'Wrong count or closure')
    x, y = values(10), values(20)
    require(len(x) == len(y) == 17, 'Missing or repeated point component')
    require(values(38) == [0.] and values(39) == [0.], 'Wrong elevation or thickness')
    check_points(list(zip(x, y)), *expected(2, sweep, 1))


def rejected(check, value):
    try:
        check(value)
    except ValueError:
        return 1
    raise AssertionError('Corruption escaped the positive validator')


def main(directory):
    directory = Path(directory)
    names = {f'ellipse-sampling-{v}-{b}-{k}.dxf' for v in PROFILES for b in (False, True) for k in range(6)} | {'ellipse-sampling-numerics.json'}
    require({p.name for p in directory.glob('ellipse-sampling-*')} == names, 'Fixture inventory differs')
    rows = json.loads((directory / 'ellipse-sampling-numerics.json').read_text())
    keys = [(a, b, c) for a in range(5) for b in range(6) for c in range(4)]
    require([(r['scale'], r['sweep'], r['rotation']) for r in rows] == keys, 'Numerical corpus differs')
    negative = 0
    for row, key in zip(rows, keys):
        target, bound = expected(*key)
        check_points(row['points'], target, bound)
        for i in range(17):
            changed = [list(p) for p in row['points']]
            changed[i][0] += SCALES[key[0]] * .01
            negative += rejected(lambda v: check_points(v, target, bound), changed)
    physical = 0
    for v, profile in PROFILES.items():
        for binary in (False, True):
            for sweep in range(6):
                path = directory / f'ellipse-sampling-{v}-{binary}-{sweep}.dxf'
                data = path.read_bytes()
                require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
                record = selected(data); check_record(record, sweep)
                for i, (code, value) in enumerate(record):
                    if code not in (10, 20, 70, 90):
                        continue
                    for op in ('change', 'drop', 'duplicate'):
                        corrupted = list(record)
                        if op == 'change':
                            corrupted[i] = (code, float(value) + 1)
                        elif op == 'drop':
                            del corrupted[i]
                        else:
                            corrupted.insert(i, corrupted[i])
                        physical += rejected(lambda r: check_record(r, sweep), corrupted)
                doc = ezdxf.readfile(path)
                require(doc.dxfversion == profile, 'Wrong version')
                audit = doc.audit()
                require(not audit.errors and not audit.fixes, 'Graph audit requires repair')
    print(f'PASS: {len(rows)} scenarios / {len(rows)*17} analytic samples; 72 drawings; '
          f'{negative} numerical and {physical} physical corruptions rejected; zero graph errors/repairs')


if __name__ == '__main__':
    main(sys.argv[1])
