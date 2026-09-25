#!/usr/bin/env python3
"""Check actual 2D-polyline edits, sparse widths, parent proxies and stable records."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.lldxf.tags import Tags
from ezdxf.lldxf.types import DXFTag
from ezdxf.proxygraphic import load_proxy_graphic
from verify_dimlfac_fidelity import PROFILES, records
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_raw_line_geometry import require, reject, audit_signature

OPS = ('none', 'position', 'zero', 'bulge', 'widths', 'clear', 'closed', 'linetype',
       'elevation', 'thickness', 'same-position', 'same-bulge', 'same-widths', 'same-header')
NAMES = {f'P2_{place}_{cache}_{op}' for place, cache, op in itertools.product(range(4), range(3), OPS)}
PAYLOAD = bytes((80, 50, 69, 0, 255))
VERTEX_CODES = {10, 20, 30, 40, 41, 42, 91}


def one(row, code, optional=False):
    values = [value for key, value in row if key == code]
    require(len(values) == 1 or (optional and not values), f'Missing/duplicate group {code}')
    return values[0] if values else None


def bits(value):
    require(isinstance(value, float), 'Expected an actual real-valued group')
    return struct.pack('>d', value)


def scalar(actual, expected):
    require((actual is None) == (expected is None), 'Optional scalar presence changed')
    if expected is not None:
        require(bits(actual) == bits(expected), 'Scalar bits changed')


def section(row, marker):
    require(row.count((100, marker)) == 1, 'Missing/duplicate subclass ' + marker)
    start = row.index((100, marker)) + 1
    end = next((i for i in range(start, len(row)) if row[i][0] in (100, 1001)), len(row))
    return start, end


def proxy(row):
    start, end = section(row, 'AcDbEntity')
    return [tag for tag in row[start:end] if tag[0] in (92, 160, 310)]


def strip_proxy(row):
    start, end = section(row, 'AcDbEntity')
    return row[:start] + [tag for tag in row[start:end] if tag[0] not in (92, 160, 310)] + row[end:]


def expected(name, source, year, legacy):
    require(name in NAMES, 'Unknown subject ' + name)
    _, place, cache, operation = name.split('_')
    points = [[1., 2., .25, None, .5], [4., 6., -.5, .75, None],
              [-2., 7., 0., 1., 1.5], [11., -3., 0., None, None]]
    if not source:
        if operation == 'position': points[1][:2] = [20., 30.]
        if operation == 'zero': points[1][:2] = [-0., 6.]
        if operation == 'bulge': points[1][2] = .75
        if operation == 'widths': points[1][3:] = [2., 4.]
        if operation == 'clear': points[1][3:] = [None, None]
    ids = list(range(101, 105)) if legacy or year >= 2013 else [None] * 4
    elevation = 9. if not source and operation == 'elevation' else 3.
    thickness = -2. if not source and operation == 'thickness' else .5
    flags = (1 if not source and operation == 'closed' else 0) | (128 if not source and operation == 'linetype' else 0)
    changed = not source and operation != 'none' and not operation.startswith('same-')
    return points, ids, elevation, thickness, flags, 0 if changed else int(cache), int(place)


def lw_vertices(row):
    start, end = section(row, 'AcDbPolyline')
    positions = [i for i in range(start, end) if row[i][0] == 10]
    result = []
    for a, b in zip(positions, positions[1:] + [end]):
        result.append([tag for tag in row[a:b] if tag[0] in VERTEX_CODES])
    return result


def check_subject(parent, vertices, terminator, name, source, year, legacy):
    points, ids, elevation, thickness, flags, cache, _ = expected(name, source, year, legacy)
    require(parent[0] == (0, 'POLYLINE' if legacy else 'LWPOLYLINE'), 'Wrong parent representation')
    require([tag for tag in parent if tag[0] >= 1000] == [(1001, 'P2_EDITS'), (1000, name)], 'Parent metadata changed')
    begin, finish = section(parent, 'AcDb2dPolyline' if legacy else 'AcDbPolyline')
    header = parent[begin:finish]
    require(one(header, 70) == flags and one(parent, 8) == '0', 'Parent flags/layer')
    scalar(one(header, 30 if legacy else 38), elevation)
    scalar(one(header, 39), thickness)
    for code, wanted in ((210, 0.), (220, 0.), (230, 1.)):
        scalar(one(header, code), wanted)
    if legacy:
        scalar(one(header, 10), 0.); scalar(one(header, 20), 0.)
        scalar(one(header, 40), 2.); scalar(one(header, 41), 3.)
        require(one(header, 75) == 0, 'Unexpected smoothing')
    else:
        require(one(header, 90) == 4 and not any(code == 43 for code, _ in header), 'Count/constant width changed')
    size_code = 92 if year < 2013 else 160
    wanted_proxy = [] if cache == 0 else [(size_code, 0)] if cache == 1 else [(size_code, len(PAYLOAD)), (310, PAYLOAD)]
    require(proxy(parent) == wanted_proxy, 'Absent/empty/nonempty or stale ordered parent proxy')
    handle, owner = one(parent, 5), one(parent, 330)
    require(handle != '0' and owner != '0', 'Parent handle/owner missing')
    require(len(vertices) == 4, 'Vertex inventory')
    handles = []
    for index, (vertex, point, identifier) in enumerate(zip(vertices, points, ids)):
        if legacy:
            require(vertex[0] == (0, 'VERTEX'), 'Wrong legacy framing')
            a, b = section(vertex, 'AcDb2dVertex'); geometry = vertex[a:b]
            require(one(vertex, 330) == (handle if index % 2 == 0 else owner), 'Physical legacy owner form changed')
            require(one(vertex, 8) == '0' and one(geometry, 70) == 0, 'Child layer/flags changed')
            require([tag for tag in vertex if tag[0] >= 1000] == [(1001, 'P2_RECORD'), (1000, f'vertex-{index}')], 'Child XData changed')
            scalar(one(geometry, 30), 0.); handles.append(one(vertex, 5))
        else:
            geometry = vertex
        for code, value in zip((10, 20, 42, 40, 41), point):
            scalar(one(geometry, code, optional=code in (40, 41)), value)
        require(one(geometry, 91, optional=True) == identifier, 'Vertex identifier value/presence changed')
    if legacy:
        require(terminator is not None and terminator[0] == (0, 'SEQEND'), 'Missing SEQEND')
        require(one(terminator, 330) == handle and one(terminator, 8) == '0', 'Terminator owner/layer changed')
        require([tag for tag in terminator if tag[0] >= 1000] == [(1001, 'P2_RECORD'), (1000, 'terminator')], 'Terminator metadata changed')
        handles.append(one(terminator, 5))
    require(len({handle, *handles}) == 1 + len(handles) and '0' not in handles, 'Duplicate/zero child identity')
    return handle, owner, tuple(handles)


def untouched(parent, vertices, terminator, legacy):
    """Compare every unselected packet field, rather than normalizing identities."""
    marker = 'AcDb2dPolyline' if legacy else 'AcDbPolyline'
    parent = strip_proxy(parent); start, end = section(parent, marker)
    ignored = {30, 39, 70} if legacy else {38, 39, 70, 10, 20, 40, 41, 42, 91}
    stable_parent = parent[:start] + [tag for tag in parent[start:end] if tag[0] not in ignored] + parent[end:]
    stable_children = []
    if legacy:
        for row in vertices:
            a, b = section(row, 'AcDb2dVertex')
            stable_children.append(row[:a] + [tag for tag in row[a:b] if tag[0] not in VERTEX_CODES] + row[b:])
    return stable_parent, stable_children, terminator


def controls(parent, vertices, terminator, name, source, year, legacy):
    count = 0
    def check(p=parent, v=vertices, e=terminator):
        return check_subject(p, v, e, name, source, year, legacy)
    for code in (30 if legacy else 38, 39, 70):
        a, b = section(parent, 'AcDb2dPolyline' if legacy else 'AcDbPolyline')
        at = next(i for i in range(a, b) if parent[i][0] == code)
        damaged = list(parent); damaged[at] = (code, parent[at][1] + 1)
        count += reject(lambda: check(p=damaged))
    damaged = strip_proxy(parent)
    if not proxy(parent):
        at = section(damaged, 'AcDbEntity')[0]; damaged[at:at] = [(92 if year < 2013 else 160, 5), (310, PAYLOAD)]
    count += reject(lambda: check(p=damaged))
    for index in range(4):
        for code in (10, 20, 42, 40, 41):
            rows = [list(row) for row in vertices]; row = rows[index]
            at = next((i for i, tag in enumerate(row) if tag[0] == code), None)
            if at is None:
                at = section(row, 'AcDb2dVertex')[1] if legacy else len(row); row.insert(at, (code, 0.))
            else:
                pattern = struct.unpack('>Q', bits(row[at][1]))[0] ^ 1
                row[at] = (code, struct.unpack('>d', struct.pack('>Q', pattern))[0])
            count += reject(lambda: check(v=rows))
    if legacy:
        for code in (5, 330):
            at = next(i for i, tag in enumerate(terminator) if tag[0] == code)
            for mode in ('missing', 'duplicate', 'zero'):
                damaged = list(terminator)
                if mode == 'missing': damaged.pop(at)
                elif mode == 'duplicate': damaged.insert(at, damaged[at])
                else: damaged[at] = (code, '0')
                count += reject(lambda: check(e=damaged))
    count += reject(lambda: check(v=vertices[:-1]))
    count += reject(lambda: check(v=list(reversed(vertices))))
    return count


def inspect(path, year, binary, legacy, source):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_visibility_tags(path); at = tags.index((9, '$ACADVER')); require(tags[at + 1] == (1, PROFILES[year]), 'Wrong DXF version')
    rows = [tags[a:b] for a, b in records(tags)]
    selected = [(i, row) for i, row in enumerate(rows) if (1001, 'P2_EDITS') in row]
    require(len(selected) == len(NAMES), 'Wrong subject inventory')
    result, count, all_ids = {}, 0, []
    doc = ezdxf.readfile(path)
    for index, parent in selected:
        name = one(parent, 1000); require(name not in result, 'Duplicate subject')
        vertices = rows[index + 1:index + 5] if legacy else lw_vertices(parent)
        end = rows[index + 5] if legacy else None
        identity = check_subject(parent, vertices, end, name, source, year, legacy)
        result[name] = (identity, untouched(parent, vertices, end, legacy))
        all_ids.extend((identity[0], *identity[2])); count += controls(parent, vertices, end, name, source, year, legacy)
        points, ids, elevation, thickness, flags, cache, place = expected(name, source, year, legacy)
        entity = doc.entitydb[identity[0]]
        layout = doc.modelspace() if place == 0 else doc.layouts.get('P2_PAPER') if place == 1 else doc.blocks[f'P2_CONTAINER_{place}']
        require(entity.dxf.owner == layout.block_record_handle == identity[1] and entity.dxf.flags == flags, 'Independent placement/flags')
        scalar(float(entity.dxf.elevation.z if legacy else entity.dxf.elevation), elevation); scalar(float(entity.dxf.thickness), thickness)
        if legacy:
            require(tuple(v.dxf.handle for v in entity.vertices) + (entity.seqend.dxf.handle,) == identity[2], 'Independent sequence identities')
            actual = [(v.dxf.location.x, v.dxf.location.y, v.dxf.bulge, v.dxf.start_width, v.dxf.end_width) for v in entity.vertices]
            scalar(float(entity.dxf.default_start_width), 2.); scalar(float(entity.dxf.default_end_width), 3.)
        else:
            actual = [(x, y, bulge, start, finish) for x, y, start, finish, bulge in entity.get_points('xyseb')]
        for observed, wanted in zip(actual, points):
            for value, target in zip(observed, wanted): scalar(float(value), 0. if target is None else target)
        packet = Tags(DXFTag(code, value) for code, value in parent)
        require(load_proxy_graphic(packet, length_code=92 if year < 2013 else 160) == (PAYLOAD if cache == 2 else None), 'Independent proxy extraction')
    require(set(result) == NAMES and len(set(all_ids)) == len(all_ids), 'Global identity inventory')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (101., 102., 103.) and tuple(line.dxf.end) == (104., 105., 106.), 'Following LINE changed')
    require(not any(any(counts.values()) for counts in audit_signature(doc)), 'Independent audit errors/repairs')
    return result, count


def same_unselected(before, after):
    require(before == after, 'Identity, metadata, topology or an unselected packet changed')


def main(directory):
    names = {f'polyline2d-edits-AutoCad{year}-{transport}-{family}-{stage}.dxf' for year, transport, family, stage in
             itertools.product(PROFILES, ('text', 'binary'), ('legacy', 'lightweight'), ('source', 'output', 'resave'))}
    def inventory(actual): require(actual == names, 'Missing/extra edit drawings')
    inventory({p.name for p in directory.glob('polyline2d-edits-*.dxf')})
    count = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport, family in itertools.product(PROFILES, ('text', 'binary'), ('legacy', 'lightweight')):
        previous = None
        for stage in ('source', 'output', 'resave'):
            actual, rejected = inspect(directory/f'polyline2d-edits-AutoCad{year}-{transport}-{family}-{stage}.dxf', year,
                                       (transport == 'binary') != (stage == 'resave'), family == 'legacy', stage == 'source')
            if previous is not None:
                same_unselected(previous, actual)
                # A valid-looking but changed record identity cannot be normalized away.
                damaged = dict(actual); name = next(iter(damaged)); identity, packet = damaged[name]
                damaged[name] = (('FFFFFFFF', identity[1], identity[2]), packet)
                count += reject(lambda: same_unselected(previous, damaged))
            previous = actual; count += rejected
    print(f'PASS: {len(names)} drawings / {len(names)*len(NAMES)} 2D-polyline parents; '
          f'{count} corruption/inventory controls rejected; exact scalar bits/presence, identities, '
          'unselected metadata and zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_polyline2d_edits.py ARTIFACTS')
    main(Path(sys.argv[1]))
