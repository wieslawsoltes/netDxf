#!/usr/bin/env python3
"""Independently check actual modern MESH header edits and unchanged physical data."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_raw_line_geometry import audit_signature

PROFILES = {2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}
OPERATIONS = ('keep', 'level', 'blend', 'same-level', 'same-blend')
NAMES = {f'MH_{place}_{cache}_{operation}' for place, cache, operation in itertools.product(range(4), range(3), OPERATIONS)}
POINTS = ((1., 2., 3.), (4., 2., 3.), (1., 6., 3.))
PAYLOAD = bytes((77, 72, 67, 0, 255))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def reject(action):
    try:
        action()
    except ValueError:
        return 1
    raise ValueError('Corrupted evidence was accepted')


def one(row, code):
    values = [value for key, value in row if key == code]
    require(len(values) == 1, f'Missing/duplicate group {code}')
    return values[0]


def part(row, marker):
    require(row.count((100, marker)) == 1, 'Missing/duplicate subclass ' + marker)
    start = row.index((100, marker)) + 1
    end = next((i for i in range(start, len(row)) if row[i][0] in (100, 1001)), len(row))
    return row[start:end]


def state(name, source):
    require(name in NAMES, 'Unexpected subject')
    _, place, cache, operation = name.split('_')
    changed = not source and operation in ('level', 'blend')
    return (5 if not source and operation == 'level' else 2,
            int(not source and operation == 'blend'), 0 if changed else int(cache), int(place))


def check_row(row, name, year, source):
    require(row[0] == (0, 'MESH'), 'Wrong entity type')
    level, blend, cache, _ = state(name, source)
    common, mesh = part(row, 'AcDbEntity'), part(row, 'AcDbSubDMesh')
    require(one(mesh, 72) == blend and one(mesh, 91) == level, 'Incorrect MESH header')
    require(one(row, 5) != '0' and one(row, 330) != '0', 'Missing/zero identity or owner')
    require([tag for tag in row if tag[0] >= 1000] == [(1001, 'MESH_HEADER'), (1000, name)], 'MESH XData changed')
    size_code = 92 if year < 2013 else 160
    expected_proxy = [] if cache == 0 else [(size_code, 0)] if cache == 1 else [(size_code, 5), (310, PAYLOAD)]
    require([tag for tag in common if tag[0] in (92, 160, 310)] == expected_proxy, 'Stale/missing/incorrect ordered proxy graphics')
    require(one(mesh, 92) == 3 and one(mesh, 93) == 4 and one(mesh, 94) == 1 and one(mesh, 95) == 1, 'Topology count changed')
    actual = [(code, struct.pack('>d', value)) for code, value in mesh if code in (10, 20, 30)]
    expected = [(code, struct.pack('>d', value)) for point in POINTS for code, value in zip((10, 20, 30), point)]
    require(actual == expected, 'Coordinate bits/order changed')
    face = next(i for i, tag in enumerate(mesh) if tag[0] == 93)
    edge = next(i for i, tag in enumerate(mesh) if tag[0] == 94)
    crease = next(i for i, tag in enumerate(mesh) if tag[0] == 95)
    require(mesh[face + 1:edge] == [(90, 3), (90, 0), (90, 1), (90, 2)], 'Face list changed')
    require(mesh[edge + 1:crease] == [(90, 0), (90, 1)] and one(mesh, 140) == 0.5, 'Edge/crease changed')


def unselected(row):
    result, section = [], ''
    for code, value in row:
        if code == 100:
            section = value
        elif code == 1001:
            section = 'XData'
        if section == 'AcDbEntity' and code in (92, 160, 310):
            continue
        if section == 'AcDbSubDMesh' and code in (72, 91):
            continue
        result.append((code, value))
    return tuple(result)


def controls(row, name, year, source):
    count = 0
    for code in (5, 330, 72, 91, 10):
        damaged = list(row); index = next(i for i, tag in enumerate(row) if tag[0] == code)
        value = row[index][1]
        if code in (5, 330):
            value = '0'
        elif code == 10:
            bits = struct.unpack('>Q', struct.pack('>d', value))[0] ^ 1
            value = struct.unpack('>d', struct.pack('>Q', bits))[0]
        else:
            value ^= 1
        damaged[index] = (code, value)
        count += reject(lambda: check_row(damaged, name, year, source))
    damaged, section = [], ''
    has_proxy = any(code in (92, 160, 310) for code, _ in part(row, 'AcDbEntity'))
    for tag in row:
        if tag[0] == 100:
            section = tag[1]
        if section == 'AcDbEntity' and tag[0] in (92, 160, 310):
            continue
        damaged.append(tag)
        if tag == (100, 'AcDbEntity') and not has_proxy:
            damaged.append((92 if year < 2013 else 160, 0))
    count += reject(lambda: check_row(damaged, name, year, source))
    return count


def inspect(path, year, binary, source):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_visibility_tags(path)
    at = tags.index((9, '$ACADVER'))
    require(tags[at + 1] == (1, PROFILES[year]), 'Wrong version')
    starts = [i for i, tag in enumerate(tags) if tag[0] == 0]
    rows = [tags[a:b] for a, b in zip(starts, starts[1:] + [len(tags)])]
    selected = [row for row in rows if (1001, 'MESH_HEADER') in row]
    require(len(selected) == len(NAMES), 'Wrong subject count')
    document = ezdxf.readfile(path)
    result, handles, count = {}, set(), 0
    for row in selected:
        name = one(row, 1000); require(name not in result, 'Duplicate subject')
        check_row(row, name, year, source); count += controls(row, name, year, source)
        handle = one(row, 5); require(handle not in handles, 'Duplicate MESH handle'); handles.add(handle)
        result[name] = unselected(row)
        level, blend, _, place = state(name, source)
        mesh = document.entitydb[handle]
        require(mesh.dxf.subdivision_levels == level and mesh.dxf.blend_crease == blend, 'Independent header mismatch')
        require(tuple(tuple(point) for point in mesh.vertices) == POINTS, 'Independent geometry mismatch')
        require(tuple(tuple(face) for face in mesh.faces) == ((0, 1, 2),)
                and tuple(tuple(edge) for edge in mesh.edges) == ((0, 1),)
                and tuple(mesh.creases) == (0.5,), 'Independent topology/crease mismatch')
        layout = document.modelspace() if place == 0 else document.layouts.get('MH_PAPER') if place == 1 else document.blocks[f'MH_CONTAINER_{place}']
        require(mesh.dxf.owner == one(row, 330) == layout.block_record_handle, 'Independent placement/owner mismatch')
    require(set(result) == NAMES, 'Missing subject')
    lines = list(document.modelspace().query('LINE'))
    require(len(lines) == 1 and tuple(lines[0].dxf.start) == (101., 102., 103.)
            and tuple(lines[0].dxf.end) == (104., 105., 106.), 'Following LINE changed')
    require(not any(any(values.values()) for values in audit_signature(document)), 'Independent audit errors/repairs')
    return result, count


def main(directory):
    names = {f'mesh-header-AutoCad{year}-{transport}-{stage}.dxf' for year, transport, stage in
             itertools.product(PROFILES, ('text', 'binary'), ('source', 'output', 'resave'))}
    def inventory(actual):
        require(actual == names, 'Missing/extra MESH header fixtures')
    inventory({path.name for path in directory.glob('mesh-header-*.dxf')})
    count = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        previous = None
        for stage in ('source', 'output', 'resave'):
            current, rejected = inspect(directory / f'mesh-header-AutoCad{year}-{transport}-{stage}.dxf', year,
                                         (transport == 'binary') != (stage == 'resave'), stage == 'source')
            count += rejected
            if previous is not None:
                require(previous == current, 'Unselected physical fields or identities changed across save')
                damaged = dict(current); name = next(iter(damaged)); row = list(damaged[name])
                index = next(i for i, tag in enumerate(row) if tag[0] == 5); row[index] = (5, 'FFFFFFFF')
                damaged[name] = tuple(row)
                count += reject(lambda: require(previous == damaged, 'Changed valid-looking handle'))
            previous = current
    print(f'PASS: {len(names)} actual drawings / {len(names) * len(NAMES)} MESH records; '
          f'{count} corruption/inventory controls rejected; no native AutoCAD acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_mesh_header_mutations.py ARTIFACTS')
    main(Path(sys.argv[1]))
