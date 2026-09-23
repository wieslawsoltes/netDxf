#!/usr/bin/env python3
"""Inspect actual generated extension geometry, source settings and ownership."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one, corrupt


def scale(row): return 2 if row in (5, 7) else 1

def base_length(row): return 100 if row == 5 else 0 if row == 6 else 2


def expected_lines(kind, row, pose):
    angle = math.radians(37) if pose == 2 else 0
    def turn(x, y): return (x * math.cos(angle) - y * math.sin(angle), x * math.sin(angle) + y * math.cos(angle))
    depth = 1 if pose == 1 else 8
    if kind < 2:
        origins = [turn(0, 0), turn(10, 0)]
        anchors = [turn(0, -8 if pose == 3 else depth), turn(10, -8 if pose == 3 else depth)]
    else:
        origins = [turn(3 if pose == 1 else 2, 0), turn(0, 3 if pose == 1 else 2)] if kind == 2 else [turn(2, 0), turn(0, (12 if pose == 2 else 3) if kind == 3 else 2)]
        anchors = [turn(depth, 0), turn(0, depth)]
        if pose == 3 and kind != 2: origins.reverse(); anchors.reverse()
    fixed = row not in (0, 4)
    below = (3 if row == 3 else base_length(row)) * scale(row)
    gap = (20 if row == 11 else .5) * scale(row)
    above = (1 if row == 11 else .25) * scale(row)
    expected = {}
    for side, (origin, anchor) in enumerate(zip(origins, anchors)):
        if row == 10 or side == 0 and row == 8 or side == 1 and row == 9: continue
        delta = tuple(b - a for a, b in zip(origin, anchor)); distance = math.hypot(*delta)
        direction = tuple(x / distance for x in delta)
        start_at = max(gap, distance - below) if fixed else gap
        if fixed and start_at >= distance + above: continue
        name = 'EXT_FIRST' if side == 0 else 'EXT_OVERRIDE' if row == 3 else 'EXT_SECOND'
        expected[name] = (tuple(o + start_at * d for o, d in zip(origin, direction)) + (0.,),
                          tuple(a + above * d for a, d in zip(anchor, direction)) + (0.,))
    return expected


def close(actual, expected, label):
    require(len(actual) == len(expected) and all(math.isfinite(a) and abs(a-b) <= 1e-9 for a, b in zip(actual, expected)), label)


def physical_geometry(lines, kind, row, pose):
    expected = expected_lines(kind, row, pose)
    require(len(lines) == len(expected) + (1 if kind < 2 else 0), 'Unexpected geometry LINE count')
    ext = [r for r in lines if any(c == 6 and str(v).startswith('EXT_') for c, v in r)]
    require(len(ext) == len(expected) and {one(r, 6)[1] for r in ext} == set(expected), 'Per-side extension line inventory')
    for line in ext:
        name = one(line, 6)[1]; start, end = expected[name]
        close(tuple(one(line, c)[1] for c in (10, 20, 30)), start, 'Physical start')
        close(tuple(one(line, c)[1] for c in (11, 21, 31)), end, 'Physical end')
        require(one(line, 62)[1] == 3 and one(line, 370)[1] == 35, 'Physical extension color/lineweight')
    return ext


def inspect(path, year, binary, kind, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_tags(path); at = tags.index((9, '$ACADVER'))
    require(tags[at + 1] == (1, PROFILES[year]), 'Wrong physical version')
    entries = [tags[a:b] for a, b in records(tags)]
    hosts = [r for r in entries if r[0][1] in ('DIMENSION', 'ARC_DIMENSION')]
    require(len(hosts) == 12 and {one(r, 8)[1] for r in hosts} == {f'EXT_ROW_{r:02d}' for r in range(12)}, 'Physical host inventory')
    controls = 0
    for host in hosts:
        row = int(one(host, 8)[1].rsplit('_', 1)[1])
        require(one(host, 70)[1] & 15 == (1, 0, 2, 5, 7)[kind], 'Physical dimension type')
        style, = [r for r in entries if r[0] == (0, 'DIMSTYLE') and (2, f'EXT_STYLE_{row:02d}') in r]
        controls += corrupt(style, {49: float(base_length(row)), 290: int(row not in (0, 3)),
                                    40: float(scale(row)), 42: 20. if row == 11 else .5, 44: 1. if row == 11 else .25})
        block, = [r for r in entries if r[0] == (0, 'BLOCK_RECORD') and (2, one(host, 2)[1]) in r]
        handle = one(block, 5)[1]
        lines = [r for r in entries if r[0] == (0, 'LINE') and (330, handle) in r]
        ext = physical_geometry(lines, kind, row, placement)
        for line in ext:
            index = lines.index(line)
            for at in [i for i, (c, _) in enumerate(line) if c in (6, 10, 20, 30, 11, 21, 31, 62, 370)]:
                for operation in ('change', 'remove', 'duplicate', 'wrong-code'):
                    damaged = list(line); code, value = damaged[at]
                    if operation == 'change': damaged[at] = (code, value + '_BAD' if isinstance(value, str) else value + 1)
                    elif operation == 'remove': del damaged[at]
                    elif operation == 'duplicate': damaged.insert(at, damaged[at])
                    else: damaged[at] = (999, value)
                    bad = list(lines); bad[index] = damaged
                    controls += reject(lambda: physical_geometry(bad, kind, row, placement))
        # These are geometry inventory controls, including fully suppressed rows.
        extra = [(0, 'LINE'), (330, handle), (6, 'EXT_EXTRA')]
        controls += reject(lambda: physical_geometry(lines + [extra], kind, row, placement))
        if ext:
            missing = [r for r in lines if r is not ext[0]]
            controls += reject(lambda: physical_geometry(missing, kind, row, placement))
    doc = ezdxf.readfile(path); require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('EXT_PAPER') if placement == 1 else doc.blocks['EXT_HOLDER']
    dimensions = list(space.query('DIMENSION ARC_DIMENSION')); require(len(dimensions) == 12, 'Independent host placement/count')
    for dim in dimensions:
        row = int(dim.dxf.layer.rsplit('_', 1)[1]); require(dim.dxf.owner == space.block_record_handle, 'Independent host owner')
        require(dim.dxftype() == ('ARC_DIMENSION' if kind == 4 else 'DIMENSION'), 'Independent type')
        require(dim.dxf.dimtype & 15 == (1, 0, 2, 5, 7)[kind], 'Independent dimension family')
        style = doc.dimstyles.get(dim.dxf.dimstyle)
        require(style.dxf.dimfxl == base_length(row) and style.dxf.dimfxlon == int(row not in (0, 3)), 'Independent base fixed settings')
        wanted = {'dimtxt': .9} if row == 2 else {'dimfxlon': 1, 'dimfxl': 3., 'dimltex2': 'EXT_OVERRIDE'} if row == 3 else {'dimfxlon': 0} if row == 4 else {}
        require(dim.get_acad_dstyle(style) == wanted, 'Independent sparse overrides')
        block = dim.get_geometry_block(); expected = expected_lines(kind, row, placement)
        lines = [l for l in block.query('LINE') if l.dxf.linetype.startswith('EXT_')]
        require(len(lines) == len(expected) and {l.dxf.linetype for l in lines} == set(expected), 'Independent extension inventory')
        for line in lines:
            close(tuple(line.dxf.start), expected[line.dxf.linetype][0], 'Independent start')
            close(tuple(line.dxf.end), expected[line.dxf.linetype][1], 'Independent end')
            require(line.dxf.owner == block.block_record_handle and line.dxf.color == 3 and line.dxf.lineweight == 35, 'Independent line attributes')
        label, = block.query('MTEXT'); require(label.text == 'FIXED', 'Following label')
        require([(t.code, t.value) for t in dim.get_xdata('EXT_KEEP')] == [(1000, 'unchanged')], 'Other-application XData')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following geometry')
    if placement == 3: require(not list(doc.modelspace().query('INSERT')), 'Unreferenced block instantiated')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls


def main(directory):
    specs = [s for s in itertools.product(PROFILES, (False, True), range(5), range(4), ('source', 'False', 'True')) if not (s[0] == 2000 and s[2] == 4)]
    def name(s):
        year, binary, kind, place, output = s
        return f'dimension-extension-render-AutoCad{year}-{binary}-{kind}-{place}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, f'Extension inventory missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('dimension-extension-render-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'dimension-extension-render-extra.dxf'}))
    controls = 0
    for s in specs:
        year, binary, kind, place, output = s
        try:
            controls += inspect(directory / name(s), year, binary if output == 'source' else output == 'True', kind, place)
        except Exception as error:
            raise ValueError(f'{name(s)}: {error}') from error
    print(f'PASS: {len(specs)} extension drawings / {12 * len(specs)} independently loaded dimensions; '
          f'{controls} packet/geometry corruptions and two inventory controls rejected; no graph errors/repairs. '
          'Selected generated geometry is not native AutoCAD visual/fit qualification.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimension_extension_rendering.py ARTIFACTS')
    main(Path(sys.argv[1]))
