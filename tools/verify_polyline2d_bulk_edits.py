#!/usr/bin/env python3
"""Check actual legacy-polyline bulk edits at the retained packet limit."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_polyline2d_edits import (
    PROFILES, records, load_visibility_tags, one, bits, scalar,
    section, proxy, strip_proxy, PAYLOAD, require, reject, audit_signature)

POINTS = ((1., 2., .25, None, .5), (4., 6., -.5, .75, None),
          (-2., 7., 0., 1., 1.5), (11., -3., 0., None, None))
EDIT_CODES = {40, 41, 42}


def exact(row):
    """Retain float bits (including signed zero), not Python numeric equality."""
    return tuple((c, bits(v) if isinstance(v, float) else v) for c, v in row)


def geometry(row, marker):
    a, b = section(row, marker)
    return row[a:b]


def check_packets(parent, vertices, end, year, operation, source):
    require(parent[0] == (0, 'POLYLINE') and end[0] == (0, 'SEQEND'), 'Sequence framing')
    require(len(vertices) == 4, 'Vertex inventory')
    head = geometry(parent, 'AcDb2dPolyline')
    for code, value in ((10, 0.), (20, 0.), (30, 3.), (39, .5),
                        (210, 0.), (220, 0.), (230, 1.)):
        scalar(one(head, code), value)
    reverse = operation == 'reverse' and not source
    scalar(one(head, 40), 3. if reverse else 2.)
    scalar(one(head, 41), 2. if reverse else 3.)
    require(one(head, 70) == 0 and one(head, 75) == 0, 'Header flags')
    require(proxy(parent) == ([(92 if year < 2013 else 160, 5), (310, PAYLOAD)] if source else []),
            'Missing retained or stale parent proxy')
    handle, owner = one(parent, 5), one(parent, 330)
    require(handle != '0' and owner != '0' and one(parent, 8) == '0', 'Parent identity/layer')
    sequence = range(3, -1, -1) if reverse else range(4)
    for row, old in zip(vertices, sequence):
        require(row[0] == (0, 'VERTEX'), 'Expected VERTEX')
        point, segment = POINTS[old], POINTS[(old + 3) % 4]
        geom = geometry(row, 'AcDb2dVertex')
        for code, value in ((10, point[0]), (20, point[1]), (30, 0.)):
            scalar(one(geom, code), value)
        bulge = -segment[2] if reverse else point[2]
        widths = (point[3], point[4]) if source else (segment[4], segment[3]) if reverse else (5., 5.)
        scalar(one(geom, 42), bulge)
        scalar(one(geom, 40, True), widths[0]); scalar(one(geom, 41, True), widths[1])
        require(one(geom, 70) == 0 and one(geom, 91) == 101 + old, 'Vertex flags/identifier')
        require(one(row, 330) == (handle if old % 2 == 0 else owner) and one(row, 8) == '0',
                'Stored source owner spelling/layer changed')
        data = [tag for tag in row if tag[0] >= 1000]
        expected = [(1001, 'P2_RECORD'), (1000, 'vertex-' + str(old))]
        if old == 3: expected += [(1000, 'budget')] * 4080
        require(data == expected, 'Vertex metadata/order/count changed')
        if old == 3:
            # The library budget excludes the group-0 record delimiter.
            require(len(row) - 1 == (4094 if source else 4096), 'Boundary packet size')
    require(one(end, 330) == handle and one(end, 8) == '0', 'Terminator owner/layer')
    require([t for t in end if t[0] >= 1000] == [(1001, 'P2_RECORD'), (1000, 'terminator')], 'Terminator metadata')
    ids = [one(parent, 5), *(one(r, 5) for r in vertices), one(end, 5)]
    require(len(set(ids)) == 6 and '0' not in ids, 'Sequence identity')


def conserved_vertex(row):
    a, b = section(row, 'AcDb2dVertex')
    return exact(row[:a] + [t for t in row[a:b] if t[0] not in EDIT_CODES] + row[b:])


def conserved_parent(row):
    row = strip_proxy(row)
    a, b = section(row, 'AcDb2dPolyline')
    return exact(row[:a] + [t for t in row[a:b] if t[0] not in (40, 41)] + row[b:])


def transition(previous, current, first, operation):
    before, old_vertices, old_end, old_line = previous
    after, new_vertices, new_end, new_line = current
    require(conserved_parent(before) == conserved_parent(after), 'Unselected parent fields changed')
    require(exact(old_end) == exact(new_end) and exact(old_line) == exact(new_line), 'Terminator/following LINE changed')
    wanted = list(reversed(old_vertices)) if first and operation == 'reverse' else old_vertices
    require([one(v, 5) for v in wanted] == [one(v, 5) for v in new_vertices], 'Retained vertex order/identity changed')
    for old, new in zip(wanted, new_vertices):
        require(conserved_vertex(old) == conserved_vertex(new), 'Unselected child fields changed')
    if not first:
        require(exact(before) == exact(after) and all(exact(a) == exact(b) for a, b in zip(old_vertices, new_vertices)),
                'Read-only resave changed packets')


def corruptions(parent, vertices, end, year, operation, source):
    count = 0
    for code in (40, 41):
        index = next(i for i, t in enumerate(parent) if t[0] == code)
        for replacement in (None, (code, 0.)):
            bad = list(parent)
            if replacement is None: bad.pop(index)
            else: bad[index] = replacement
            count += reject(lambda: check_packets(bad, vertices, end, year, operation, source))
    bad = strip_proxy(parent)
    if not source:
        pos = bad.index((100, 'AcDbEntity')) + 1
        bad[pos:pos] = [(92 if year < 2013 else 160, 5), (310, PAYLOAD)]
    count += reject(lambda: check_packets(bad, vertices, end, year, operation, source))
    for i, row in enumerate(vertices):
        for code in (5, 330, 10, 20, 30, 40, 41, 42, 91):
            bad = [list(v) for v in vertices]
            positions = [at for at, tag in enumerate(row) if tag[0] == code]
            if positions:
                at = positions[0]
                bad[i][at] = (code, '0' if code in (5, 330) else -1 if code == 91 else 99.)
            else:
                at = next(at for at, tag in enumerate(row) if tag[0] == 1001)
                bad[i].insert(at, (code, 0.))
            count += reject(lambda: check_packets(parent, bad, end, year, operation, source))
        bad = [list(v) for v in vertices]; bad[i][-1] = (1000, 'changed')
        count += reject(lambda: check_packets(parent, bad, end, year, operation, source))
    count += reject(lambda: check_packets(parent, list(reversed(vertices)), end, year, operation, source))
    count += reject(lambda: check_packets(parent, vertices[:-1], end, year, operation, source))
    return count


def inspect(path, year, transport, operation, stage):
    binary = (transport == 'binary') != (stage == 'resave')
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load_visibility_tags(path)
    at = tags.index((9, '$ACADVER')); require(tags[at + 1] == (1, PROFILES[year]), 'Version')
    rows = [tags[a:b] for a, b in records(tags)]
    positions = [i for i, row in enumerate(rows) if row[0] == (0, 'POLYLINE')]
    require(len(positions) == 1, 'Polyline inventory')
    start = positions[0]; parent = rows[start]; vertices = rows[start+1:start+5]; end = rows[start+5]
    line, = [row for row in rows if row[0] == (0, 'LINE')]
    for code, value in ((10, 101.), (20, 102.), (30, 103.), (11, 104.), (21, 105.), (31, 106.)):
        scalar(one(line, code), value)
    check_packets(parent, vertices, end, year, operation, stage == 'source')
    count = corruptions(parent, vertices, end, year, operation, stage == 'source')
    doc = ezdxf.readfile(path)
    entity, = doc.modelspace().query('POLYLINE')
    require(entity.dxf.handle == one(parent, 5) and entity.seqend.dxf.handle == one(end, 5), 'Independent parent/end identity')
    require([v.dxf.handle for v in entity.vertices] == [one(v, 5) for v in vertices], 'Independent vertex identities')
    for v, row in zip(entity.vertices, vertices):
        for actual, code in zip(tuple(v.dxf.location), (10, 20, 30)):
            scalar(actual, one(row, code))
        scalar(v.dxf.bulge, one(row, 42))
    require(not any(any(c.values()) for c in audit_signature(doc)), 'Independent graph errors/repairs')
    return (parent, vertices, end, line), count


def main(directory):
    expected = {f'polyline2d-bulk-AutoCad{y}-{t}-{o}-{s}.dxf' for y, t, o, s in
                itertools.product(PROFILES, ('text', 'binary'), ('width', 'reverse'), ('source', 'output', 'resave'))}
    def inventory(actual): require(actual == expected, 'Missing/extra bulk drawings')
    inventory({p.name for p in directory.glob('polyline2d-bulk-*.dxf')})
    count = reject(lambda: inventory(expected - {next(iter(expected))})) + reject(lambda: inventory(expected | {'extra.dxf'}))
    for year, transport, operation in itertools.product(PROFILES, ('text', 'binary'), ('width', 'reverse')):
        previous = None
        for stage in ('source', 'output', 'resave'):
            current, controls = inspect(directory/f'polyline2d-bulk-AutoCad{year}-{transport}-{operation}-{stage}.dxf',
                                        year, transport, operation, stage)
            if previous is not None:
                transition(previous, current, stage == 'output', operation)
                bad = list(current); bad[2] = list(current[2])
                at = next(i for i, t in enumerate(bad[2]) if t[0] == 5); bad[2][at] = (5, 'FFFF')
                count += reject(lambda: transition(previous, bad, stage == 'output', operation))
            previous = current; count += controls
    print(f'PASS: {len(expected)} bulk drawings / {len(expected)*4} VERTEX records, exact boundary packets, inherited widths, '
          f'signed bulges and stable identities; {count} corruptions/inventory controls rejected; zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_polyline2d_bulk_edits.py ARTIFACTS')
    main(Path(sys.argv[1]))
