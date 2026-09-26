#!/usr/bin/env python3
"""Verify real LAYER dictionary/state identity and metadata without handle normalization."""
import copy
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_editable_table_styles import records
from verify_raw_line_geometry import audit_signature

PROFILES = {f'AutoCad{year}': code for year, code in
            ((2000, 'AC1015'), (2004, 'AC1018'), (2007, 'AC1021'),
             (2010, 'AC1024'), (2013, 'AC1027'), (2018, 'AC1032'))}


def require(value, message):
    if not value:
        raise ValueError(message)


def reject(action):
    try:
        action()
    except (ValueError, KeyError, IndexError):
        return 1
    raise ValueError('Corrupted identity evidence was accepted')


def one(tags, code):
    values = [v for c, v in tags if c == code]
    require(len(values) == 1, f'Missing/duplicate field {code}')
    return values[0]


def common(row):
    """Separate structural fields from grouped extension/reactor handles."""
    plain, extension, reactors = [], None, []
    group, depth = None, 0
    for code, value in row[1:]:
        if code == 100 and depth == 0:
            break
        if code == 102:
            if value.startswith('{'):
                if depth == 0:
                    group = value
                depth += 1
            elif value == '}':
                depth -= 1
                require(depth >= 0, 'Unbalanced control group')
            continue
        if depth:
            if depth == 1 and group == '{ACAD_XDICTIONARY' and code == 360:
                require(extension is None, 'Duplicate extension')
                extension = value
            if depth == 1 and group == '{ACAD_REACTORS' and code == 330:
                reactors.append(value)
        else:
            plain.append([code, value])
    require(depth == 0, 'Unclosed control group')
    return plain, extension, reactors


def dictionary_entries(row):
    require(row[0] == [0, 'DICTIONARY'] and row.count([100, 'AcDbDictionary']) == 1,
            'Managed source must be a plain dictionary')
    at = row.index([100, 'AcDbDictionary']) + 1
    end = next((i for i in range(at, len(row)) if row[i][0] == 1001), len(row))
    pairs, pending = [], None
    for code, value in row[at:end]:
        if code == 3:
            require(pending is None, 'Orphan dictionary name')
            pending = value
        elif code in (350, 360):
            require(pending is not None, 'Orphan dictionary pointer')
            pairs.append((pending, value, code))
            pending = None
    require(pending is None, 'Dictionary name missing a pointer')
    require(len({name for name, _, _ in pairs}) == len(pairs), 'Duplicate dictionary names')
    return pairs


def projection(items, populated, metadata):
    layer = [(h, row) for h, row in items.items() if row[:2] == [[0, 'TABLE'], [2, 'LAYER']]]
    require(len(layer) == 1, 'LAYER table inventory')
    table_id, table = layer[0]
    _, wrapper_id, _ = common(table)
    wrapper = items[wrapper_id]
    require(one(common(wrapper)[0], 330) == table_id, 'Wrong wrapper structural owner')
    slots = dictionary_entries(wrapper)
    require(len(slots) == 1 and slots[0][0] == 'ACAD_LAYERSTATES', 'Managed extension slot')
    _, states_id, pointer_kind = slots[0]
    require(pointer_kind == (350 if metadata else 360), 'Wrapper pointer strength changed')
    states = items[states_id]
    require(one(common(states)[0], 330) == wrapper_id, 'Wrong states-dictionary owner')
    pairs = dictionary_entries(states)
    require([name for name, _, _ in pairs] == (['FIRST', 'SECOND'] if populated else []), 'State names/order/inventory')
    require(one(wrapper, 280) == (0 if metadata else 1) and one(states, 280) == (0 if metadata else 1), 'Dictionary ownership flags')
    require(one(wrapper, 281) == (4 if metadata else 1) and one(states, 281) == (5 if metadata else 1), 'Dictionary cloning flags')
    selected = {table_id: table, wrapper_id: wrapper, states_id: states}
    require(len(selected) == 3, 'Managed dictionary identities collide')
    for name, state_id, kind in pairs:
        require(kind == (360 if metadata else 350), 'State pointer strength changed')
        row = items[state_id]
        require(state_id not in selected and row[0] == [0, 'XRECORD'], 'Invalid state identity/type')
        plain, _, reactors = common(row)
        require(one(plain, 330) == states_id and states_id in reactors, 'State owner/reactor target')
        require(row.count([100, 'AcDbXrecord']) == 1 and one(row, 280) == (3 if metadata else 1), 'State cloning/header')
        require(one(row, 91) == 2047 and one(row, 301) == name.lower() + ' snapshot'
                and one(row, 302) == '0' and one(row, 290) == 0, 'State snapshot header')
        require([v for c, v in row if c == 62] == [7, 3], 'Stored snapshot layer colors')
        require([tag for tag in row if tag[0] >= 1000] == [[1001, 'IDENTITY_DATA'], [1000, name]], 'State XData changed')
        selected[state_id] = row
    if metadata:
        for handle, row in list(selected.items()):
            if handle == table_id:
                continue
            _, extension_id, reactors = common(row)
            require(extension_id is not None and reactors, 'Managed metadata missing')
            extension = items[extension_id]
            require(one(common(extension)[0], 330) == handle, 'Extension ownership')
            ((name, value_id, _),) = dictionary_entries(extension)
            require(name == 'NOTE' and items[value_id][0] == [0, 'DICTIONARYVAR'], 'Extension child inventory')
            require(one(items[value_id], 1) == 'metadata-' + handle, 'Extension child payload')
            require(one(common(items[value_id])[0], 330) == extension_id, 'Extension child owner')
            if handle in (wrapper_id, states_id):
                require([tag for tag in row if tag[0] >= 1000] ==
                        [[1001, 'IDENTITY_DATA'], [1000, 'metadata-' + handle]], 'Dictionary XData doubled or lost')
            selected[extension_id] = extension
            selected[value_id] = items[value_id]
    return selected


