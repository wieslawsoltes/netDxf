#!/usr/bin/env python3
"""Check mode-only tolerance overrides against serialized base values, independently."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one, corrupt

BOUNDS = ((.25, .125), (-.5, .25), (float.fromhex('0x0.0000000000001p-1022'), 0.),
          (0., -0.), (1e-300, -1e-300), (.25, .25))


def lower(row, base=False):
    upper, value = BOUNDS[row % len(BOUNDS)]
    return upper if (base or row // len(BOUNDS) == 1) and upper != value else value


def packet(tags, row):
    starts = [i for i, tag in enumerate(tags) if tag == (1001, 'ACAD')]
    require(len(starts) == 1, 'Exactly one ACAD application')
    start = starts[0] + 1
    end = next((i for i in range(start, len(tags)) if tags[i][0] == 1001), len(tags))
    body = tags[start:end]
    require(body[:2] == [(1000, 'DSTYLE'), (1002, '{')] and body[-1:] == [(1002, '}')]
            and len(body) % 2 == 1, 'Balanced DSTYLE pair sequence')
    actual = {}
    positions = []
    for at in range(2, len(body) - 1, 2):
        code, identifier = body[at]
        require(code == 1070 and identifier not in actual, 'Typed unique identifier')
        actual[identifier] = body[at + 1]
        positions += [start + at, start + at + 1]
    mode = row // len(BOUNDS)
    expected = {140: (1040, .75), 71: (1070, int(mode in (1, 2))), 72: (1070, int(mode == 3))}
    if lower(row, True) != lower(row):
        expected[48] = (1040, lower(row))
    require(actual.keys() == expected.keys(), 'Exact minimal override inventory')
    for identifier, tag in expected.items():
        require(key(actual[identifier]) == key(tag), 'Wrong mode, lower bound, scalar or wire type')
    return positions


def inspect(path, year, binary, leader, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load_tags(path)
    require(tags[tags.index((9, '$ACADVER')) + 1] == (1, PROFILES[year]), 'Physical version')
    entries = [tags[a:b] for a, b in records(tags)]
    hosts = [r for r in entries if r[0][1] in ('DIMENSION', 'LEADER')]
    require(len(hosts) == 24 and {one(r, 8)[1] for r in hosts} == {f'TRANSITION_{i:02d}' for i in range(24)}, 'Physical host inventory')
    controls = 0
    for host in hosts:
        row = int(one(host, 8)[1].rsplit('_', 1)[1])
        require(host[0] == (0, 'LEADER' if leader else 'DIMENSION'), 'Physical host type')
        styles = [r for r in entries if r[0] == (0, 'DIMSTYLE') and (2, f'TRANSITION_{row:02d}') in r]
        require(len(styles) == 1, 'Base style identity')
        controls += corrupt(styles[0], {47: BOUNDS[row % 6][0], 48: lower(row, True), 71: 1, 72: 0})
        for at in packet(host, row):
            for operation in ('remove', 'duplicate', 'type', 'value'):
                damaged = list(host)
                code, value = damaged[at]
                if operation == 'remove': del damaged[at]
                elif operation == 'duplicate': damaged.insert(at, damaged[at])
                elif operation == 'type': damaged[at] = (999, value)
                else: damaged[at] = (code, value + 1)
                controls += reject(lambda: packet(damaged, row))
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('TRANSITION_PAPER') if placement == 1 else doc.blocks['TRANSITION_HOLDER']
    loaded = list(space.query('DIMENSION LEADER'))
    require(len(loaded) == 24 and {h.dxf.layer for h in loaded} == {f'TRANSITION_{i:02d}' for i in range(24)}, 'Independent placement/count')
    for host in loaded:
        row = int(host.dxf.layer.rsplit('_', 1)[1]); mode = row // 6
        require(host.dxf.owner == space.block_record_handle and host.dxftype() == ('LEADER' if leader else 'DIMENSION'), 'Independent ownership/type')
        style = doc.dimstyles.get(host.dxf.dimstyle)
        require(key((48, style.dxf.dimtm)) == key((48, lower(row, True))), 'Independent base lower bits')
        wanted = {'dimtxt': .75, 'dimtol': int(mode in (1, 2)), 'dimlim': int(mode == 3)}
        if lower(row, True) != lower(row): wanted['dimtm'] = lower(row)
        overrides = host.get_acad_dstyle(style)
        require(overrides == wanted, 'Independent complete sparse override')
        effective = overrides.get('dimtm', style.dxf.dimtm)
        require(key((48, effective)) == key((48, lower(row))), 'Independent effective lower bits')
        require([(t.code, t.value) for t in host.get_xdata('TRANSITION_KEEP')] == [(1000, 'unchanged')], 'Unrelated application data')
        if not leader:
            label, = host.get_geometry_block().query('MTEXT')
            require(label.text == 'FIXED', 'Fixed user label')
    if placement >= 2:
        require(len(doc.modelspace().query('INSERT')) == int(placement == 2), 'Referenced/unreferenced block policy')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following line')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent database errors/repairs')
    return controls


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), (False, True), range(4), ('source', 'False', 'True')))
    def name(s):
        year, binary, leader, placement, output = s
        return f'tolerance-mode-transition-AutoCad{year}-{binary}-{leader}-{placement}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, 'Incomplete/extra tolerance-transition drawing inventory')
    inventory({p.name for p in directory.glob('tolerance-mode-transition-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'tolerance-mode-transition-extra.dxf'}))
    controls = 0
    for spec in specs:
        year, binary, leader, placement, output = spec
        controls += inspect(directory/name(spec), year, binary if output == 'source' else output == 'True', leader, placement)
    print(f'PASS: {len(specs)} transition drawings / {24*len(specs)} independently read hosts; '
          f'{controls} actual-packet corruptions and two inventory controls rejected; zero database errors/repairs. '
          'Native AutoCAD execution and font/fit rendering are not qualified.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_tolerance_mode_transitions.py ARTIFACTS')
    main(Path(sys.argv[1]))
