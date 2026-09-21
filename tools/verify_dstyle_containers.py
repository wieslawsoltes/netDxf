#!/usr/bin/env python3
"""Verify DSTYLE values, reference targets and ownership in all container fixtures."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import key, load_tags, require, reject
from verify_dimlfac_fidelity import PROFILES, records

SCALARS = {144: (1040, -0.75), 140: (1040, 0.75), 145: (1040, -0.25),
           288: (1070, 1), 178: (1070, 4), 173: (1070, 1)}
REFERENCES = {340: ('STYLE', 'DSTYLE_TEXT'), 345: ('LTYPE', 'DSTYLE_LINES'),
              343: ('BLOCK_RECORD', 'DSTYLE_ARROW'), 341: ('BLOCK_RECORD', 'DSTYLE_ARROW')}


def packet(tags, targets):
    apps = [i for i, t in enumerate(tags) if t == (1001, 'ACAD')]
    require(len(apps) == 1, 'One ACAD override application required')
    start = apps[0] + 1
    end = next((i for i in range(start, len(tags)) if tags[i][0] == 1001), len(tags))
    body = tags[start:end]
    require(len(body) == 23 and body[:2] == [(1000, 'DSTYLE'), (1002, '{')]
            and body[-1] == (1002, '}'), 'DSTYLE framing/count')
    values = {}
    positions = []
    for at in range(2, len(body) - 1, 2):
        code, identifier = body[at]
        require(code == 1070 and identifier not in values, 'Override identifier/duplicate')
        values[identifier] = body[at + 1]; positions.append(start + at + 1)
    wanted = dict(SCALARS)
    wanted.update({code: (1005, targets[name]) for code, name in REFERENCES.items()})
    require(values.keys() == wanted.keys(), 'Override field set')
    for code, tag in wanted.items():
        require(key(values[code]) == key(tag), f'Override value or reference {code}')
    return positions


def inspect_file(path, year, binary, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load_tags(path)
    at = tags.index((9, '$ACADVER')); require(tags[at + 1] == (1, PROFILES[year]), 'Physical profile')
    all_records = [tags[a:b] for a, b in records(tags)]
    targets = {}
    for kind, name in set(REFERENCES.values()):
        found = [r for r in all_records if r[0] == (0, kind) and (2, name) in r]
        require(len(found) == 1, 'Named reference target')
        handles = [v for c, v in found[0] if c == 5]
        require(len(handles) == 1, 'Reference target identity'); targets[(kind, name)] = handles[0]
    selected = [r for r in all_records if r[0][1] in ('DIMENSION', 'LEADER')]
    require(len(selected) == 8, 'Physical dimension/leader count')
    corruptions = 0
    for record in selected:
        positions = packet(record, targets)
        for at in positions:
            for operation in ('zero', 'remove', 'duplicate', 'wrong-group'):
                bad = list(record)
                if operation == 'zero': bad[at] = (bad[at][0], '0' if bad[at][0] == 1005 else 0.0)
                elif operation == 'remove': del bad[at]
                elif operation == 'duplicate': bad.insert(at, bad[at])
                else: bad[at] = (1071, bad[at][1])
                corruptions += reject(lambda: packet(bad, targets))
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent profile')
    if placement == 0: space = doc.modelspace()
    elif placement < 3: space = doc.layouts.get('DSTYLE_PAPER_A' if placement == 1 else 'DSTYLE_PAPER_B')
    else: space = doc.blocks['DSTYLE_CONTAINER']
    entities = list(space.query('DIMENSION LEADER'))
    require(len(entities) == 8 and {e.dxf.layer for e in entities} == {f'DSTYLE_ENTITY_{i}' for i in range(8)}, 'Independent families/placement')
    require({e.dxf.dimtype & 7 for e in entities if e.dxftype() == 'DIMENSION'} == set(range(7)), 'All seven dimension types')
    expected = {'dimlfac': -0.75, 'dimtxt': 0.75, 'dimtvp': -0.25, 'dimupt': 1,
                'dimclrt': 4, 'dimsah': 1, 'dimtxsty': 'DSTYLE_TEXT',
                'dimltype': 'DSTYLE_LINES', 'dimblk1': 'DSTYLE_ARROW', 'dimldrblk': 'DSTYLE_ARROW'}
    for entity in entities:
        require(entity.dxf.owner == space.block_record_handle, 'Independent owner')
        overrides = entity.get_acad_dstyle(doc.dimstyles.get(entity.dxf.dimstyle))
        require(overrides == expected, 'Independent override values/reference names')
        require([(t.code, t.value) for t in entity.get_xdata('DSTYLE_KEEP')] == [(1000, 'preserved')], 'Unrelated XData')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following line')
    if placement == 4:
        require(len(doc.modelspace().query('INSERT[name=="DSTYLE_OUTER"]')) == 2 and
                len(doc.blocks['DSTYLE_OUTER'].query('INSERT[name=="DSTYLE_CONTAINER"]')) == 2, 'Repeated nested inserts')
    if placement == 5:
        require(not any(e.dxftype() == 'INSERT' and e.dxf.name == 'DSTYLE_CONTAINER' for e in doc.entitydb.values()), 'Unreferenced block was instantiated')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return corruptions


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), range(6), ('source', 'False', 'True')))
    def name(spec):
        year, source, placement, output = spec
        return f'dstyle-containers-AutoCad{year}-{source}-{placement}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, 'Missing or extra DSTYLE container drawings')
    inventory({p.name for p in directory.glob('dstyle-containers-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'dstyle-containers-extra.dxf'}))
    corruptions = 0
    for spec in specs:
        year, source, placement, output = spec
        corruptions += inspect_file(directory / name(spec), year, source if output == 'source' else output == 'True', placement)
    print(f'PASS: {len(specs)} DSTYLE container drawings / {8*len(specs)} independently loaded dimension/leader records; '
          f'{corruptions} actual-tag corruptions and two inventory controls rejected; zero graph errors/repairs. '
          'Stored overrides and reference resolution do not certify rendering or private-cache regeneration.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dstyle_containers.py ARTIFACTS')
    main(Path(sys.argv[1]))