def unchanged(expected, actual):
    require(set(actual) == set(expected), 'Missing/reallocated/extra selected identities')
    for handle in expected:
        require(actual[handle] == expected[handle], 'Changed ordered packet at ' + handle)


def controls(expected, actual):
    count = 0
    for handle, row in actual.items():
        for index, (code, value) in enumerate(row):
            changed = dict(actual)
            changed[handle] = list(row)
            changed[handle][index] = [code, value + '_altered' if isinstance(value, str)
                                      else [*value, 1] if isinstance(value, list) else value + 1]
            count += reject(lambda: unchanged(expected, changed))
    missing = dict(actual)
    missing.pop(next(iter(missing)))
    count += reject(lambda: unchanged(expected, missing))
    extra = dict(actual)
    extra['FFFFF'] = copy.deepcopy(next(iter(actual.values())))
    count += reject(lambda: unchanged(expected, extra))
    return count


def inspect(path, profile, binary, populated, metadata):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport mismatch')
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == profile, 'Version mismatch')
    selected = projection(records(path), populated, metadata)
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (1., 2., 3.) and tuple(line.dxf.end) == (4., 5., 6.), 'Following geometry changed')
    require(doc.layers.get('IDENTITY_LAYER').color == 3, 'Current layer color changed')
    errors, fixes = audit_signature(doc)
    # Keep the auditor's observation explicit. Its orphan-dictionary pass does
    # not recognize a DICTIONARY owner's own extension as an owned child: the
    # two nested NOTE dictionaries are removed even from these untouched inputs.
    # Exact physical ownership/extension/payload records above are NOT normalized.
    # Ordinary fixtures must remain completely clean; complex metadata carriers
    # must retain precisely the two pre-existing input findings, with no errors.
    require(not errors, 'Independent audit errors')
    require(dict(fixes) == ({202: 2} if metadata else {}), 'Independent audit findings differ from the fixed source fixture')
    return selected


def main(directory):
    groups = []
    for version, binary, populated in itertools.product(PROFILES, (False, True), (False, True)):
        groups.append((f'layer-identity-{version}-{binary}-{populated}', PROFILES[version], binary, populated, False, 4))
    for binary in (False, True):
        groups.append((f'layer-identity-metadata-{binary}', 'AC1032', binary, True, True, 3))
    wanted = {f'{prefix}-{stage}.dxf' for prefix, _, _, _, _, count in groups for stage in ['source', *range(count)]}
    def inventory(actual):
        require(actual == wanted, 'Missing/extra layer-state identity drawing')
    inventory({p.name for p in directory.glob('layer-identity-*.dxf')})
    rejected = reject(lambda: inventory(wanted - {next(iter(wanted))})) + reject(lambda: inventory(wanted | {'extra.dxf'}))
    packets = 0
    for prefix, profile, binary, populated, metadata, count in groups:
        expected = inspect(directory / f'{prefix}-source.dxf', profile, binary, populated, metadata)
        packets += len(expected)
        for stage in range(count):
            actual = inspect(directory / f'{prefix}-{stage}.dxf', profile,
                             binary if stage % 2 == 0 else not binary, populated, metadata)
            unchanged(expected, actual)
            rejected += controls(expected, actual)
            packets += len(actual)
    print(f'PASS: {len(wanted)} real drawings / {packets} selected physical records; '
          f'{rejected} corrupted packet/inventory controls rejected; no identity normalization, '
          '120 ordinary drawings have no audit errors/repairs; 8 nested-dictionary metadata drawings retain '
          'exactly the 2 input audit repairs each, with no errors. Native AutoCAD was not executed.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_layer_state_identity.py ARTIFACTS')
    main(Path(sys.argv[1]))
