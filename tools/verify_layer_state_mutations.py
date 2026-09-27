#!/usr/bin/env python3
"""Check selective saved-layer updates and exact identity/metadata preservation."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_populated_layer_state_identity import load
from verify_layer_state_identity import canonical, one, require, reject, records, PROFILES
from verify_raw_line_geometry import audit_signature

MASKS = (0, 1, 2, 4, 8, 15, 64, 128, 256, 512, 2047, 1072)
STAGES = ('source', 'output', 'resave')


def expected_state(handle, owner, mask, changed, layers, linetypes):
    flags = 63
    if changed:
        # Independent truth table for each of the four supported Boolean properties.
        for bit, enabled in ((1, False), (2, True), (4, False), (8, False)):
            if mask & bit:
                flags = (flags | bit) if enabled else (flags & ~bit)
    color = 3 if changed and mask & 64 else 1
    line = 'NEW_DASH' if changed and mask & 128 else 'OLD_DASH'
    weight = 50 if changed and mask & 256 else 20
    alpha = 33554623 if changed and mask & 512 else 33554495  # 0x020000BF / 0x0200003F
    return [(0, 'XRECORD'), (5, handle), (102, '{ACAD_REACTORS'), (330, owner), (102, '}'),
            (330, owner), (100, 'AcDbXrecord'), (280, 1), (91, 2047), (301, 'Selective capture'),
            (290, False), (302, '0'),
            (330, layers['0']), (90, 8), (62, 7), (370, -3), (331, linetypes['Continuous']), (440, 0),
            (330, layers['Walls']), (90, flags), (62, color), (370, weight), (331, linetypes[line]), (440, alpha)]


def exact_state(actual, wanted):
    require(actual == wanted, 'Selected values, unselected flags/metadata or state identity changed')


def inspect(path, year, binary, changed):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load(path, year); at = tags.index((9, '$ACADVER'))
    require(tags[at+1] == (1, PROFILES[year]), 'Wrong profile')
    rows = records(tags); indexed = {}
    for row in rows:
        if row[0] in ((0, 'SECTION'), (0, 'ENDSEC'), (0, 'EOF')):
            continue
        handles = [v for c, v in row if c in (5, 105)]
        if handles:
            require(len(handles) == 1, 'Duplicate physical handle declaration')
            handle = canonical(handles[0]); require(handle not in indexed, 'Duplicate record identity')
            indexed[handle] = row
    layer_rows = [r for r in rows if r[0] == (0, 'LAYER')]
    layers = {one(r, 2): one(r, 5) for r in layer_rows}
    require(len(layer_rows) == len(layers) == 2 and set(layers) == {'0', 'Walls'}, 'Layer inventory')
    linetypes = {one(r, 2): one(r, 5) for r in rows if r[0] == (0, 'LTYPE')}
    require(set(linetypes) == {'ByBlock', 'ByLayer', 'Continuous', 'OLD_DASH', 'NEW_DASH'}, 'Linetype inventory')
    table, = [r for r in rows if r[:2] == [(0, 'TABLE'), (2, 'LAYER')]]
    outer_id = one(table, 360); outer = indexed[outer_id]; inner_id = one(outer, 360); inner = indexed[inner_id]
    require(outer == [(0, 'DICTIONARY'), (5, outer_id), (330, one(table, 5)), (100, 'AcDbDictionary'),
                     (280, 1), (281, 1), (3, 'ACAD_LAYERSTATES'), (360, inner_id)], 'LAYER extension structure')
    require(inner[:6] == [(0, 'DICTIONARY'), (5, inner_id), (330, outer_id), (100, 'AcDbDictionary'), (280, 1), (281, 1)], 'State dictionary header')
    require(len(inner) == 6 + 2*len(MASKS), 'State dictionary count')
    count = 0; identities = {}
    doc = ezdxf.readfile(path)
    for offset, mask in enumerate(MASKS):
        name, pointer = inner[6+2*offset:8+2*offset]
        require(name == (3, 'MASK_'+str(mask)) and pointer[0] == 350, 'State names/order/kinds')
        handle = canonical(pointer[1]); require(handle not in identities.values(), 'Aliased saved states')
        identities[mask] = handle
        row = indexed[handle]; wanted = expected_state(handle, inner_id, mask, changed, layers, linetypes)
        exact_state(row, wanted)
        native = doc.entitydb[handle]
        require(native.dxftype() == 'XRECORD' and native.dxf.owner == inner_id, 'Independent identity/owner')
        require([(t.code, t.value) for t in native.tags] == wanted[8:], 'Independent saved-property interpretation')
        for i in range(1, len(row)):
            for mode in ('remove', 'duplicate', 'change'):
                bad = list(row)
                if mode == 'remove': bad.pop(i)
                elif mode == 'duplicate': bad.insert(i, bad[i])
                else:
                    code, value = bad[i]
                    bad[i] = (code, value + '_bad' if isinstance(value, str) else int(value) ^ 1)
                count += reject(lambda: exact_state(bad, wanted))
    require(len([r for r in rows if r[0] == (0, 'XRECORD')]) == len(MASKS), 'Unexpected state XRECORD inventory')
    line, = [r for r in rows if r[0] == (0, 'LINE')]
    for c, value in {10:1.,20:2.,30:3.,11:4.,21:5.,31:6.}.items():
        require(one(line, c) == value, 'Following LINE changed')
    require(not any(any(v.values()) for v in audit_signature(doc)), 'Independent audit errors/repairs')
    # The update must not rename or replace resources, parent dictionaries or geometry.
    retained = (table, outer, inner, layer_rows, sorted(linetypes.items()), line, identities)
    return retained, count


def main(directory):
    names = {f'layer-state-mutation-AutoCad{year}-{transport}-{stage}.dxf'
             for year, transport, stage in itertools.product(PROFILES, ('text', 'binary'), STAGES)}
    def inventory(actual): require(actual == names, 'Missing/extra selective-update drawings')
    inventory({p.name for p in directory.glob('layer-state-mutation-*.dxf')})
    count = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        source = None
        for stage in STAGES:
            current, rejected = inspect(directory/f'layer-state-mutation-AutoCad{year}-{transport}-{stage}.dxf',
                                        year, (transport == 'binary') != (stage == 'output'), stage != 'source')
            count += rejected
            if source is None: source = current
            else:
                require(source == current, 'Unselected resources/identities changed across updates/saves')
                damaged = list(current); damaged[-1] = dict(current[-1]); damaged[-1][0] = 'ABCDEF'
                count += reject(lambda: require(source == tuple(damaged), 'Changed coherent state identity'))
    print(f'PASS: {len(names)} actual drawings / {len(names)*len(MASKS)} saved states, selective values and exact '
          f'unselected identities; {count} corruption/inventory controls rejected; zero audit errors/repairs. '
          'No native AutoCAD execution or general resource-rename claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_layer_state_mutations.py ARTIFACTS')
    main(Path(sys.argv[1]))
