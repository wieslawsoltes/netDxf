#!/usr/bin/env python3
"""Check actual typed 3D-polyline edit output, parent graphics and retained identities."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.lldxf.tags import Tags
from ezdxf.lldxf.types import DXFTag
from ezdxf.proxygraphic import load_proxy_graphic
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_dimlfac_fidelity import PROFILES, records
from verify_raw_line_geometry import require, reject, audit_signature

POINTS = ((1., 2., 3.), (4., 6., 8.), (-2., 7., 5.), (11., -3., 9.))
OPS = ('none', 'set', 'zero', 'insert', 'remove', 'move', 'reverse', 'closed', 'linetype', 'same-index', 'same-set')
NAMES = {f'P3_{place}_{cache}_{op}' for place, cache, op in itertools.product(range(4), range(3), OPS)}
PAYLOAD = bytes((80, 51, 71, 0, 255))


def one(row, code):
    values = [value for c, value in row if c == code]
    require(len(values) == 1, f'Missing/duplicate field {code}')
    return values[0]


def point_bits(point):
    return tuple(struct.pack('>d', value) for value in point)


def expected(name, source):
    prefix, place, cache, operation = name.split('_')
    require(name in NAMES and prefix == 'P3', 'Unknown subject identity')
    points = list(POINTS)
    if not source:
        if operation == 'set': points[1] = (20., 30., 40.)
        if operation == 'zero': points[0] = (-0., 2., 3.)
        if operation == 'insert': points.insert(1, (20., 30., 40.))
        if operation == 'remove': points.pop(1)
        if operation == 'move': points = [POINTS[i] for i in (1, 2, 0, 3)]
        if operation == 'reverse': points.reverse()
    flags = 8 | (1 if not source and operation == 'closed' else 0) | (128 if not source and operation == 'linetype' else 0)
    changed = not source and operation not in ('none', 'same-index', 'same-set')
    cache = 0 if changed else int(cache)
    return points, flags, cache


def parent_proxy(row):
    start = row.index((100, 'AcDbEntity')) + 1
    end = next((i for i in range(start, len(row)) if row[i][0] in (100, 1001)), len(row))
    return [tag for tag in row[start:end] if tag[0] in (92, 160, 310)]


def check_sequence(parent, vertices, end, year, name, source):
    require(parent[0] == (0, 'POLYLINE') and end[0] == (0, 'SEQEND'), 'Wrong sequence framing')
    points, flags, cache = expected(name, source)
    require([tag for tag in parent if tag[0] >= 1000] == [(1001, 'P3_EDITS'), (1000, name)], 'Parent XData changed')
    require(one(parent, 70) == flags and one(parent, 75) == 0, 'Closed/linetype/smoothing flags changed')
    require(parent.count((100, 'AcDb3dPolyline')) == 1 and one(parent, 8) == '0', 'Parent subclass/layer')
    count_code = 92 if year < 2013 else 160
    proxy = [] if cache == 0 else [(count_code, 0)] if cache == 1 else [(count_code, len(PAYLOAD)), (310, PAYLOAD)]
    require(parent_proxy(parent) == proxy, 'Absent/empty/nonempty or stale ordered parent proxy packet')
    handle = one(parent, 5)
    require(handle != '0' and one(parent, 330) != '0', 'Parent identity/owner missing')
    require(len(vertices) == len(points), 'Wrong VERTEX count')
    ids = []
    for row, point in zip(vertices, points):
        require(row[0] == (0, 'VERTEX') and one(row, 70) == 32, 'Wrong VERTEX kind')
        require(one(row, 330) == handle and one(row, 8) == '0', 'VERTEX structural owner/layer')
        require(point_bits(tuple(one(row, c) for c in (10, 20, 30))) == point_bits(point), 'VERTEX coordinate bits/order')
        require(row.count((100, 'AcDb3dPolylineVertex')) == 1, 'VERTEX subclass')
        ids.append(one(row, 5))
    require(one(end, 330) == handle and one(end, 8) == '0' and end.count((100, 'AcDbEntity')) == 1, 'SEQEND owner/layer/subclass')
    end_id = one(end, 5)
    require(len({handle, end_id, *ids}) == len(ids) + 2 and '0' not in {handle, end_id, *ids}, 'Duplicate/zero sequence identity')
    return handle, one(parent, 330), tuple(ids), end_id


def check_transition(before, after, first):
    require(set(before) == set(after) == NAMES, 'Cross-save identity inventory')
    old_all = {value for identity in before.values() for value in (identity[0], *identity[2], identity[3])}
    new_all = [value for identity in after.values() for value in (identity[0], *identity[2], identity[3])]
    require(len(new_all) == len(set(new_all)), 'Duplicate cross-entity identity')
    for name, previous in before.items():
        current = after[name]
        require(previous[:2] == current[:2] and previous[3] == current[3], 'Parent/owner/SEQEND identity changed')
        wanted = list(previous[2]); operation = name.split('_')[3]
        if first:
            if operation == 'insert':
                require(current[2][1] not in old_all, 'Inserted VERTEX reused old identity')
                wanted.insert(1, current[2][1])
            if operation == 'remove':
                retired = wanted.pop(1); require(retired not in new_all, 'Removed VERTEX identity reused')
            if operation == 'move': wanted = [wanted[i] for i in (1, 2, 0, 3)]
            if operation == 'reverse': wanted.reverse()
        require(tuple(wanted) == current[2], 'VERTEX identity/order changed across edit/save')


def controls(parent, vertices, end, year, name, source):
    count = 0
    for code in (70, 75):
        index = next(i for i, tag in enumerate(parent) if tag[0] == code)
        damaged = list(parent); damaged[index] = (code, damaged[index][1] ^ 1)
        count += reject(lambda: check_sequence(damaged, vertices, end, year, name, source))
    if parent_proxy(parent):
        damaged = [tag for tag in parent if tag[0] not in (92, 160, 310)]
    else:
        damaged = list(parent); index = damaged.index((100, 'AcDbEntity')) + 1
        damaged[index:index] = [(92 if year < 2013 else 160, 5), (310, PAYLOAD)]
    count += reject(lambda: check_sequence(damaged, vertices, end, year, name, source))
    for vertex in range(len(vertices)):
        for code in (10, 20, 30):
            rows = [list(row) for row in vertices]; index = next(i for i, tag in enumerate(rows[vertex]) if tag[0] == code)
            bits = struct.unpack('>Q', struct.pack('>d', rows[vertex][index][1]))[0] ^ 1
            rows[vertex][index] = (code, struct.unpack('>d', struct.pack('>Q', bits))[0])
            count += reject(lambda: check_sequence(parent, rows, end, year, name, source))
    for code in (5, 330):
        index = next(i for i, tag in enumerate(end) if tag[0] == code)
        for mode in ('remove', 'duplicate', 'zero'):
            damaged_end = list(end)
            if mode == 'remove': damaged_end.pop(index)
            elif mode == 'duplicate': damaged_end.insert(index, damaged_end[index])
            else: damaged_end[index] = (code, '0')
            count += reject(lambda: check_sequence(parent, vertices, damaged_end, year, name, source))
    count += reject(lambda: check_sequence(parent, vertices[:-1], end, year, name, source))
    count += reject(lambda: check_sequence(parent, list(reversed(vertices)), end, year, name, source))
    return count


def inspect(path, year, binary, source):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_visibility_tags(path); index = tags.index((9, '$ACADVER'))
    require(tags[index + 1] == (1, PROFILES[year]), 'Wrong version')
    rows = [tags[a:b] for a, b in records(tags)]
    selected = [(i, row) for i, row in enumerate(rows) if row[0] == (0, 'POLYLINE') and (1001, 'P3_EDITS') in row]
    require(len(selected) == len(NAMES), 'Subject count')
    result, count, packets = {}, 0, {}
    for index, parent in selected:
        name = one(parent, 1000); require(name not in result, 'Duplicate subject')
        vertices = []; at = index + 1
        while at < len(rows) and rows[at][0] == (0, 'VERTEX'): vertices.append(rows[at]); at += 1
        require(at < len(rows), 'Missing terminal record'); end = rows[at]
        result[name] = check_sequence(parent, vertices, end, year, name, source)
        count += controls(parent, vertices, end, year, name, source); packets[name] = parent
    require(set(result) == NAMES, 'Subject inventory')
    all_ids = [v for identity in result.values() for v in (identity[0], *identity[2], identity[3])]
    require(len(all_ids) == len(set(all_ids)), 'Duplicate global sequence identity')
    doc = ezdxf.readfile(path)
    for name, identity in result.items():
        entity = doc.entitydb[identity[0]]; points, flags, cache = expected(name, source)
        require(entity.dxf.flags == flags and entity.dxf.owner == identity[1], 'Independent flags/owner')
        require([point_bits(tuple(v.dxf.location)) for v in entity.vertices] == [point_bits(p) for p in points], 'Independent coordinate bits')
        require(tuple(v.dxf.handle for v in entity.vertices) == identity[2] and entity.seqend.dxf.handle == identity[3], 'Independent sequence identity')
        place = int(name.split('_')[1]); layout = doc.modelspace() if place == 0 else doc.layouts.get('P3_PAPER') if place == 1 else doc.blocks[f'P3_CONTAINER_{place}']
        require(entity.dxf.owner == layout.block_record_handle, 'Independent placement')
        packet = Tags(DXFTag(c, v) for c, v in packets[name])
        require(load_proxy_graphic(packet, length_code=92 if year < 2013 else 160) == (PAYLOAD if cache == 2 else None), 'Independent proxy extraction')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (101., 102., 103.) and tuple(line.dxf.end) == (104., 105., 106.), 'Following LINE')
    require(not any(any(values.values()) for values in audit_signature(doc)), 'Independent graph errors/repairs')
    return result, count


def main(directory):
    names = {f'polyline3d-edits-AutoCad{year}-{transport}-{stage}.dxf' for year, transport, stage in
             itertools.product(PROFILES, ('text', 'binary'), ('source', 'output', 'resave'))}
    def inventory(actual): require(actual == names, 'Missing/extra polyline edit drawings')
    inventory({p.name for p in directory.glob('polyline3d-edits-*.dxf')})
    count = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        previous = None
        for stage in ('source', 'output', 'resave'):
            actual, errors = inspect(directory/f'polyline3d-edits-AutoCad{year}-{transport}-{stage}.dxf', year,
                                     (transport == 'binary') != (stage == 'resave'), stage == 'source')
            if previous is not None:
                check_transition(previous, actual, stage == 'output')
                # All individually valid identities must remain stable after a subsequent save.
                name = next(iter(actual)); bad = dict(actual); identity = list(actual[name]); identity[3] = 'FFFFFFFF'
                bad[name] = tuple(identity); count += reject(lambda: check_transition(previous, bad, stage == 'output'))
            previous = actual; count += errors
    print(f'PASS: {len(names)} drawings / {len(names)*len(NAMES)} POLYLINE records; {count} corruption/inventory controls rejected; '
          'exact coordinate bits, parent graphics, retained identities and zero independent graph errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_polyline3d_edits.py ARTIFACTS')
    main(Path(sys.argv[1]))
