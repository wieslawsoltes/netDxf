#!/usr/bin/env python3
"""Check real saved-state renames and clone headers without normalizing identities."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_populated_layer_state_identity import load
from verify_layer_state_identity import canonical, one, require, reject, records, PROFILES
from verify_raw_line_geometry import audit_signature

STAGES = ('source', 'output', 'resave')


def inspect_rows(rows, clone, source):
    indexed = {}
    for row in rows:
        if row[0] in ((0, 'SECTION'), (0, 'ENDSEC'), (0, 'EOF')):
            continue
        handles = [v for c, v in row if c in (5, 105)]
        if handles:
            require(len(handles) == 1, 'Repeated physical identity')
            handle = canonical(handles[0])
            require(handle not in indexed, 'Duplicate physical object')
            indexed[handle] = row
    table, = [r for r in rows if r[:2] == [(0, 'TABLE'), (2, 'LAYER')]]
    outer_id = canonical(one(table, 360)); outer = indexed.get(outer_id)
    require(outer is not None, 'Unresolved outer dictionary')
    inner_id = canonical(one(outer, 360)); inner = indexed.get(inner_id)
    require(inner is not None, 'Unresolved state dictionary')
    require(outer == [(0, 'DICTIONARY'), (5, outer_id), (330, one(table, 5)),
        (100, 'AcDbDictionary'), (280, 1), (281, 1), (3, 'ACAD_LAYERSTATES'), (360, inner_id)], 'Outer packet')
    require(inner[:6] == [(0, 'DICTIONARY'), (5, inner_id), (330, outer_id),
        (100, 'AcDbDictionary'), (280, 1), (281, 1)], 'State dictionary header')
    names = {'Before', 'Untouched'} if source else {'Before', 'Untouched', 'Copy'} if clone else {'After', 'Untouched'}
    require(len(inner) == 6 + len(names)*2, 'State dictionary size')
    entries = {}
    for at in range(6, len(inner), 2):
        require(inner[at][0] == 3 and inner[at+1][0] == 350, 'Entry framing/type')
        name = inner[at][1]; handle = canonical(inner[at+1][1])
        require(name not in entries and handle not in entries.values(), 'Duplicate state name/target')
        entries[name] = handle
    require(set(entries) == names, 'State names')
    layers = {one(r, 2): one(r, 5) for r in rows if r[0] == (0, 'LAYER')}
    require(set(layers) == {'0', 'Walls'}, 'Layer inventory')
    line = {one(r, 2): one(r, 5) for r in rows if r[0] == (0, 'LTYPE')}
    require(set(line) == {'ByBlock', 'ByLayer', 'Continuous'}, 'Linetype inventory')
    states = {}
    for name, handle in entries.items():
        paper = name != 'Untouched'
        wanted = [(0, 'XRECORD'), (5, handle), (102, '{ACAD_REACTORS'), (330, inner_id), (102, '}'),
            (330, inner_id), (100, 'AcDbXrecord'), (280, 1), (91, 2047),
            (301, 'Paper state' if paper else 'Model state'), (290, paper), (302, 'Walls' if paper else '0')]
        for layer, flags, color, weight in [('0', 8, 7, -3), ('Walls', 12, 3, 30)]:
            wanted += [(330, layers[layer]), (90, flags), (62, color), (370, weight), (331, line['Continuous']), (440, 0)]
        require(indexed.get(handle) == wanted, 'State packet/header/identity/settings')
        states[name] = tuple(wanted)
    require(len([r for r in rows if r[0] == (0, 'XRECORD')]) == len(names), 'XRECORD inventory')
    following, = [r for r in rows if r[0] == (0, 'LINE')]
    for code, value in {10:1., 20:2., 30:3., 11:4., 21:5., 31:6.}.items():
        require(one(following, code) == value, 'Following geometry')
    resources = tuple(sorted((h, tuple(r)) for h, r in indexed.items() if r[0] in ((0, 'LAYER'), (0, 'LTYPE'))))
    return tuple(table), tuple(outer), tuple(inner[:6]), resources, tuple(following), entries, states


def transition(before, after, clone):
    require(before[:5] == after[:5], 'Table/dictionaries/resources/LINE changed')
    old_entries, new_entries = before[5], after[5]
    require(old_entries['Untouched'] == new_entries['Untouched']
            and before[6]['Untouched'] == after[6]['Untouched'], 'Unrelated state changed')
    if clone:
        require(old_entries['Before'] == new_entries['Before'] and before[6]['Before'] == after[6]['Before'], 'Clone changed original')
        require(new_entries['Copy'] not in old_entries.values(), 'Clone reused state identity')
        source = list(before[6]['Before'])
        source[1] = (5, new_entries['Copy'])
        require(tuple(source) == after[6]['Copy'], 'Clone lost an unselected header/property')
    else:
        require(old_entries['Before'] == new_entries['After']
                and before[6]['Before'] == after[6]['After'], 'Rename changed state identity/payload')


def inspect(path, year, binary, clone, source):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load(path, year); at = tags.index((9, '$ACADVER'))
    require(tags[at+1] == (1, PROFILES[year]), 'Profile')
    rows = records(tags); selected = inspect_rows(rows, clone, source)
    doc = ezdxf.readfile(path); dictionary = doc.entitydb[one(selected[2], 5)]
    require(set(dictionary.keys()) == set(selected[5]), 'Independent dictionary names')
    for name, handle in selected[5].items():
        state = dictionary[name]
        require(state.dxf.handle == handle and state.dxf.owner == dictionary.dxf.handle, 'Independent state identity/owner')
        require([(t.code, t.value) for t in state.tags] == list(selected[6][name][8:]), 'Independent state payload')
    require(not any(any(v.values()) for v in audit_signature(doc)), 'Independent audit errors/repairs')
    count = 0
    packets = [selected[1], next(tuple(r) for r in rows if r[0] == (0, 'DICTIONARY') and one(r, 5) == one(selected[2], 5)), *selected[6].values()]
    for packet in packets:
        index = next(i for i, r in enumerate(rows) if tuple(r) == packet)
        for offset in range(1, len(packet)):
            for mode in ('remove', 'duplicate', 'change'):
                damaged = list(rows); row = list(packet)
                if mode == 'remove': row.pop(offset)
                elif mode == 'duplicate': row.insert(offset, row[offset])
                else:
                    code, value = row[offset]
                    row[offset] = (code, '0' if code in (5, 330, 350, 360) else value + '_bad' if isinstance(value, str) else int(value)^1)
                damaged[index] = row
                count += reject(lambda: inspect_rows(damaged, clone, source))
    return selected, count


def main(directory):
    names = {f'layer-state-lifecycle-AutoCad{year}-{transport}-{operation}-{stage}.dxf'
        for year, transport, operation, stage in itertools.product(PROFILES, ('text', 'binary'), ('rename', 'clone'), STAGES)}
    def inventory(actual): require(actual == names, 'Missing/extra lifecycle drawings')
    inventory({p.name for p in directory.glob('layer-state-lifecycle-*.dxf')})
    controls = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport, operation in itertools.product(PROFILES, ('text', 'binary'), ('rename', 'clone')):
        baseline = previous = None
        for stage in STAGES:
            path = directory / f'layer-state-lifecycle-AutoCad{year}-{transport}-{operation}-{stage}.dxf'
            current, count = inspect(path, year, (transport == 'binary') != (stage == 'output'), operation == 'clone', stage == 'source')
            controls += count
            if baseline is None: baseline = current
            else:
                transition(baseline, current, operation == 'clone')
                if previous is not None:
                    require(current == previous, 'Second save changed identities/settings')
                previous = current
    print(f'PASS: {len(names)} drawings / 168 saved-state packets; {controls} corruptions/inventories rejected; '
          'exact rename identities, clone paper-space state and zero independent audit errors/repairs. No native AutoCAD claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_layer_state_lifecycle.py ARTIFACTS')
    main(Path(sys.argv[1]))
