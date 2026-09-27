#!/usr/bin/env python3
"""Check the real saved-state pointer carriers and exact identities across a rename."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_populated_layer_state_identity import load
from verify_layer_state_identity import canonical, one, require, reject, records, PROFILES
from verify_raw_line_geometry import audit_signature


def inspect_rows(rows, renamed):
    table, = [r for r in rows if r[:2] == [(0, 'TABLE'), (2, 'LAYER')]]
    dictionaries = {one(r, 5): r for r in rows if r[0] == (0, 'DICTIONARY')}
    parent = dictionaries[one(table, 360)]
    child = dictionaries[one(parent, 360)]
    name = 'Renamed' if renamed else 'State_0'
    require(child[6:] == [(3, name), (350, one(child, 350))], 'State name/entry inventory')
    target = canonical(one(child, 350))
    state, = [r for r in rows if r[0] == (0, 'XRECORD') and one(r, 5) == target]
    require(state[:8] == [(0, 'XRECORD'), (5, target), (102, '{ACAD_REACTORS'),
                         (330, one(child, 5)), (102, '}'), (330, one(child, 5)),
                         (100, 'AcDbXrecord'), (280, 1)], 'State header/owner/reactor')
    pointers = [r for r in rows if r[0] == (0, 'XRECORD') and one(r, 5) != target]
    require(len(pointers) == 1, 'Pointer carrier count')
    pointer = pointers[0]; at = pointer.index((100, 'AcDbXrecord'))
    require(pointer[at+1:] == [(280, 1), (330, target), (340, target), (350, target),
                               (360, target), (320, target), (1, target)], 'Pointer/arbitrary/text packet differs')
    line, = [r for r in rows if r[0] == (0, 'LINE')]
    at = line.index((102, '{ACAD_REACTORS'))
    require(line[at:at+3] == [(102, '{ACAD_REACTORS'), (330, target), (102, '}')], 'Persistent reactor packet differs')
    require([t for t in line if t[0] >= 1000] == [(1001, 'LSQ_REF'), (1005, target)], 'XData pointer differs')
    header, = [r for r in rows if r[:2] == [(0, 'SECTION'), (2, 'HEADER')]]
    at = header.index((9, '$LSQ_POINTER'))
    end = next((i for i in range(at+1, len(header)) if header[i][0] == 9), len(header))
    require(header.count((9, '$LSQ_POINTER')) == 1 and header[at+1:end] == [(340, target)], 'Custom HEADER pointer differs')
    require(one(parent, 330) == one(table, 5) and one(child, 330) == one(parent, 5), 'Dictionary ownership differs')
    # Normalize only the deliberate name edit, never any identity or reference.
    expected_name = [(code, 'State_0' if code == 3 else value) for code, value in child]
    return tuple(table), tuple(parent), tuple(expected_name), tuple(state), tuple(pointer), tuple(line), header[at+1]


def compare(before, after):
    require(before == after, 'Stored state/carrier identity or unselected data changed')


def main(directory):
    names = {f'layer-state-queries-AutoCad{v}-{t}-{s}.dxf' for v, t, s in
             itertools.product(PROFILES, ('text', 'binary'), ('source', 'output', 'resave'))}
    def inventory(actual): require(actual == names, 'Query drawing inventory')
    inventory({p.name for p in directory.glob('layer-state-queries-*.dxf')})
    checks = reject(lambda: inventory(names - {next(iter(names))}))
    checks += reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        before = None
        for stage in ('source', 'output', 'resave'):
            path = directory / f'layer-state-queries-AutoCad{year}-{transport}-{stage}.dxf'
            require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == ((transport == 'binary') != (stage == 'output')), 'Query transport')
            tags = load(path, year); at = tags.index((9, '$ACADVER')); require(tags[at+1] == (1, PROFILES[year]), 'Query profile')
            rows = records(tags); result = inspect_rows(rows, stage != 'source')
            if before is None: before = result
            else: compare(before, result)
            # Corrupt every exposed target-bearing slot; identities are never repaired.
            target = one(result[3], 5)
            for ri, row in enumerate(rows):
                for ti, (code, value) in enumerate(row):
                    if code not in (330, 340, 350, 360, 1005) or value != target: continue
                    for mode in ('remove', 'duplicate', 'change'):
                        bad = list(rows); changed = list(row)
                        if mode == 'remove': changed.pop(ti)
                        elif mode == 'duplicate': changed.insert(ti, changed[ti])
                        else: changed[ti] = (code, '0')
                        bad[ri] = changed
                        def verify():
                            try: inspect_rows(bad, stage != 'source')
                            except (KeyError, IndexError) as error: raise ValueError('Missing reference record') from error
                        checks += reject(verify)
            doc = ezdxf.readfile(path); layer_dict = doc.entitydb[one(result[2], 5)]
            require(layer_dict['State_0' if stage == 'source' else 'Renamed'].dxf.handle == target, 'Independent target resolution')
            require(doc.header['$LSQ_POINTER'] == target, 'Independent HEADER pointer')
            require(not any(any(values.values()) for values in audit_signature(doc)), 'Independent repairs/errors')
    print(f'PASS: {len(names)} actual reference drawings; {checks} corruption/inventory controls rejected; '
          'exact saved-state identities and pointer carriers, zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_layer_state_references.py ARTIFACTS')
    main(Path(sys.argv[1]))
