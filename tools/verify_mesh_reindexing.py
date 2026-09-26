#!/usr/bin/env python3
"""Verify actual MESH vertex reindexing, preserved geometry and unselected records."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_raw_line_geometry import audit_signature
from verify_mesh_header_mutations import require, reject, one, part

PROFILES = {2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}
OPERATIONS = ('keep', 'first', 'middle', 'last', 'remove', 'forward', 'backward', 'same')
NAMES = {f'MR_{p}_{c}_{op}' for p, c, op in itertools.product(range(4), range(3), OPERATIONS)}
POINTS = ((-0., 2., 3.), (11., 12., 13.), (4., 2., 3.), (1., 6., 3.), (4., 6., 3.))
FACES = ((0, 2, 3), (3, 2, 4, 0))
EDGES = ((0, 2), (4, 3))
CREASES = (-0., -1.)
ADDED = (99., -7., 5.)
PAYLOAD = bytes((77, 82, 0, 255))


def bits(value):
    return struct.pack('>d', value)


def expected(name, source):
    require(name in NAMES, 'Unexpected subject')
    _, place, cache, operation = name.split('_')
    order = list(range(5))
    if not source:
        if operation == 'first': order.insert(0, -1)
        elif operation == 'middle': order.insert(2, -1)
        elif operation == 'last': order.append(-1)
        elif operation == 'remove': order.remove(1)
        elif operation == 'forward': order = [1, 2, 3, 4, 0]
        elif operation == 'backward': order = [4, 0, 1, 2, 3]
    points = tuple(ADDED if i == -1 else POINTS[i] for i in order)
    faces = tuple(tuple(order.index(i) for i in face) for face in FACES)
    edges = tuple(tuple(order.index(i) for i in edge) for edge in EDGES)
    cache = int(cache) if source or operation in ('keep', 'same') else 0
    return points, faces, edges, cache, int(place)


def check_row(row, name, year, source):
    require(row[0] == (0, 'MESH'), 'Not a MESH record')
    points, faces, edges, cache, _ = expected(name, source)
    common, mesh = part(row, 'AcDbEntity'), part(row, 'AcDbSubDMesh')
    require(one(row, 5) != '0' and one(row, 330) != '0', 'Missing/zero handle or owner')
    require([tag for tag in row if tag[0] >= 1000] == [(1001, 'MESH_REINDEX'), (1000, name)], 'Unexpected XData')
    require(one(mesh, 72) == 1 and one(mesh, 91) == 2, 'Subdivision headers changed')
    require(one(mesh, 92) == len(points) and one(mesh, 93) == 9 and one(mesh, 94) == 2 and one(mesh, 95) == 2, 'Incorrect topology counts')
    actual = [(c, bits(v)) for c, v in mesh if c in (10, 20, 30)]
    wanted = [(c, bits(v)) for p in points for c, v in zip((10, 20, 30), p)]
    require(actual == wanted, 'Coordinate bits/order mismatch')
    a = next(i for i, t in enumerate(mesh) if t[0] == 93)
    b = next(i for i, t in enumerate(mesh) if t[0] == 94)
    c = next(i for i, t in enumerate(mesh) if t[0] == 95)
    require(mesh[a + 1:b] == [(90, v) for face in faces for v in (len(face), *face)], 'Wrong remapped face packet')
    require(mesh[b + 1:c] == [(90, v) for edge in edges for v in edge], 'Wrong remapped edge packet')
    require([bits(v) for code, v in mesh if code == 140] == [bits(v) for v in CREASES], 'Crease bits changed')
    length_code = 92 if year < 2013 else 160
    proxy = [] if cache == 0 else [(length_code, 0)] if cache == 1 else [(length_code, len(PAYLOAD)), (310, PAYLOAD)]
    require([t for t in common if t[0] in (92, 160, 310)] == proxy, 'Stale/missing/incorrect common graphics')


def unselected(row):
    result, section, indices = [], '', False
    for code, value in row:
        if code == 100:
            section, indices = value, False
        elif code == 1001:
            section, indices = 'XData', False
        if section == 'AcDbEntity' and code in (92, 160, 310): continue
        if section == 'AcDbSubDMesh':
            if code == 93: indices = True
            elif code == 95: indices = False
            if code in (92, 10, 20, 30) or (indices and code == 90): continue
        result.append((code, bits(value) if isinstance(value, float) else value))
    return tuple(result)


def controls(row, name, year, source):
    count = 0
    geom = row.index((100, 'AcDbSubDMesh'))
    for index, (code, value) in enumerate(row):
        if index < geom or code not in (10, 20, 30, 90, 92, 93, 94, 95, 140): continue
        # Group 90 after the two crease values is the unrelated override count.
        if code == 90 and index > next(i for i, t in enumerate(row) if i > geom and t[0] == 95): continue
        changed = list(row)
        if isinstance(value, float):
            number = struct.unpack('>Q', bits(value))[0] ^ 1
            value = struct.unpack('>d', struct.pack('>Q', number))[0]
        else: value += 1
        changed[index] = (code, value)
        count += reject(lambda: check_row(changed, name, year, source))
    for code in (5, 330, 72, 91):
        index = next(i for i, tag in enumerate(row) if tag[0] == code)
        for duplicate in (False, True):
            changed = list(row)
            if duplicate: changed.insert(index, changed[index])
            else: changed.pop(index)
            count += reject(lambda: check_row(changed, name, year, source))
    changed, section = [], ''
    present = any(code in (92, 160, 310) for code, _ in part(row, 'AcDbEntity'))
    for tag in row:
        if tag[0] == 100: section = tag[1]
        if section == 'AcDbEntity' and tag[0] in (92, 160, 310): continue
        changed.append(tag)
        if tag == (100, 'AcDbEntity') and not present: changed.append((92 if year < 2013 else 160, 0))
    count += reject(lambda: check_row(changed, name, year, source))
    return count


def inspect(path, year, binary, source):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_visibility_tags(path); index = tags.index((9, '$ACADVER'))
    require(tags[index + 1] == (1, PROFILES[year]), 'Wrong format version')
    starts = [i for i, tag in enumerate(tags) if tag[0] == 0]
    rows = [tags[a:b] for a, b in zip(starts, starts[1:] + [len(tags)])]
    subjects = [r for r in rows if (1001, 'MESH_REINDEX') in r]
    require(len(subjects) == len(NAMES), 'Subject count mismatch')
    doc = ezdxf.readfile(path); result, handles, count = {}, set(), 0
    for row in subjects:
        name = one(row, 1000); require(name not in result, 'Duplicate subject label')
        handle = one(row, 5); require(handle not in handles, 'Duplicate subject handle'); handles.add(handle)
        check_row(row, name, year, source); count += controls(row, name, year, source); result[name] = unselected(row)
        points, faces, edges, _, place = expected(name, source); mesh = doc.entitydb[handle]
        require([tuple(map(bits, p)) for p in mesh.vertices] == [tuple(map(bits, p)) for p in points], 'Independent coordinate bits')
        require(tuple(tuple(f) for f in mesh.faces) == faces and tuple(tuple(e) for e in mesh.edges) == edges, 'Independent topology indices')
        require(tuple(map(bits, mesh.creases)) == tuple(map(bits, CREASES)), 'Independent crease bits')
        require(mesh.dxf.subdivision_levels == 2 and mesh.dxf.blend_crease == 1, 'Independent subdivision fields')
        for current, original in zip(mesh.faces, FACES):
            require([tuple(map(bits, mesh.vertices[v])) for v in current] == [tuple(map(bits, POINTS[v])) for v in original], 'Face geometry changed')
        for current, original in zip(mesh.edges, EDGES):
            require([tuple(map(bits, mesh.vertices[v])) for v in current] == [tuple(map(bits, POINTS[v])) for v in original], 'Edge geometry changed')
        layout = doc.modelspace() if place == 0 else doc.layouts.get('MR_PAPER') if place == 1 else doc.blocks[f'MR_CONTAINER_{place}']
        require(mesh.dxf.owner == one(row, 330) == layout.block_record_handle, 'Owner/placement changed')
    require(set(result) == NAMES, 'Missing subject labels')
    lines = list(doc.modelspace().query('LINE'))
    require(len(lines) == 1 and tuple(lines[0].dxf.start) == (101., 102., 103.) and tuple(lines[0].dxf.end) == (104., 105., 106.), 'Following LINE changed')
    require(not any(any(counts.values()) for counts in audit_signature(doc)), 'Independent audit errors/repairs')
    return result, count


def main(directory):
    names = {f'mesh-reindex-AutoCad{year}-{transport}-{stage}.dxf' for year, transport, stage in itertools.product(PROFILES, ('text', 'binary'), ('source', 'output', 'resave'))}
    def inventory(actual): require(actual == names, 'Missing/extra reindex drawings')
    inventory({p.name for p in directory.glob('mesh-reindex-*.dxf')})
    count = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        previous = None
        for stage in ('source', 'output', 'resave'):
            result, rejected = inspect(directory / f'mesh-reindex-AutoCad{year}-{transport}-{stage}.dxf', year,
                                       (transport == 'binary') != (stage == 'resave'), stage == 'source')
            count += rejected
            if previous is not None:
                require(previous == result, 'Unselected fields/identities changed across save')
                changed = dict(result); name = next(iter(result)); row = list(changed[name]); i = next(i for i, t in enumerate(row) if t[0] == 5)
                row[i] = (5, 'FFFFFFFF'); changed[name] = tuple(row)
                count += reject(lambda: require(previous == changed, 'Changed valid-looking handle'))
            previous = result
    print(f'PASS: {len(names)} drawings / {len(names)*len(NAMES)} MESH records; {count} corruption/inventory controls rejected; '
          'preserved face/edge geometry and zero independent audit errors/repairs. No native AutoCAD acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_mesh_reindexing.py ARTIFACTS')
    main(Path(sys.argv[1]))
