#!/usr/bin/env python3
"""Verify actual legacy-mesh edits, exact packets and persistent child identities."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_dimlfac_fidelity import PROFILES, records
from verify_raw_line_geometry import require, reject, audit_signature

OPS = {
    'grid': ('none', 'set', 'zero', 'closedU', 'closedV', 'densityU', 'densityV', 'same', 'outside'),
    'face': ('none', 'set', 'zero', 'index', 'hidden', 'visible', 'terminate', 'same', 'sameEdge'),
}
NAMES = {f'ME_{kind}_{place}_{cache}_{op}' for kind in OPS for place, cache, op in
         itertools.product(range(4), range(3), OPS[kind])}
DENSITIES = ((None, None), (None, 5), (4, None), (0, 0), (1, 1), (2, 2), (-1, -2),
             (-32768, 32767), (32767, -32768), (201, 201), (202, 203), (4, 5))
DENSITY_NAMES = {f'MD_{i}' for i in range(len(DENSITIES))}
POINTS = tuple((float(i % 3), float(i // 3), float((i % 3) * (i // 3))) for i in range(9))
GRID_ORDER = tuple(u + 3 * v for u in range(3) for v in range(3))
PAYLOAD = b'MESH\0\xff'


def one(row, code):
    found = [v for c, v in row if c == code]
    require(len(found) == 1, f'Missing/duplicate field {code}')
    return found[0]


def bits(value):
    return struct.pack('>d', value)


def coordinate(row):
    return tuple(bits(one(row, code)) for code in (10, 20, 30))


def bitpoint(values):
    return tuple(bits(v) for v in values)


def xdata(row):
    return [tag for tag in row if tag[0] >= 1000]


def expected(name, source):
    points = list(POINTS)
    faces = [[1, 2, 5, 4], [-4, 5, 8, 7], [2, 3, 6]]
    if name.startswith('MD_'):
        require(name in DENSITY_NAMES, 'Unknown density identity')
        index = int(name[3:]); raw = DENSITIES[index]
        density = raw if source else tuple(v if v is not None else 0 for v in raw)
        return 'grid', 0, index % 3, points, faces, 16, density, 'ME_DENSITY'
    require(name in NAMES, 'Unknown edit identity')
    _, kind, place, cache, op = name.split('_')
    flags = 16 if kind == 'grid' else 64
    density = (4, 5)
    changed = not source and op not in ('none', 'same', 'sameEdge', 'outside')
    if not source:
        if op == 'set': points[4] = (20., 30., 40.)
        if op == 'zero': points[0] = (-0., 0., 0.)
        if op == 'closedU': flags |= 1
        if op == 'closedV': flags |= 32
        if op == 'densityU': density = (7, 5)
        if op == 'densityV': density = (4, 8)
        if op == 'index': faces[0][2] = 8
        if op == 'hidden': faces[0][0] = -1
        if op == 'visible': faces[1][0] = 4
        if op == 'terminate': faces[0][3] = 0
    return kind, int(place), 0 if changed else int(cache), points, faces, flags, density, 'MESH_EDITS'


def common_proxy(row):
    start = row.index((100, 'AcDbEntity')) + 1
    end = next((i for i in range(start, len(row)) if row[i][0] in (100, 1001)), len(row))
    return [tag for tag in row[start:end] if tag[0] in (92, 160, 310)]


def check_sequence(rows, year, name, source):
    kind, place, cache, points, faces, flags, density, app = expected(name, source)
    require(len(rows) == (11 if kind == 'grid' else 14), 'Sequence inventory')
    parent, end = rows[0], rows[-1]
    require(parent[0] == (0, 'POLYLINE') and end[0] == (0, 'SEQEND'), 'Sequence framing')
    require(xdata(parent) == [(1001, app), (1000, name)], 'Parent XData')
    require(one(parent, 70) == flags and one(parent, 75) == 0, 'Flags/smoothing changed')
    require(one(parent, 8) == '0', 'Parent layer')
    subclass = 'AcDbPolygonMesh' if kind == 'grid' else 'AcDbPolyFaceMesh'
    require(parent.count((100, subclass)) == 1, 'Parent subclass')
    if kind == 'grid':
        require(one(parent, 71) == 3 and one(parent, 72) == 3, 'Grid dimensions')
        for code, value in zip((73, 74), density):
            require([v for c, v in parent if c == code] == ([] if value is None else [value]), 'Stored density value/absence')
    else:
        require(not any(c in (71, 72, 73, 74) for c, v in parent), 'Unexpected polyface header counts/densities')
    length_code = 92 if year < 2013 else 160
    wanted_proxy = [] if cache == 0 else [(length_code, 0)] if cache == 1 else [(length_code, len(PAYLOAD)), (310, PAYLOAD)]
    require(common_proxy(parent) == wanted_proxy, 'Absent/empty/nonempty or stale parent proxy')
    handle = one(parent, 5)
    require(one(parent, 330) != '0', 'Parent owner')
    order = GRID_ORDER if kind == 'grid' else tuple(range(9))
    for index, row in zip(order, rows[1:10]):
        require(row[0] == (0, 'VERTEX') and one(row, 70) == (64 if kind == 'grid' else 192), 'Coordinate vertex kind')
        require(coordinate(row) == bitpoint(points[index]), 'Exact coordinate bits/order')
        require(one(row, 8) == '0', 'Coordinate layer')
    if kind == 'face':
        for row, face in zip(rows[10:13], faces):
            require(row[0] == (0, 'VERTEX') and one(row, 70) == 128 and row.count((100, 'AcDbFaceRecord')) == 1, 'Face record kind')
            require([tag for tag in row if 71 <= tag[0] <= 74] == list(zip(range(71, 71 + len(face)), face)), 'Signed physical face slots/visibility/termination')
            require(coordinate(row) == bitpoint((0., 0., 0.)), 'Face dummy position')
    require(one(end, 8) == '0' and end.count((100, 'AcDbEntity')) == 1, 'SEQEND layer/subclass')
    indexes = (*order, 9) if kind == 'grid' else tuple(range(13))
    for row, index in zip(rows[1:], indexes):
        require(one(row, 330) == handle, 'Actual child owner')
        require(xdata(row) == ([] if app == 'ME_DENSITY' else [(1001, 'ME_CHILD'), (1000, name + '/' + str(index))]), 'Child metadata/order')
    ids = [one(row, 5) for row in rows]
    require(len(set(ids)) == len(ids) and '0' not in ids, 'Duplicate/zero sequence identity')
    return ids


def invariant(rows):
    """Omit only independently checked editable scalars; never normalize identities."""
    result = []
    for index, row in enumerate(rows):
        skip = {70, 73, 74, 92, 160, 310} if index == 0 else {10, 20, 30, 71, 72, 73, 74} if row[0] == (0, 'VERTEX') else set()
        result.append(tuple((c, bits(v) if type(v) is float else v) for c, v in row if c not in skip))
    return tuple(result)


def check_transition(before, after):
    require(set(before) == set(after), 'Cross-save inventory')
    for name in before:
        require(invariant(before[name]) == invariant(after[name]), 'Changed identity/owner/metadata/unselected packet: ' + name)


def controls(rows, year, name, source):
    count = 0
    def corrupted(row_index, code, value):
        nonlocal count
        altered = [list(row) for row in rows]
        index = next(i for i, tag in enumerate(altered[row_index]) if tag[0] == code)
        altered[row_index][index] = (code, value)
        count += reject(lambda: check_sequence(altered, year, name, source))
    corrupted(0, 70, one(rows[0], 70) ^ 1)
    corrupted(0, 75, 5)
    for row_index in range(1, 10):
        for code in (10, 20, 30):
            value = one(rows[row_index], code)
            altered_bits = struct.unpack('>Q', bits(value))[0] ^ 1
            corrupted(row_index, code, struct.unpack('>d', struct.pack('>Q', altered_bits))[0])
    for code in (73, 74):
        present = [v for c, v in rows[0] if c == code]
        if present: corrupted(0, code, present[0] ^ 1)
    for row_index in (1, len(rows) - 1):
        corrupted(row_index, 330, '0')
        corrupted(row_index, 5, one(rows[0], 5))
    if len(rows) == 14:
        for row_index in range(10, 13):
            for c, v in rows[row_index]:
                if 71 <= c <= 74: corrupted(row_index, c, v ^ 1)
    altered = [list(row) for row in rows]
    if common_proxy(rows[0]):
        altered[0] = [tag for tag in rows[0] if tag[0] not in (92, 160, 310)]
    else:
        at = altered[0].index((100, 'AcDbEntity')) + 1
        altered[0][at:at] = [(92 if year < 2013 else 160, len(PAYLOAD)), (310, PAYLOAD)]
    count += reject(lambda: check_sequence(altered, year, name, source))
    count += reject(lambda: check_sequence(rows[:-1], year, name, source))
    count += reject(lambda: check_sequence([rows[0], *reversed(rows[1:10]), *rows[10:]], year, name, source))
    return count


def inspect(path, year, binary, source, density):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load_visibility_tags(path); at = tags.index((9, '$ACADVER'))
    require(tags[at + 1] == (1, PROFILES[year]), 'Version')
    parsed = [tags[a:b] for a, b in records(tags)]
    app = 'ME_DENSITY' if density else 'MESH_EDITS'
    expected_names = DENSITY_NAMES if density else NAMES
    result, count, ids = {}, 0, []
    for index, row in enumerate(parsed):
        if row[0] != (0, 'POLYLINE') or (1001, app) not in row: continue
        name = one(row, 1000); require(name not in result, 'Duplicate subject')
        end = index + 1
        while end < len(parsed) and parsed[end][0] == (0, 'VERTEX'): end += 1
        require(end < len(parsed), 'Missing terminator')
        sequence = parsed[index:end + 1]
        ids.extend(check_sequence(sequence, year, name, source)); count += controls(sequence, year, name, source)
        result[name] = sequence
    require(set(result) == expected_names and len(ids) == len(set(ids)), 'Whole-file inventory/global identities')
    doc = ezdxf.readfile(path)
    for name, sequence in result.items():
        kind, place, cache, points, faces, flags, density_values, _ = expected(name, source)
        entity = doc.entitydb[one(sequence[0], 5)]
        require(entity.dxf.flags == flags and entity.dxf.smooth_type == 0, 'Independent flags')
        expected_order = GRID_ORDER if kind == 'grid' else tuple(range(9))
        require([bitpoint(tuple(v.dxf.location)) for v in entity.vertices[:9]] == [bitpoint(points[i]) for i in expected_order], 'Independent coordinates')
        require([v.dxf.handle for v in entity.vertices] + [entity.seqend.dxf.handle] == [one(row, 5) for row in sequence[1:]], 'Independent child identity')
        if kind == 'grid':
            require((entity.dxf.m_count, entity.dxf.n_count) == (3, 3), 'Independent grid dimensions')
            require((entity.dxf.m_smooth_density, entity.dxf.n_smooth_density) == tuple(v or 0 for v in density_values), 'Independent stored density')
        else:
            for vertex, face in zip(entity.vertices[9:], faces):
                require([vertex.dxf.get('vtx' + str(i), 0) for i in range(4)] == face + [0] * (4 - len(face)), 'Independent face indices')
        layout = doc.modelspace() if place == 0 else doc.layouts.get('ME_PAPER') if place == 1 else doc.blocks[f'ME_CONTAINER_{place}']
        require(entity.dxf.owner == layout.block_record_handle, 'Independent placement')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (101., 102., 103.) and tuple(line.dxf.end) == (104., 105., 106.), 'Following LINE')
    require(not any(any(values.values()) for values in audit_signature(doc)), 'Independent graph errors/repairs')
    return result, count


def main(directory):
    names = {f'legacy-mesh-{group}-AutoCad{year}-{transport}-{stage}.dxf' for group, year, transport, stage in
             itertools.product(('edits', 'density'), PROFILES, ('text', 'binary'), ('source', 'output', 'resave'))}
    def inventory(actual): require(actual == names, 'Missing/extra mesh drawings')
    inventory({p.name for p in directory.glob('legacy-mesh-*.dxf')})
    count = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for group, year, transport in itertools.product(('edits', 'density'), PROFILES, ('text', 'binary')):
        previous = None
        for stage in ('source', 'output', 'resave'):
            binary = False if group == 'density' and stage == 'source' else (transport == 'binary') != (stage == 'resave')
            actual, errors = inspect(directory / f'legacy-mesh-{group}-AutoCad{year}-{transport}-{stage}.dxf', year, binary, stage == 'source', group == 'density')
            if previous is not None:
                check_transition(previous, actual)
                # A structurally valid replacement identity still must fail cross-save identity checks.
                name = next(iter(actual)); altered = dict(actual); altered[name] = [list(row) for row in actual[name]]
                at = next(i for i, tag in enumerate(altered[name][-1]) if tag[0] == 5)
                altered[name][-1][at] = (5, 'FFFFFFFF')
                count += reject(lambda: check_transition(previous, altered))
            previous = actual; count += errors
    print(f'PASS: {len(names)} drawings / {36 * (len(NAMES) + len(DENSITY_NAMES))} mesh parents; '
          f'{count} corruption/inventory controls rejected; exact geometry, signed face slots, dormant densities, '
          'proxy packets and unchanged child identities; zero independent graph errors/repairs. No native AutoCAD acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_legacy_mesh_edits.py ARTIFACTS')
    main(Path(sys.argv[1]))
