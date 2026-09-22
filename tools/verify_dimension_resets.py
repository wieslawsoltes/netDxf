#!/usr/bin/env python3
"""Validate explicit default-arrow and no-fill overrides, not native CAD rendering."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject
from verify_dimlfac_fidelity import PROFILES, records

FIELDS = (343, 344, 341, 69)
NAMES = ('dimblk1', 'dimblk2', 'dimldrblk', 'dimtfill')

def selected(row, field):
    return row == field + 1 or row >= 5

def packet(record, row, arrow):
    apps = [i for i, tag in enumerate(record) if tag == (1001, 'ACAD')]
    require(len(apps) == 1, 'One ACAD application')
    start = apps[0] + 1
    end = next((i for i in range(start, len(record)) if record[i][0] == 1001), len(record))
    body = record[start:end]
    require(body[:3] == [(1000, 'NEIGHBOR'), (1000, 'DSTYLE'), (1002, '{')]
            and body[-2:] == [(1002, '}'), (1000, 'TAIL')], 'Opaque neighbors and DSTYLE framing')
    pairs = body[3:-2]
    require(len(pairs) % 2 == 0, 'Incomplete pair')
    actual = {}
    for i in range(0, len(pairs), 2):
        code, field = pairs[i]
        require(code == 1070 and field not in actual, 'Invalid or duplicate identifier')
        actual[field] = pairs[i+1]
    expected = {43: (1040, 11.), 140: (1040, .75)}
    for f, code in enumerate(FIELDS):
        if not selected(row, f):
            continue
        expected[code] = (1070, 2 if row == 6 else 0) if f == 3 else (1005, arrow if row == 6 else '0')
    if selected(row, 0) or selected(row, 1):
        expected[173] = (1070, 1)
    if row == 6:
        expected[70] = (1070, 3)
    require(actual.keys() == expected.keys(), 'Complete field set (absence differs from reset)')
    require(all(key(actual[k]) == key(v) for k, v in expected.items()), 'Wrong reset value/type/reference')
    return range(start, end)

def inspect(path, year, binary, kind, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_tags(path)
    av = tags.index((9, '$ACADVER')); require(tags[av+1] == (1, PROFILES[year]), 'Wrong version')
    entries = [tags[a:b] for a, b in records(tags)]
    arrow, = [r for r in entries if r[0] == (0, 'BLOCK_RECORD') and (2, 'RESET_OTHER_ARROW') in r]
    handle, = [v for c, v in arrow if c == 5]
    hosts = [r for r in entries if r[0][1] in ('DIMENSION', 'ARC_DIMENSION', 'LEADER')]
    require(len(hosts) == 7 and {v for r in hosts for c, v in r if c == 8} == {f'RESET_ROW_{r:02d}' for r in range(7)}, 'Host inventory')
    controls = 0
    for host in hosts:
        row, = [int(v.rsplit('_', 1)[1]) for c, v in host if c == 8]
        positions = packet(host, row, handle)
        for at in positions:
            for operation in ('change', 'remove', 'duplicate', 'wrong-code'):
                bad = list(host); code, value = bad[at]
                if operation == 'change': bad[at] = (code, value+'_BAD' if isinstance(value, str) else value+1)
                elif operation == 'remove': del bad[at]
                elif operation == 'duplicate': bad.insert(at, bad[at])
                else: bad[at] = (999, value)
                controls += reject(lambda: packet(bad, row, handle))
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('RESET_PAPER') if placement == 1 else doc.blocks['RESET_HOLDER']
    loaded = list(space.query('DIMENSION ARC_DIMENSION LEADER'))
    require(len(loaded) == 7, 'Independent placement/count')
    require({h.dxf.layer for h in loaded} == {f'RESET_ROW_{r:02d}' for r in range(7)}, 'Independent inventory')
    for host in loaded:
        row = int(host.dxf.layer.rsplit('_', 1)[1])
        require(host.dxf.owner == space.block_record_handle, 'Independent owner')
        require(host.dxftype() == ('LEADER' if kind == 8 else 'ARC_DIMENSION' if kind == 7 else 'DIMENSION'), 'Host family')
        style = doc.dimstyles.get(host.dxf.dimstyle)
        overrides = host.get_acad_dstyle(style)
        wanted = {'dimdli': 11., 'dimtxt': .75}
        for f, name in enumerate(NAMES):
            if selected(row, f): wanted[name] = (2 if row == 6 else 0) if f == 3 else 'RESET_OTHER_ARROW' if row == 6 else ''
        if selected(row, 0) or selected(row, 1): wanted['dimsah'] = 1
        if row == 6: wanted['dimtfillclr'] = 3
        require(overrides == wanted, 'Independent typed override names/values')
        require([(t.code, t.value) for t in host.get_xdata('RESET_KEEP')] == [(1000, 'unchanged')], 'Other application')
        require(style.dxf.dimtfill == 2 and style.dxf.dimtfillclr == 2, 'Base fill mutated')
        if kind != 8:
            text, = host.get_geometry_block().query('MTEXT');require(text.text == 'FIXED', 'Primary label')
            if kind == 1: require(len(host.get_geometry_block().query('INSERT')) == (0 if row == 5 else 1 if row in (1, 2) else 2), 'Generated arrow reset')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following geometry')
    audit = doc.audit();require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls

def main(directory):
    specs = [s for s in itertools.product(PROFILES, (False, True), range(9), range(4), ('source', 'False', 'True')) if not (s[0] == 2000 and s[2] == 7)]
    def name(s):
        year, binary, kind, placement, output = s
        return f'dimension-reset-AutoCad{year}-{binary}-{kind}-{placement}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, f'Reset inventory missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('dimension-reset-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'dimension-reset-extra.dxf'}))
    controls = 0
    for s in specs:
        year, binary, kind, placement, output = s
        controls += inspect(directory/name(s), year, binary if output == 'source' else output == 'True', kind, placement)
    print(f'PASS: {len(specs)} reset drawings / {7*len(specs)} independent dimension/leader records; {controls} actual-packet corruptions and two inventory controls rejected; no graph errors/repairs.')

if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimension_resets.py ARTIFACTS')
    main(Path(sys.argv[1]))
