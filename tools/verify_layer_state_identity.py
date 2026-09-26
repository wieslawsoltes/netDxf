#!/usr/bin/env python3
"""Check canonical empty LAYER dictionary identities without handle normalization."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_raw_line_geometry import audit_signature

PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021', 2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}
STAGES = ('source', 'repeat', 'output', 'resave')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def reject(action):
    try:
        action()
    except ValueError:
        return 1
    raise ValueError('Corrupted identity evidence was accepted')


def one(row, code):
    values = [value for c, value in row if c == code]
    require(len(values) == 1, f'Missing or duplicated group {code}')
    return values[0]


def canonical(handle):
    require(type(handle) is str and 0 < len(handle) <= 16
            and all(c in '0123456789ABCDEF' for c in handle), 'Invalid canonical identity')
    number = int(handle, 16)
    require(0 < number < 0x7fffffffffffffff and f'{number:X}' == handle, 'Invalid/zero/noncanonical handle')
    return handle


def records(tags):
    offsets = [i for i, tag in enumerate(tags) if tag[0] == 0]
    return [tags[a:b] for a, b in zip(offsets, offsets[1:] + [len(tags)])]


def check(rows):
    # Each record's identity must be globally unique, not only unique within the
    # selected dictionary pair. HEADER's $HANDSEED is not a database identity.
    indexed = {}
    for row in rows:
        if row[0] in ((0, 'SECTION'), (0, 'ENDSEC'), (0, 'EOF')):
            continue
        ids = [v for c, v in row if c in (5, 105)]
        if ids:
            require(len(ids) == 1, 'Duplicate identity declaration')
            handle = canonical(ids[0]); require(handle not in indexed, 'Duplicate global record identity')
            indexed[handle] = row
    tables = [r for r in rows if r[:2] == [(0, 'TABLE'), (2, 'LAYER')]]
    require(len(tables) == 1, 'Missing/duplicate LAYER table')
    table = tables[0]; table_id = canonical(one(table, 5))
    outer_id = canonical(one(table, 360))
    require(table.count((102, '{ACAD_XDICTIONARY')) == 1 and table.count((102, '}')) == 1, 'LAYER extension group framing')
    at = table.index((102, '{ACAD_XDICTIONARY'))
    require(table[at:at + 3] == [(102, '{ACAD_XDICTIONARY'), (360, outer_id), (102, '}')], 'LAYER extension group order')
    require(one(table, 330) == '0', 'LAYER table owner changed')
    outer = indexed.get(outer_id)
    require(outer is not None and outer[0] == (0, 'DICTIONARY'), 'LAYER extension does not resolve to a dictionary')
    inner_id = canonical(one(outer, 360)); inner = indexed.get(inner_id)
    require(len({table_id, outer_id, inner_id}) == 3, 'Aliased dictionary/table identities')
    require(outer == [(0, 'DICTIONARY'), (5, outer_id), (330, table_id), (100, 'AcDbDictionary'),
                      (280, 1), (281, 1), (3, 'ACAD_LAYERSTATES'), (360, inner_id)], 'Outer dictionary shape/owner/flags changed')
    require(inner == [(0, 'DICTIONARY'), (5, inner_id), (330, outer_id), (100, 'AcDbDictionary'),
                      (280, 1), (281, 1)], 'Inner dictionary shape/owner/empty state changed')
    lines = [r for r in rows if r[0] == (0, 'LINE')]
    require(len(lines) == 1, 'Following LINE inventory')
    line = lines[0]
    for code, value in {10:1., 20:2., 30:3., 11:4., 21:5., 31:6.}.items():
        require(struct.pack('>d', one(line, code)) == struct.pack('>d', value), 'Following geometry bits changed')
    return tuple(table), tuple(outer), tuple(inner), tuple(line)


def controls(rows):
    selected = check(rows); count = 0
    for part in selected[:3]:
        index = next(i for i, row in enumerate(rows) if tuple(row) == part)
        for code in (5, 330, 360, 280, 281, 3):
            positions = [i for i, tag in enumerate(part) if tag[0] == code]
            for position in positions:
                for mode in ('remove', 'duplicate', 'change'):
                    damaged = list(rows); row = list(part)
                    if mode == 'remove':
                        row.pop(position)
                    elif mode == 'duplicate':
                        row.insert(position, row[position])
                    else:
                        value = row[position][1]
                        row[position] = (code, '0' if code in (5, 330, 360) and value != '0' else
                                         'FFFFFFFF' if code in (5, 330, 360) else
                                         value + '_bad' if isinstance(value, str) else value ^ 1)
                    damaged[index] = row
                    count += reject(lambda: check(damaged))
        damaged = rows[:index] + rows[index+1:]
        count += reject(lambda: check(damaged))
        count += reject(lambda: check(rows + [list(part)]))
    # Duplicate the structural extension group even though its target is valid.
    table = list(selected[0]); index = next(i for i, r in enumerate(rows) if tuple(r) == selected[0])
    at = table.index((102, '{ACAD_XDICTIONARY')); table[at:at] = table[at:at+3]
    damaged = list(rows); damaged[index] = table
    count += reject(lambda: check(damaged))
    return count


def compare(expected, actual):
    require(expected == actual, 'Dictionary identity/ownership or unselected LINE/table data changed across save')


def inspect(path, version, binary):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Unexpected transport')
    tags = load_visibility_tags(path); at = tags.index((9, '$ACADVER'))
    require(tags[at+1] == (1, PROFILES[version]), 'Unexpected version')
    rows = records(tags); selection = check(rows)
    doc = ezdxf.readfile(path)
    table, outer, inner, line = selection
    for row in (outer, inner):
        entity = doc.entitydb[one(row, 5)]
        require(entity.dxftype() == 'DICTIONARY' and entity.dxf.owner == one(row, 330), 'Independent dictionary resolution/owner')
    parent = doc.entitydb[one(outer, 5)]; child = doc.entitydb[one(inner, 5)]
    require(list(parent.keys()) == ['ACAD_LAYERSTATES'] and parent['ACAD_LAYERSTATES'] is child and len(child) == 0,
            'Independent dictionary membership')
    entity = doc.entitydb[one(line, 5)]
    require(tuple(entity.dxf.start) == (1.,2.,3.) and tuple(entity.dxf.end) == (4.,5.,6.), 'Independent LINE geometry')
    require(not any(any(values.values()) for values in audit_signature(doc)), 'Independent audit errors/repairs')
    at = tags.index((9, '$HANDSEED'))
    return selection, tags[at+1], controls(rows)


def main(directory):
    names = {f'layer-state-identity-AutoCad{v}-{transport}-{stage}.dxf'
             for v, transport, stage in itertools.product(PROFILES, ('text','binary'), STAGES)}
    def inventory(actual):
        require(actual == names, 'Missing/extra identity drawings')
    inventory({p.name for p in directory.glob('layer-state-identity-*.dxf')})
    count = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for version, transport in itertools.product(PROFILES, ('text','binary')):
        before = source_seed = None
        for stage in STAGES:
            path = directory/f'layer-state-identity-AutoCad{version}-{transport}-{stage}.dxf'
            value, seed, rejected = inspect(path, version, (transport == 'binary') != (stage == 'output'))
            count += rejected
            if before is None:
                before, source_seed = value, seed
            else:
                compare(before, value)
                # A coherent replacement of a dictionary pair could pass each file's
                # structure checks. Its different IDs must still fail cross-save comparison.
                renamed = tuple(tuple((c, 'ABCDEF' if v == one(value[2], 5) else v) for c, v in row) for row in value)
                count += reject(lambda: compare(before, renamed))
                if stage == 'repeat':
                    require(seed == source_seed, 'Same-instance save advanced handle seed')
    print(f'PASS: {len(names)} actual drawings, exact LAYER/dictionary identities and owners across four generations, '
          f'{count} corruption/inventory controls rejected; zero independent audit errors/repairs. '
          'Canonical empty pairs only; no identity normalization or native AutoCAD acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_layer_state_identity.py ARTIFACTS')
    main(Path(sys.argv[1]))
