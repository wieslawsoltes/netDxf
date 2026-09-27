#!/usr/bin/env python3
"""Verify populated LAYER/state identities and complete packets without handle normalization."""
import io
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from verify_layer_state_identity import canonical, one, require, reject, records, PROFILES
from verify_raw_line_geometry import audit_signature

STAGES = ('source', 'output', 'resave', 'repeat')


def load(path, year):
    data = path.read_bytes()
    stream = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else \
        ascii_tags_loader(io.StringIO(data.decode('utf-8' if year >= 2007 else 'cp1252'), newline=None))
    return [(tag.code, cast_tag_value(tag.code, tag.value)) for tag in stream]


def check(rows, count, year):
    by_handle = {}
    for row in rows:
        if row[0] in ((0, 'SECTION'), (0, 'ENDSEC'), (0, 'EOF')):
            continue
        ids = [v for c, v in row if c in (5, 105)]
        if ids:
            require(len(ids) == 1, 'Duplicate record handle declaration')
            identity = canonical(ids[0])
            require(identity not in by_handle, 'Duplicate physical object identity')
            by_handle[identity] = row
    tables = [r for r in rows if r[:2] == [(0, 'TABLE'), (2, 'LAYER')]]
    require(len(tables) == 1, 'LAYER table inventory')
    table = tables[0]
    parent_id = canonical(one(table, 360)); parent = by_handle.get(parent_id)
    require(parent is not None, 'Missing LAYER extension dictionary')
    child_id = canonical(one(parent, 360)); child = by_handle.get(child_id)
    require(parent == [(0, 'DICTIONARY'), (5, parent_id), (330, one(table, 5)),
                       (100, 'AcDbDictionary'), (280, 1), (281, 1), (3, 'ACAD_LAYERSTATES'), (360, child_id)],
            'Unexpected outer dictionary packet')
    require(child is not None and child[:6] == [(0, 'DICTIONARY'), (5, child_id), (330, parent_id),
                        (100, 'AcDbDictionary'), (280, 1), (281, 1)], 'Unexpected states dictionary header')
    require(len(child) == 6 + 2 * count, 'State dictionary count')
    layers = {one(r, 2): one(r, 5) for r in rows if r[0] == (0, 'LAYER')}
    linetypes = {one(r, 2): one(r, 5) for r in rows if r[0] == (0, 'LTYPE')}
    require(set(layers) == {'0', 'Walls', 'Details'}, 'Layer inventory changed')
    states = []
    for i in range(count):
        require(child[6+2*i] == (3, f'State_{i}') and child[7+2*i][0] == 350, 'State name/entry kind/order')
        state_id = canonical(child[7+2*i][1]); state = by_handle.get(state_id)
        description = f'Saved Δ layer state {i}' if year >= 2007 else f'Saved \\U+0394 layer state {i}'
        expected = [(0, 'XRECORD'), (5, state_id), (102, '{ACAD_REACTORS'), (330, child_id),
                    (102, '}'), (330, child_id), (100, 'AcDbXrecord'), (280, 1), (91, 2047),
                    (301, description), (290, i % 2), (302, 'Details' if i % 2 else 'Walls')]
        for name, flags, color, weight, alpha in [('0', 8, 7, -3, 0), ('Walls', 9, 3+i, 30, 33554623), ('Details', 2, 147, -3, 0)]:
            expected.extend([(330, layers[name]), (90, flags), (62, color), (370, weight),
                             (331, linetypes['Continuous']), (440, alpha)])
            if name == 'Details':
                expected.append((92, -1038858622))  # netDxf's retained signed true-color representation.
        require(state == expected, 'Complete state packet/value/owner/reactor mismatch')
        states.append(tuple(state))
    require(len({one(s, 5) for s in states}) == count, 'Aliased state records')
    lines = [r for r in rows if r[0] == (0, 'LINE')]
    require(len(lines) == 1, 'Following LINE inventory')
    for code, value in {10:1., 20:2., 30:3., 11:4., 21:5., 31:6.}.items():
        require(one(lines[0], code) == value, 'Following geometry changed')
    return tuple(table), tuple(parent), tuple(child), tuple(states), tuple(lines[0])


def compare(before, after):
    require(before == after, 'Cross-save identity or full state packet changed')


def inspect(path, year, binary, count):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load(path, year); at = tags.index((9, '$ACADVER'))
    require(tags[at+1] == (1, PROFILES[year]), 'Wrong version')
    rows = records(tags); selected = check(rows, count, year)
    doc = ezdxf.readfile(path)
    parent = doc.entitydb[one(selected[1], 5)]; child = doc.entitydb[one(selected[2], 5)]
    require(parent['ACAD_LAYERSTATES'] is child and len(child) == count, 'Independent membership mismatch')
    for i, packet in enumerate(selected[3]):
        state = child[f'State_{i}']; require(state.dxf.handle == one(packet, 5) and state.dxf.owner == child.dxf.handle, 'Independent state identity/owner')
        require([decode_dxf_unicode(t.value) for t in state.tags if t.code == 301] == [f'Saved Δ layer state {i}'], 'Independent state description')
        require([t.value for t in state.tags if t.code == 62] == [7, 3+i, 147], 'Independent state ACI values')
    require(not any(any(values.values()) for values in audit_signature(doc)), 'Independent audit errors/repairs')
    controls = 0
    # Each selected dictionary/state field is protected against omission, duplication
    # and value changes. These are mutations of actual exported packets.
    for packet in (selected[1], selected[2], *selected[3]):
        index = next(i for i, r in enumerate(rows) if tuple(r) == packet)
        for offset in range(1, len(packet)):
            for mode in ('remove', 'duplicate', 'value'):
                bad = list(rows); row = list(packet)
                if mode == 'remove': row.pop(offset)
                elif mode == 'duplicate': row.insert(offset, row[offset])
                else:
                    code, value = row[offset]
                    row[offset] = (code, '0' if code in (5, 330, 350, 360) else value + '_changed' if isinstance(value, str) else value + 1)
                bad[index] = row
                controls += reject(lambda: check(bad, count, year))
    return selected, controls


def main(directory):
    names = {f'layer-state-populated-AutoCad{year}-{transport}-{count}-{stage}.dxf'
             for year, transport, count, stage in itertools.product(PROFILES, ('text', 'binary'), (1, 3), STAGES)}
    def inventory(actual): require(actual == names, 'Missing/extra populated-state drawings')
    inventory({p.name for p in directory.glob('layer-state-populated-*.dxf')})
    controls = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport, count in itertools.product(PROFILES, ('text', 'binary'), (1, 3)):
        source = None
        for stage in STAGES:
            path = directory / f'layer-state-populated-AutoCad{year}-{transport}-{count}-{stage}.dxf'
            selected, rejected = inspect(path, year, (transport == 'binary') != (stage == 'output'), count)
            controls += rejected
            if source is None: source = selected
            else:
                compare(source, selected)
                # A globally coherent new identity must still fail the cross-save check.
                old = one(selected[3][0], 5)
                def rename(value):
                    if isinstance(value, tuple): return tuple(rename(v) for v in value)
                    return 'ABCDEF' if value == old else value
                controls += reject(lambda: compare(source, rename(selected)))
    print(f'PASS: {len(names)} drawings / 192 retained state records; {controls} corrupted packets/inventories rejected; '
          'exact identities, values and ownership with zero independent audit errors/repairs. No native acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_populated_layer_state_identity.py ARTIFACTS')
    main(Path(sys.argv[1]))
