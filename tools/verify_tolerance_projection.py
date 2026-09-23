#!/usr/bin/env python3
"""Independent complete DSTYLE projection when the base hides an unequal lower bound."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one, corrupt

UPPER = (.25, .25, 2 * float.fromhex('0x0.0000000000001p-1022'), 0.)
LOWER = (.125, -0., -float.fromhex('0x0.0000000000001p-1022'), -0.)


def modes(row):
    base = 1 if row % 10 < 5 else 2
    target = base if row % 5 == 4 else row % 5
    return base, target


def native_lower(row, mode):
    high, low = UPPER[row // 10], LOWER[row // 10]
    return high if mode == 1 and high != low else low


def wanted(row):
    base, target = modes(row)
    result = {178: (1070, 3)}
    if row % 5 != 4:
        result.update({71: (1070, int(target in (1, 2))), 72: (1070, int(target == 3))})
        if native_lower(row, base) != native_lower(row, target):
            result[48] = (1040, native_lower(row, target))
    return result


def packet(record, row):
    apps = [i for i, t in enumerate(record) if t == (1001, 'ACAD')]
    require(len(apps) == 1, 'One override application')
    start = apps[0] + 1
    end = next((i for i in range(start, len(record)) if record[i][0] == 1001), len(record))
    body = record[start:end]
    require(body[:2] == [(1000, 'DSTYLE'), (1002, '{')] and body[-1:] == [(1002, '}')], 'Override framing')
    require((len(body) - 3) % 2 == 0, 'Complete pairs')
    fields = {}
    for i in range(2, len(body) - 1, 2):
        group, identifier = body[i]
        require(group == 1070 and identifier not in fields, 'Unique typed identifiers')
        fields[identifier] = body[i + 1]
    expected = wanted(row)
    require(fields.keys() == expected.keys(), 'Minimal projected field set')
    for code, value in expected.items():
        require(key(fields[code]) == key(value), 'Projected field bits/type')
    return list(range(start, end))


def inspect(path, year, binary, leader, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load_tags(path)
    require(tags[tags.index((9, '$ACADVER')) + 1] == (1, PROFILES[year]), 'Profile')
    entries = [tags[a:b] for a, b in records(tags)]
    hosts = [r for r in entries if r[0][1] in ('DIMENSION', 'LEADER')]
    require(len(hosts) == 40 and {one(r, 8)[1] for r in hosts} == {f'PROJECTION_{r:02}' for r in range(40)}, 'Complete hosts')
    count = 0
    for host in hosts:
        row = int(one(host, 8)[1][11:]); base, target = modes(row)
        require(host[0][1] == ('LEADER' if leader else 'DIMENSION'), 'Host kind')
        style, = [r for r in entries if r[0] == (0, 'DIMSTYLE') and (2, f'PROJECTION_STYLE_{row:02}') in r]
        count += corrupt(style, {47: UPPER[row // 10], 48: native_lower(row, base), 71: 1, 72: 0})
        for at in packet(host, row):
            for op in ('change', 'remove', 'duplicate', 'wrong-code'):
                bad = list(host); code, value = bad[at]
                if op == 'change': bad[at] = (code, value + '_BAD' if isinstance(value, str) else value + 1)
                elif op == 'remove': del bad[at]
                elif op == 'duplicate': bad.insert(at, bad[at])
                else: bad[at] = (999, value)
                count += reject(lambda: packet(bad, row))
    doc = ezdxf.readfile(path)
    space = doc.modelspace() if placement == 0 else doc.layouts.get('PROJECTION_PAPER') if placement == 1 else doc.blocks['PROJECTION_HOLDER']
    hosts = list(space.query('DIMENSION LEADER')); require(len(hosts) == 40, 'Independent host placement')
    for host in hosts:
        row = int(host.dxf.layer[11:]); base, target = modes(row)
        style = doc.dimstyles.get(host.dxf.dimstyle)
        require(host.dxf.owner == space.block_record_handle, 'Independent ownership')
        require(key((48, style.dxf.dimtm)) == key((48, native_lower(row, base))), 'Independent native base bits')
        fields = host.get_acad_dstyle(style)
        names = {178: 'dimclrt', 71: 'dimtol', 72: 'dimlim', 48: 'dimtm'}
        expected = {names[code]: value for code, value in wanted(row).items()}
        require(fields.keys() == expected.keys(), 'Independent sparse fields')
        for name, (code, value) in expected.items(): require(key((code, fields[name])) == key((code, value)), 'Independent field bits')
        effective = fields.get('dimtm', style.dxf.dimtm)
        require(key((48, effective)) == key((48, native_lower(row, target))), 'Independent effective lower')
        require([(t.code, t.value) for t in host.get_xdata('PROJECTION_KEEP')] == [(1000, 'unchanged')], 'Other application retained')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'No independent graph repairs')
    return count


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), (False, True), range(4), ('source', 'False', 'True')))
    def name(s):
        year, binary, leader, place, output = s
        return f'tolerance-projection-AutoCad{year}-{binary}-{leader}-{place}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, 'Missing/extra tolerance projection drawings')
    inventory({p.name for p in directory.glob('tolerance-projection-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'tolerance-projection-extra.dxf'}))
    count = 0
    for s in specs:
        year, binary, leader, place, output = s
        count += inspect(directory / name(s), year, binary if output == 'source' else output == 'True', leader, place)
    print(f'PASS: {len(specs)} drawings / {40 * len(specs)} independently read hosts; {count} packet corruptions and two inventory controls rejected; zero graph errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_tolerance_projection.py ARTIFACTS')
    main(Path(sys.argv[1]))
