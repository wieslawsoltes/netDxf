#!/usr/bin/env python3
"""Independently verify exact ARC endpoint storage and a complete fixture inventory."""
import itertools
import math
from pathlib import Path
import struct
import sys
import ezdxf
from verify_raw_line_geometry import require, reject
from verify_ellipse_axis_proxies import load_visibility_tags as load_tags
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one

PAIRS = (
    (float.fromhex('0x0.0000000000001p-1022'), 1e-13), (1e-300, 180.), (1e-13, 2e-13),
    (0., math.nextafter(360., 0.)), (math.nextafter(360., 0.), float.fromhex('0x0.0000000000001p-1022')),
    (359.999999999999, 1e-12), (math.nextafter(90., math.inf), math.nextafter(180., 0.)),
    (359.9999999999, 359.99999999995), (1e-9, 2e-9), (90., 180.),
)
PROXY = bytes((1, 3, 7, 255))


def exact(value, expected):
    require(isinstance(value, float) and math.isfinite(value), 'Endpoint must be a finite real')
    require(struct.pack('>d', value) == struct.pack('>d', expected), 'Endpoint binary64 bits changed')


def packet(record, start, end, tilted):
    exact(one(record, 50)[1], start)
    exact(one(record, 51)[1], end)
    expected = {10: 2. if tilted else 1., 20: 3. if tilted else 2., 30: 1. if tilted else 3.,
                40: 4., 39: 2., 210: 1. if tilted else 0., 220: 0., 230: 0. if tilted else 1.}
    for code, value in expected.items():
        require(one(record, code)[1] == value, f'Following geometry changed: {code}')
    chunks = [value for code, value in record if code == 310]
    lengths = [value for code, value in record if code in (92, 160)]
    require(lengths == [len(PROXY)] and b''.join(chunks) == PROXY, 'Unmodified proxy packet changed')


def inspect(path, year, binary, placement, tilted):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport mismatch')
    tags = load_tags(path)
    at = tags.index((9, '$ACADVER'))
    require(tags[at + 1] == (1, PROFILES[year]), 'Version mismatch')
    entries = [tags[a:b] for a, b in records(tags)]
    arcs = [r for r in entries if r[0] == (0, 'ARC')]
    require(len(arcs) == len(PAIRS), 'Physical ARC count')
    require({one(r, 8)[1] for r in arcs} == {f'ANGLE_{i:02d}' for i in range(len(PAIRS))}, 'Physical ARC inventory')
    controls = 0
    for record in arcs:
        i = int(one(record, 8)[1].split('_')[1]); start, end = PAIRS[i]
        packet(record, start, end, tilted)
        for code in (50, 51):
            at = next(j for j, tag in enumerate(record) if tag[0] == code)
            for operation in ('zero', 'remove', 'duplicate', 'wrong-group', 'ulp'):
                bad = list(record)
                value = bad[at][1]
                if operation == 'zero': bad[at] = (code, 0. if value else 1.)
                elif operation == 'remove': del bad[at]
                elif operation == 'duplicate': bad.insert(at, bad[at])
                elif operation == 'wrong-group': bad[at] = (1070, value)
                else: bad[at] = (code, math.nextafter(value, math.inf))
                controls += reject(lambda: packet(bad, start, end, tilted))
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('ANGLE_PAPER') if placement == 1 else doc.blocks['ANGLE_BLOCK']
    arcs = list(space.query('ARC'))
    require(len(arcs) == len(PAIRS), 'Independent ARC placement/count')
    for arc in arcs:
        i = int(arc.dxf.layer.split('_')[1]); start, end = PAIRS[i]
        exact(arc.dxf.start_angle, start); exact(arc.dxf.end_angle, end)
        require(arc.dxf.owner == space.block_record_handle, 'Independent owner')
        require(tuple(arc.ocs().to_wcs(arc.dxf.center)) == (1., 2., 3.), 'Independent WCS center')
        require(arc.dxf.radius == 4. and arc.dxf.thickness == 2., 'Independent size')
        require(tuple(arc.dxf.extrusion) == ((1., 0., 0.) if tilted else (0., 0., 1.)), 'Independent plane')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following entity')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), range(3), (False, True), (False, True), ('source', 'False', 'True')))
    def name(spec):
        year, binary, placement, tilted, raw, output = spec
        return f'arc-angle-fidelity-AutoCad{year}-{binary}-{placement}-{tilted}-{raw}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, f'ARC inventory missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('arc-angle-fidelity-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'arc-angle-fidelity-extra.dxf'}))
    controls = 0
    for spec in specs:
        year, binary, placement, tilted, raw, output = spec
        controls += inspect(directory / name(spec), year, binary if output == 'source' else output == 'True', placement, tilted)
    print(f'PASS: {len(specs)} ARC drawings / {len(specs)*len(PAIRS)} independently loaded arcs; '
          f'{controls} endpoint corruptions and two inventory controls rejected; zero graph errors/repairs. '
          'Stored parameter fidelity is not native rendering or universal geometry-algorithm qualification.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_arc_angle_fidelity.py ARTIFACTS')
    main(Path(sys.argv[1]))
