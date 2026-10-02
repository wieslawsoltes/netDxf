#!/usr/bin/env python3
"""Verify actual LAS transfers and DXF saved-state packets independently of netDxf."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_populated_layer_state_identity import load
from verify_layer_state_identity import records, one, canonical, require, reject, PROFILES
from verify_raw_line_geometry import audit_signature

DESCRIPTION = 'Saved Δ € 🚀 layer state'
STAGES = ('source', 'output', 'resave')
LAS = [(0, 'LAYERSTATEDICTIONARY'), (0, 'LAYERSTATE'), (1, 'Transfer'), (91, '2047'),
       (301, DESCRIPTION), (290, '1'), (302, 'Walls'), (8, '0'), (90, '8'), (62, '7'),
       (370, '-3'), (6, 'Continuous'), (440, '0'), (8, 'Walls'), (90, '12'), (62, '1'),
       (370, '30'), (6, 'Continuous'), (440, '0')]


def check_las(data):
    require(type(data) is bytes, 'LAS byte input')
    try:
        text = data.decode('utf-8')
    except UnicodeDecodeError as error:
        raise ValueError('LAS Unicode') from error
    # Normalize transport CRLF only, not data, field order, identities or values.
    lines = text.replace('\r\n', '\n').split('\n')
    require(lines[-1] == '' and len(lines) == 2 * len(LAS) + 1, 'LAS framing/inventory')
    require(lines[:-1] == [v for c, value in LAS for v in (str(c), value)], 'LAS fixed packet differs')


def inspect_rows(rows, year):
    indexed = {}
    for row in rows:
        if row[0] in ((0, 'SECTION'), (0, 'ENDSEC'), (0, 'EOF')):
            continue
        ids = [v for c, v in row if c in (5, 105)]
        if ids:
            require(len(ids) == 1, 'Physical identity count')
            handle = canonical(ids[0]); require(handle not in indexed, 'Duplicate identity')
            indexed[handle] = row
    table, = [r for r in rows if r[:2] == [(0, 'TABLE'), (2, 'LAYER')]]
    outer = indexed[one(table, 360)]; inner = indexed[one(outer, 360)]
    require(outer == [(0, 'DICTIONARY'), (5, one(outer, 5)), (330, one(table, 5)),
                     (100, 'AcDbDictionary'), (280, 1), (281, 1), (3, 'ACAD_LAYERSTATES'),
                     (360, one(inner, 5))], 'LAYER extension packet')
    require(inner == [(0, 'DICTIONARY'), (5, one(inner, 5)), (330, one(outer, 5)),
                     (100, 'AcDbDictionary'), (280, 1), (281, 1), (3, 'Transfer'),
                     (350, one(inner, 350))], 'Saved-state dictionary packet')
    state = indexed[one(inner, 350)]
    require(len([r for r in rows if r[0] == (0, 'XRECORD')]) == 1, 'Saved-state record inventory')
    layers = {one(r, 2): r for r in rows if r[0] == (0, 'LAYER')}
    require(set(layers) == {'0', 'Walls'}, 'Layer inventory')
    types = {one(r, 2): one(r, 5) for r in rows if r[0] == (0, 'LTYPE')}
    require(set(types) == {'ByLayer', 'ByBlock', 'Continuous'}, 'Linetype inventory')
    description = DESCRIPTION if year >= 2007 else r'Saved \U+0394 \U+20AC \U+D83D\U+DE80 layer state'
    wanted = [(0, 'XRECORD'), (5, one(state, 5)), (102, '{ACAD_REACTORS'), (330, one(inner, 5)),
              (102, '}'), (330, one(inner, 5)), (100, 'AcDbXrecord'), (280, 1), (91, 2047),
              (301, description), (290, 1), (302, 'Walls')]
    for name, flags, color, weight in [('0', 8, 7, -3), ('Walls', 12, 1, 30)]:
        layer = layers[name]
        require(one(layer, 62) == color and one(layer, 370) == weight
                and one(layer, 6) == 'Continuous' and one(layer, 70) == (flags & 4), 'Restored live layer values')
        wanted += [(330, one(layer, 5)), (90, flags), (62, color), (370, weight),
                   (331, types['Continuous']), (440, 0)]
    require(state == wanted, 'Transferred saved-state packet')
    following, = [r for r in rows if r[0] == (0, 'LINE')]
    for code, value in {10:1., 20:2., 30:3., 11:4., 21:5., 31:6.}.items():
        require(one(following, code) == value, 'Following LINE geometry')
    # All selected IDs must remain identical for output -> resave. Transfer into a
    # different document deliberately has independent identities; do not compare
    # source/destination handles as though this were same-document hydration.
    return tuple(tuple(r) for r in (table, outer, inner, state, following, *layers.values()))


def checked(rows, year):
    try:
        return inspect_rows(rows, year)
    except (KeyError, IndexError) as error:
        raise ValueError('Unresolved transfer record') from error


def compare(a, b):
    require(a == b, 'Resave changed selected identities or packets')


def main(directory):
    stems = {f'layer-state-transfer-AutoCad{v}-{t}' for v, t in itertools.product(PROFILES, ('text', 'binary'))}
    names = {stem + '-' + stage + '.dxf' for stem, stage in itertools.product(stems, STAGES)} | {stem + '.las' for stem in stems}
    def inventory(actual):
        require(actual == names, 'Missing/extra transfer output inventory')
    inventory({p.name for p in directory.glob('layer-state-transfer-*')})
    controls = reject(lambda: inventory(names - {next(iter(names))}))
    controls += reject(lambda: inventory(names | {'extra.las'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        stem = f'layer-state-transfer-AutoCad{year}-{transport}'
        las = (directory / (stem + '.las')).read_bytes(); check_las(las)
        lines = las.decode('utf-8').replace('\r\n', '\n').splitlines()
        for i in range(len(lines)):
            for mode in ('remove', 'duplicate', 'change'):
                bad = list(lines)
                if mode == 'remove': bad.pop(i)
                elif mode == 'duplicate': bad.insert(i, bad[i])
                else: bad[i] += '_bad'
                controls += reject(lambda: check_las(('\n'.join(bad) + '\n').encode()))
        previous = None
        for stage in STAGES:
            path = directory / (stem + '-' + stage + '.dxf')
            require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == ((transport == 'binary') != (stage == 'output')), 'Transfer transport')
            tags = load(path, year); at = tags.index((9, '$ACADVER'))
            require(tags[at+1] == (1, PROFILES[year]), 'Transfer version')
            rows = records(tags); selected = checked(rows, year)
            if stage == 'output': previous = selected
            elif stage == 'resave':
                compare(previous, selected)
                target = one(selected[3], 5)
                renamed = tuple(tuple((c, 'ABCDEF' if v == target else v) for c, v in row) for row in selected)
                controls += reject(lambda: compare(previous, renamed))
            for packet in selected[1:4]:
                index = next(i for i, row in enumerate(rows) if tuple(row) == packet)
                for offset in range(1, len(packet)):
                    for mode in ('remove', 'duplicate', 'change'):
                        bad = list(rows); row = list(packet)
                        if mode == 'remove': row.pop(offset)
                        elif mode == 'duplicate': row.insert(offset, row[offset])
                        else:
                            code, value = row[offset]; row[offset] = (code, value + '_bad' if isinstance(value, str) else int(value) ^ 1)
                        bad[index] = row
                        controls += reject(lambda: checked(bad, year))
            doc = ezdxf.readfile(path); inner = doc.entitydb[one(selected[2], 5)]
            require(list(inner.keys()) == ['Transfer'] and inner['Transfer'].dxf.handle == one(selected[3], 5), 'Independent saved-state identity')
            require(doc.layers.get('Walls').dxf.color == 1 and doc.layers.get('Walls').dxf.flags == 4, 'Independent layer restoration')
            require(not any(any(v.values()) for v in audit_signature(doc)), 'Independent DXF errors/repairs')
    print(f'PASS: 12 exact LAS packets and 36 actual DXF drawings; {controls} corruption/inventory controls rejected; '
          'zero independent audit errors/repairs. No native AutoCAD acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_layer_state_transfer.py ARTIFACTS')
    main(Path(sys.argv[1]))
