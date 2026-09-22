#!/usr/bin/env python3
"""Independently check inherited components of compact dimension override values."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one, corrupt

CODES = (78, 79, 285, 284, 286, 273)
NAMES = ('dimzin', 'dimazin', 'dimaltz', 'dimtzin', 'dimalttz', 'dimaltu')


def row_values(group, row):
    if group == 5:
        seed = row-1 if row else 2
        unit = 4 if seed < 4 else 5
        stacked = bool(seed // 2 & 1)
        original = unit if stacked else unit+2
        if row:
            if seed % 2: stacked = not stacked
            else: unit = 5 if unit == 4 else 4
        return original, unit if stacked else unit+2
    bits = 2 if group == 1 else 4
    base = row-1 if row else (1 << bits)-1
    edited = base ^ (1 << (base % bits)) if row else base
    def code(mask):
        if group == 1: return mask
        # DXF's low two bits encode the feet/inches policy, not ordinary bit flags.
        low = (0 if mask & 8 else 3) if mask & 4 else (2 if mask & 8 else 1)
        return low | (4 if mask & 1 else 0) | (8 if mask & 2 else 0)
    return code(base), code(edited)


def packet(record, group, row):
    apps = [i for i, t in enumerate(record) if t == (1001, 'ACAD')]
    require(len(apps) == 1, 'One ACAD application')
    start = apps[0]+1
    end = next((i for i in range(start, len(record)) if record[i][0] == 1001), len(record))
    wanted = [(1000, 'DSTYLE'), (1002, '{'), (1070, 140), (1040, 0.75)]
    if row: wanted.extend(((1070, CODES[group]), (1070, row_values(group, row)[1])))
    wanted.append((1002, '}'))
    require([key(t) for t in record[start:end]] == [key(t) for t in wanted], 'Exact composite/scalar packet')
    return range(start, end)


def inspect(path, year, binary, leader, placement, group):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load_tags(path)
    av = tags.index((9, '$ACADVER')); require(tags[av+1] == (1, PROFILES[year]), 'Physical profile')
    entries = [tags[a:b] for a, b in records(tags)]
    hosts = [r for r in entries if r[0][1] in ('DIMENSION', 'LEADER')]
    count = 9 if group == 5 else 5 if group == 1 else 17
    require(len(hosts) == count and {one(r, 8)[1] for r in hosts} == {f'COMPOSITE_{r:02d}' for r in range(count)}, 'Physical host inventory')
    controls = 0
    for host in hosts:
        row = int(one(host, 8)[1].split('_')[-1])
        require(host[0][1] == ('LEADER' if leader else 'DIMENSION'), 'Host type')
        original, edited = row_values(group, row)
        style, = [r for r in entries if r[0] == (0, 'DIMSTYLE') and (2, f'COMPOSITE_STYLE_{row:02d}') in r]
        controls += corrupt(style, {CODES[group]: original})
        for at in packet(host, group, row):
            for operation in ('change', 'remove', 'duplicate', 'wrong-code'):
                bad = list(host); code, value = bad[at]
                if operation == 'change': bad[at] = (code, value+'_BAD' if isinstance(value, str) else value+1)
                elif operation == 'remove': del bad[at]
                elif operation == 'duplicate': bad.insert(at, bad[at])
                else: bad[at] = (1071, value)
                controls += reject(lambda: packet(bad, group, row))
        if not leader:
            controls += corrupt(host, {1: 'FIXED', 70: 33, 13: 0., 23: 0., 14: 10., 24: 0.})
    doc = ezdxf.readfile(path); require(doc.dxfversion == PROFILES[year], 'Independent profile')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('COMPOSITE_PAPER') if placement == 1 else doc.blocks['COMPOSITE_HOLDER']
    entities = list(space.query('DIMENSION LEADER')); require(len(entities) == count, 'Independent placement/count')
    for host in entities:
        row = int(host.dxf.layer.split('_')[-1]); original, edited = row_values(group, row)
        require(host.dxf.owner == space.block_record_handle, 'Independent owner')
        style = doc.dimstyles.get(host.dxf.dimstyle)
        require(style.dxf.get(NAMES[group]) == original, 'Independent base composite')
        expected = {'dimtxt': 0.75}
        if row: expected[NAMES[group]] = edited
        require(host.get_acad_dstyle(style) == expected, 'Independent effective composite/absence')
        require([(t.code, t.value) for t in host.get_xdata('COMPOSITE_KEEP')] == [(1000, 'unchanged')], 'Other application XData')
        if not leader:
            require(tuple(host.dxf.defpoint2) == (0., 0., 0.) and tuple(host.dxf.defpoint3) == (10., 0., 0.), 'Independent geometry')
            text, = host.get_geometry_block().query('MTEXT'); require(text.text == 'FIXED', 'Unchanged literal label')
    if placement == 2:
        insert, = doc.modelspace().query('INSERT'); require(insert.dxf.name == 'COMPOSITE_HOLDER', 'Block reference')
    if placement == 3:
        require(not any(e.dxftype() == 'INSERT' and e.dxf.name == 'COMPOSITE_HOLDER' for e in doc.entitydb.values()), 'Unreferenced block')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls, count


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), (False, True), range(4), range(6), ('source', 'False', 'True')))
    def name(spec):
        year, binary, leader, placement, group, output = spec
        return f'dimension-composite-AutoCad{year}-{binary}-{leader}-{placement}-{group}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, f'Composite inventory missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('dimension-composite-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)})); reject(lambda: inventory(expected | {'dimension-composite-extra.dxf'}))
    controls = records = 0
    for spec in specs:
        year, binary, leader, placement, group, output = spec
        n, r = inspect(directory/name(spec), year, binary if output == 'source' else output == 'True', leader, placement, group)
        controls += n; records += r
    print(f'PASS: {len(specs)} composite drawings / {records} independent dimension/leader records; '
          f'{controls} actual-packet corruptions and two inventory controls rejected; zero graph errors/repairs. '
          'Compact stored settings are checked, not native units/zero-suppression rendering.')

if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimension_composite_overrides.py ARTIFACTS')
    main(Path(sys.argv[1]))
