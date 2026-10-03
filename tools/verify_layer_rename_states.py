#!/usr/bin/env python3
"""Verify exported layer-name changes without normalizing handles or saved values."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_layer_state_identity import one, canonical, require, reject, records, PROFILES
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_raw_line_geometry import audit_signature


def inspect(path, year, binary, renamed):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_visibility_tags(path)
    at = tags.index((9, '$ACADVER')); require(tags[at+1] == (1, PROFILES[year]), 'Wrong profile')
    at = tags.index((9, '$CLAYER'))
    name = 'Renamed' if renamed else 'Walls'
    require(tags[at+1] == (8, name), 'CLAYER did not follow layer')
    rows = records(tags); indexed = {}
    for row in rows:
        if row[0] in ((0, 'SECTION'), (0, 'ENDSEC'), (0, 'EOF')): continue
        ids = [v for c, v in row if c in (5, 105)]
        if ids:
            require(len(ids) == 1, 'Repeated identity declaration')
            handle = canonical(ids[0]); require(handle not in indexed, 'Duplicate global identity')
            indexed[handle] = row
    table, = [r for r in rows if r[:2] == [(0, 'TABLE'), (2, 'LAYER')]]
    outer_id = one(table, 360); outer = indexed[outer_id]
    child_id = one(outer, 360); child = indexed[child_id]
    require(outer == [(0, 'DICTIONARY'), (5, outer_id), (330, one(table, 5)), (100, 'AcDbDictionary'),
                      (280, 1), (281, 1), (3, 'ACAD_LAYERSTATES'), (360, child_id)], 'Outer packet')
    require(child[:6] == [(0, 'DICTIONARY'), (5, child_id), (330, outer_id), (100, 'AcDbDictionary'), (280, 1), (281, 1)]
            and len(child) == 10 and child[6][0] == child[8][0] == 3
            and child[7][0] == child[9][0] == 350, 'State dictionary shape')
    entries = {child[i][1]: child[i+1][1] for i in (6, 8)}
    require(set(entries) == {'Saved', 'Other'} and len(set(entries.values())) == 2, 'State inventory')
    layers = {one(r, 2): r for r in rows if r[0] == (0, 'LAYER')}
    types = {one(r, 2): r for r in rows if r[0] == (0, 'LTYPE')}
    require(set(layers) == {'0', name} and set(types) == {'ByLayer', 'ByBlock', 'Continuous'}, 'Resource inventory')
    require(one(layers[name], 62) == 3 and one(layers[name], 70) == 4 and one(layers[name], 370) == 30, 'Live layer values changed')
    for label, handle in entries.items():
        wanted = [(0, 'XRECORD'), (5, handle), (102, '{ACAD_REACTORS'), (330, child_id), (102, '}'),
                  (330, child_id), (100, 'AcDbXrecord'), (280, 1), (91, 2047),
                  (301, 'Keep captured values' if label == 'Saved' else 'Keep other current layer'),
                  (290, label == 'Saved'), (302, name if label == 'Saved' else '0')]
        for layer, flags, color, weight in [('0', 8, 7, -3), (name, 12, 3, 30)]:
            wanted += [(330, one(layers[layer], 5)), (90, flags), (62, color), (370, weight),
                       (331, one(types['Continuous'], 5)), (440, 0)]
        require(indexed[handle] == wanted, 'Saved state identity, ownership or captured packet changed')
    line, = [r for r in rows if r[0] == (0, 'LINE')]
    require(one(line, 8) == name, 'Entity layer name did not follow')
    for code, value in ((10, 1.), (20, 2.), (30, 3.), (11, 4.), (21, 5.), (31, 6.)):
        require(one(line, code) == value, 'Following geometry changed')
    require(len([r for r in rows if r[0] == (0, 'XRECORD')]) == 2, 'Extra state records')
    selected = {one(r, 5): tuple(r) for r in [table, outer, child, *layers.values(), *types.values(),
                                           *(indexed[h] for h in entries.values()), line]}
    selected['CLAYER'] = (tags[at+1],)
    doc = ezdxf.readfile(path)
    # ezdxf supplies its own Defpoints layer on load; physical source inventory was checked above.
    require(set(doc.layers.entries) - {'defpoints'} == {'0', name.lower()}, 'Independent layer inventory')
    require(doc.header['$CLAYER'] == name and doc.entitydb[one(line, 5)].dxf.layer == name, 'Independent live name resolution')
    for label, handle in entries.items():
        state = doc.entitydb[child_id][label]
        require(state.dxf.handle == handle and state.dxf.owner == child_id, 'Independent state identity/owner')
        require([(t.code, t.value) for t in state.tags] == indexed[handle][8:], 'Independent saved payload')
    require(not any(any(v.values()) for v in audit_signature(doc)), 'Independent audit errors/repairs')
    return selected


def expected_rename(source):
    result = {}
    for handle, row in source.items():
        kind = row[0][1]
        result[handle] = tuple((code, 'Renamed' if value == 'Walls' and
                               (handle == 'CLAYER' or kind == 'LAYER' and code == 2
                                or kind == 'LINE' and code == 8 or kind == 'XRECORD' and code == 302)
                               else value) for code, value in row)
    return result


def compare(expected, actual):
    require(expected == actual, 'Unexpected identity, packet, resource or captured-value change')


def main(directory):
    names = {f'layer-rename-state-AutoCad{v}-{t}-{s}.dxf' for v, t, s in
             itertools.product(PROFILES, ('text', 'binary'), ('source', 'output', 'resave'))}
    def inventory(actual): require(actual == names, 'Drawing inventory mismatch')
    inventory({p.name for p in directory.glob('layer-rename-state-*.dxf')})
    controls = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        wanted = None
        for stage in ('source', 'output', 'resave'):
            p = directory / f'layer-rename-state-AutoCad{year}-{transport}-{stage}.dxf'
            observed = inspect(p, year, (transport == 'binary') != (stage == 'output'), stage != 'source')
            if wanted is None: wanted = expected_rename(observed)
            else:
                compare(wanted, observed)
                for handle, row in observed.items():
                    for i, (code, value) in enumerate(row):
                        for mode in ('change', 'remove', 'duplicate'):
                            bad = dict(observed); packet = list(row)
                            if mode == 'remove': packet.pop(i)
                            elif mode == 'duplicate': packet.insert(i, packet[i])
                            else: packet[i] = (code, value + '_changed' if isinstance(value, str) else value + 1)
                            bad[handle] = tuple(packet)
                            controls += reject(lambda: compare(wanted, bad))
                missing = dict(observed); missing.pop(next(iter(missing)))
                controls += reject(lambda: compare(wanted, missing))
    print(f'PASS: {len(names)} drawings / 72 saved-state packets; {controls} corrupted packets/inventories rejected; '
          'only declared layer-name fields changed, exact handles/captured settings, zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_layer_rename_states.py ARTIFACTS')
    main(Path(sys.argv[1]))
