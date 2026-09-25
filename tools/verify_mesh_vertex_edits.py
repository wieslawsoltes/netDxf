#!/usr/bin/env python3
"""Independently verify actual count-preserving MESH coordinate-edit exports."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_raw_line_geometry import audit_signature
from verify_mesh_header_mutations import one, part, require, reject

PROFILES = {2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}
OPERATIONS = ('keep', 'single', 'replace', 'batch', 'same', 'alias')
NAMES = {f'MV_{place}_{cache}_{operation}' for place, cache, operation in itertools.product(range(4), range(3), OPERATIONS)}
POINTS = ((1., 2., 3.), (4., 2., 3.), (1., 6., 3.))
PAYLOAD = bytes((77, 86, 69, 0, 255))


def expected(name, source):
    require(name in NAMES, 'Unexpected MESH edit subject')
    _, place, cache, operation = name.split('_')
    points = list(POINTS)
    if not source:
        if operation == 'single':
            points[1] = (14., -2., 9.)
        elif operation == 'replace':
            points = [(x + 10., y - 4., z + 8.) for x, y, z in POINTS]
        elif operation == 'batch':
            points[0], points[2] = (-4., -5., -6.), (30., 31., 32.)
    changed = not source and operation in ('single', 'replace', 'batch')
    return tuple(points), 0 if changed else int(cache), int(place)


def check_row(row, name, year, source):
    require(row[0] == (0, 'MESH'), 'Wrong entity type')
    points, cache, _ = expected(name, source)
    common, mesh = part(row, 'AcDbEntity'), part(row, 'AcDbSubDMesh')
    require(one(mesh, 72) == 1 and one(mesh, 91) == 2, 'Unselected subdivision headers changed')
    require(one(row, 5) != '0' and one(row, 330) != '0', 'Missing/zero handle or owner')
    require([tag for tag in row if tag[0] >= 1000] == [(1001, 'MESH_VERTEX_EDIT'), (1000, name)], 'MESH XData changed')
    size = 92 if year < 2013 else 160
    proxy = [] if cache == 0 else [(size, 0)] if cache == 1 else [(size, 5), (310, PAYLOAD)]
    require([tag for tag in common if tag[0] in (92, 160, 310)] == proxy, 'Stale/missing/unordered common graphics')
    actual = [(code, struct.pack('>d', value)) for code, value in mesh if code in (10, 20, 30)]
    wanted = [(code, struct.pack('>d', value)) for point in points for code, value in zip((10, 20, 30), point)]
    require(actual == wanted, 'Wrong coordinate bits/order')
    require(one(mesh, 92) == 3 and one(mesh, 93) == 4 and one(mesh, 94) == 1 and one(mesh, 95) == 1, 'Topology counts changed')
    face = next(i for i, tag in enumerate(mesh) if tag[0] == 93)
    edge = next(i for i, tag in enumerate(mesh) if tag[0] == 94)
    crease = next(i for i, tag in enumerate(mesh) if tag[0] == 95)
    require(mesh[face + 1:edge] == [(90, 3), (90, 0), (90, 1), (90, 2)], 'Face indices changed')
    require(mesh[edge + 1:crease] == [(90, 0), (90, 1)] and one(mesh, 140) == 0.5, 'Edge indices or crease changed')


def unselected(row):
    result, section = [], ''
    for code, value in row:
        if code == 100:
            section = value
        elif code == 1001:
            section = 'XData'
        if section == 'AcDbEntity' and code in (92, 160, 310):
            continue
        if section == 'AcDbSubDMesh' and code in (10, 20, 30):
            continue
        result.append((code, value))
    return tuple(result)


def controls(row, name, year, source):
    count = 0
    for code in (5, 330, 10, 72, 91, 90, 140):
        damaged = list(row)
        index = next(i for i, tag in enumerate(row) if tag[0] == code)
        value = row[index][1]
        if code in (5, 330):
            value = '0'
        elif code in (10, 140):
            value = struct.unpack('>d', struct.pack('>Q', struct.unpack('>Q', struct.pack('>d', value))[0] ^ 1))[0]
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
    require(tags[at + 1] == (1, PROFILES[year]), 'Wrong profile')
    starts = [i for i, tag in enumerate(tags) if tag[0] == 0]
    rows = [tags[a:b] for a, b in zip(starts, starts[1:] + [len(tags)])]
    selected = [row for row in rows if (1001, 'MESH_VERTEX_EDIT') in row]
    require(len(selected) == len(NAMES), 'Wrong MESH subject count')
    document = ezdxf.readfile(path)
    result, handles, count = {}, set(), 0
    for row in selected:
        name = one(row, 1000)
        require(name not in result, 'Duplicate MESH subject')
        check_row(row, name, year, source)
        count += controls(row, name, year, source)
        handle = one(row, 5)
        require(handle not in handles, 'Duplicate MESH handle')
        handles.add(handle)
        result[name] = unselected(row)
        points, _, place = expected(name, source)
        mesh = document.entitydb[handle]
        require(mesh.dxftype() == 'MESH' and mesh.dxf.subdivision_levels == 2 and mesh.dxf.blend_crease == 1, 'Independent header mismatch')
        require(tuple(tuple(point) for point in mesh.vertices) == points, 'Independent edited geometry mismatch')
        require(tuple(tuple(face) for face in mesh.faces) == ((0, 1, 2),)
                and tuple(tuple(edge) for edge in mesh.edges) == ((0, 1),)
                and tuple(mesh.creases) == (0.5,), 'Independent topology/crease mismatch')
        layout = document.modelspace() if place == 0 else document.layouts.get('MV_PAPER') if place == 1 else document.blocks[f'MV_CONTAINER_{place}']
        require(mesh.dxf.owner == one(row, 330) == layout.block_record_handle, 'Independent placement/owner mismatch')
    require(set(result) == NAMES, 'Missing MESH subject')
    lines = list(document.modelspace().query('LINE'))
    require(len(lines) == 1 and tuple(lines[0].dxf.start) == (101., 102., 103.)
            and tuple(lines[0].dxf.end) == (104., 105., 106.), 'Following LINE changed')
    require(not any(any(values.values()) for values in audit_signature(document)), 'Independent audit errors/repairs')
    return result, count


def main(directory):
    names = {f'mesh-vertex-edit-AutoCad{year}-{transport}-{stage}.dxf' for year, transport, stage in
             itertools.product(PROFILES, ('text', 'binary'), ('source', 'output', 'resave'))}
    def inventory(actual):
        require(actual == names, 'Missing/extra MESH coordinate-edit fixtures')
    inventory({path.name for path in directory.glob('mesh-vertex-edit-*.dxf')})
    count = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        previous = None
        for stage in ('source', 'output', 'resave'):
            current, rejected = inspect(directory / f'mesh-vertex-edit-AutoCad{year}-{transport}-{stage}.dxf', year,
                                         (transport == 'binary') != (stage == 'resave'), stage == 'source')
            count += rejected
            if previous is not None:
                require(previous == current, 'Unselected physical fields or graph identities changed')
                damaged = dict(current)
                name = next(iter(damaged))
                row = list(damaged[name])
                index = next(i for i, tag in enumerate(row) if tag[0] == 5)
                row[index] = (5, 'FFFFFFFF')
                damaged[name] = tuple(row)
                count += reject(lambda: require(previous == damaged, 'Changed valid-looking handle'))
            previous = current
    print(f'PASS: {len(names)} actual drawings / {len(names) * len(NAMES)} MESH records; '
          f'{count} corruption/inventory controls rejected; no native AutoCAD acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_mesh_vertex_edits.py ARTIFACTS')
    main(Path(sys.argv[1]))
