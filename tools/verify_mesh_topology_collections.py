#!/usr/bin/env python3
"""Verify actual MESH face/edge list edits, ordered creases and unchanged records."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_raw_line_geometry import audit_signature
from verify_mesh_header_mutations import require, reject, one, part

PROFILES = {2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}
OPERATIONS = ('keep', 'face-first', 'face-last', 'face-remove', 'face-forward', 'face-backward',
              'face-same', 'edge-first', 'edge-last', 'edge-remove', 'edge-forward', 'edge-backward', 'edge-same', 'sequence')
NAMES = {f'MC_{p}_{c}_{op}' for p, c, op in itertools.product(range(4), range(3), OPERATIONS)}
POINTS = ((-0., 2., 3.), (4., 2., 3.), (1., 6., 3.), (4., 6., 3.), (10., 11., 12.), (14., 15., 16.))
FACES = ((0, 1, 2), (3, 2, 1), (0, 1, 3, 2), (4, 5, 0))
EDGES = ((0, 1), (2, 3), (0, 3), (4, 5))
CREASES = (-0., .5, -1., 2.5)
PAYLOAD = bytes((77, 67, 0, 255))


def bits(value):
    return struct.pack('>d', value)


def expected(name, source):
    require(name in NAMES, 'Unexpected subject name')
    _, place, cache, op = name.split('_')
    face_order = edge_order = (0, 1, 2)
    if not source:
        face_order = {'face-first': (3, 0, 1, 2), 'face-last': (0, 1, 2, 3), 'face-remove': (0, 2),
                      'face-forward': (1, 2, 0), 'face-backward': (2, 0, 1), 'sequence': (3, 1, 0)}.get(op, face_order)
        edge_order = {'edge-first': (3, 0, 1, 2), 'edge-last': (0, 1, 2, 3), 'edge-remove': (0, 2),
                      'edge-forward': (1, 2, 0), 'edge-backward': (2, 0, 1), 'sequence': (2, 0, 1)}.get(op, edge_order)
    cache = int(cache) if source or op in ('keep', 'face-same', 'edge-same') else 0
    return tuple(FACES[i] for i in face_order), tuple(EDGES[i] for i in edge_order), tuple(CREASES[i] for i in edge_order), cache, int(place)


def check_row(row, name, year, source):
    require(row[0] == (0, 'MESH'), 'Wrong entity type')
    faces, edges, creases, cache, _ = expected(name, source)
    common, mesh = part(row, 'AcDbEntity'), part(row, 'AcDbSubDMesh')
    require(one(row, 5) != '0' and one(row, 330) != '0', 'Zero/missing parent identity')
    require([t for t in row if t[0] >= 1000] == [(1001, 'MESH_COLLECTIONS'), (1000, name)], 'Unexpected XData')
    require(one(mesh, 72) == 1 and one(mesh, 91) == 2, 'Unselected subdivision fields changed')
    require(one(mesh, 92) == len(POINTS) and one(mesh, 93) == sum(1 + len(f) for f in faces)
            and one(mesh, 94) == len(edges) and one(mesh, 95) == len(creases), 'Wrong geometry/topology counts')
    wanted = [(c, bits(v)) for p in POINTS for c, v in zip((10, 20, 30), p)]
    require([(c, bits(v)) for c, v in mesh if c in (10, 20, 30)] == wanted, 'Coordinate bits/order changed')
    a = next(i for i, t in enumerate(mesh) if t[0] == 93)
    b = next(i for i, t in enumerate(mesh) if t[0] == 94)
    c = next(i for i, t in enumerate(mesh) if t[0] == 95)
    require(mesh[a+1:b] == [(90, v) for f in faces for v in (len(f), *f)], 'Wrong face count/arity/winding/order')
    require(mesh[b+1:c] == [(90, v) for e in edges for v in e], 'Wrong edge count/endpoints/order')
    require(mesh[c+1:c+1+len(creases)] == [(140, v) for v in creases], 'Incorrect crease packet order')
    require([bits(v) for code, v in mesh if code == 140] == list(map(bits, creases)), 'Crease bits or edge association changed')
    require(mesh[c+1+len(creases):] == [(90, 0)], 'Unexpected property override packet')
    size = 92 if year < 2013 else 160
    proxy = [] if cache == 0 else [(size, 0)] if cache == 1 else [(size, len(PAYLOAD)), (310, PAYLOAD)]
    require([t for t in common if t[0] in (92, 160, 310)] == proxy, 'Stale/missing/misframed proxy graphics')


def unchanged(row):
    # Remove only the modeled face/edge/crease packet using subclass-scoped boundaries.
    common_start = row.index((100, 'AcDbEntity'))
    mesh_start = row.index((100, 'AcDbSubDMesh'))
    a = next(i for i in range(mesh_start+1, len(row)) if row[i][0] == 93)
    c = next(i for i in range(a+1, len(row)) if row[i][0] == 95)
    end = c + 1 + row[c][1]
    return tuple((code, bits(value) if isinstance(value, float) else value)
                 for i, (code, value) in enumerate(row)
                 if not a <= i < end and not (common_start < i < mesh_start and code in (92, 160, 310)))


def controls(row, name, year, source):
    count = 0; mesh_start = row.index((100, 'AcDbSubDMesh'))
    for i, (code, value) in enumerate(row):
        if i <= mesh_start or code not in (10, 20, 30, 90, 92, 93, 94, 95, 140): continue
        damaged = list(row)
        if isinstance(value, float):
            value = struct.unpack('>d', struct.pack('>Q', struct.unpack('>Q', bits(value))[0] ^ 1))[0]
        else: value += 1
        damaged[i] = (code, value)
        count += reject(lambda: check_row(damaged, name, year, source))
    for code in (5, 330, 72, 91):
        i = next(i for i, t in enumerate(row) if t[0] == code)
        for duplicate in (False, True):
            damaged = list(row)
            if duplicate: damaged.insert(i, damaged[i])
            else: damaged.pop(i)
            count += reject(lambda: check_row(damaged, name, year, source))
    damaged, section = [], ''
    present = any(c in (92, 160, 310) for c, _ in part(row, 'AcDbEntity'))
    for tag in row:
        if tag[0] == 100: section = tag[1]
        if section == 'AcDbEntity' and tag[0] in (92, 160, 310): continue
        damaged.append(tag)
        if tag == (100, 'AcDbEntity') and not present: damaged.append((92 if year < 2013 else 160, 0))
    count += reject(lambda: check_row(damaged, name, year, source))
    return count


def inspect(path, year, binary, source):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_visibility_tags(path); i = tags.index((9, '$ACADVER'))
    require(tags[i+1] == (1, PROFILES[year]), 'Wrong version')
    starts = [i for i, t in enumerate(tags) if t[0] == 0]
    rows = [tags[a:b] for a, b in zip(starts, starts[1:] + [len(tags)])]
    subjects = [r for r in rows if (1001, 'MESH_COLLECTIONS') in r]
    require(len(subjects) == len(NAMES), 'Wrong subject inventory')
    doc = ezdxf.readfile(path); result, handles, count = {}, set(), 0
    for row in subjects:
        name = one(row, 1000); handle = one(row, 5)
        require(name not in result and handle not in handles, 'Duplicate subject name/handle')
        handles.add(handle); check_row(row, name, year, source); count += controls(row, name, year, source)
        result[name] = unchanged(row)
        faces, edges, creases, _, place = expected(name, source); mesh = doc.entitydb[handle]
        require([tuple(map(bits, p)) for p in mesh.vertices] == [tuple(map(bits, p)) for p in POINTS], 'Independent coordinate bits')
        require(tuple(tuple(f) for f in mesh.faces) == faces and tuple(tuple(e) for e in mesh.edges) == edges, 'Independent topology list values')
        require(tuple(map(bits, mesh.creases)) == tuple(map(bits, creases)), 'Independent crease association/bits')
        require(mesh.dxf.subdivision_levels == 2 and mesh.dxf.blend_crease == 1, 'Independent header values')
        layout = doc.modelspace() if place == 0 else doc.layouts.get('MC_PAPER') if place == 1 else doc.blocks[f'MC_CONTAINER_{place}']
        require(mesh.dxf.owner == one(row, 330) == layout.block_record_handle, 'Independent owner/placement')
    require(set(result) == NAMES, 'Missing subjects')
    lines = list(doc.modelspace().query('LINE'))
    require(len(lines) == 1 and tuple(lines[0].dxf.start) == (101., 102., 103.)
            and tuple(lines[0].dxf.end) == (104., 105., 106.), 'Following LINE changed')
    require(not any(any(c.values()) for c in audit_signature(doc)), 'Independent audit errors/repairs')
    return result, count


def main(directory):
    names = {f'mesh-collections-AutoCad{year}-{transport}-{stage}.dxf' for year, transport, stage in
             itertools.product(PROFILES, ('text', 'binary'), ('source', 'output', 'resave'))}
    def inventory(actual): require(actual == names, 'Missing/extra topology-collection drawings')
    inventory({p.name for p in directory.glob('mesh-collections-*.dxf')})
    count = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        previous = None
        for stage in ('source', 'output', 'resave'):
            result, n = inspect(directory / f'mesh-collections-AutoCad{year}-{transport}-{stage}.dxf', year,
                                (transport == 'binary') != (stage == 'resave'), stage == 'source'); count += n
            if previous is not None:
                require(previous == result, 'Unselected physical fields/identities changed')
                damaged = dict(result); name = next(iter(damaged)); row = list(damaged[name]); i = next(i for i, t in enumerate(row) if t[0] == 5)
                row[i] = (5, 'FFFFFFFF'); damaged[name] = tuple(row)
                count += reject(lambda: require(previous == damaged, 'Changed valid-looking parent handle'))
            previous = result
    print(f'PASS: {len(names)} actual drawings / {len(names)*len(NAMES)} MESH parents; {count} corruptions/inventory controls rejected; '
          'exact face/edge/crease lists, unchanged coordinates and zero independent audit errors/repairs. No native AutoCAD claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_mesh_topology_collections.py ARTIFACTS')
    main(Path(sys.argv[1]))
