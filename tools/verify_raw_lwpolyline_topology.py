#!/usr/bin/env python3
"""Check raw LWPOLYLINE insertion/removal against complete expected tag sequences."""
from __future__ import annotations
import io
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from verify_raw_line_geometry import PROFILES, key, require, reject, audit_signature

VERSIONS = {k: v for k, v in PROFILES.items() if k not in ('AutoCad12', 'AutoCad13')}
VERTEX_FIELDS = (10, 20, 40, 41, 42, 91)
INSERTED = [(10, -8.5), (20, 16.25), (91, -200), (40, 1.25), (41, .5), (42, .25)]


def load_tags(path):
    data = path.read_bytes()
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else \
        ascii_tags_loader(io.StringIO(data.decode('cp1252'), newline=None))
    tags = []
    for tag in loader:
        value = tag.value
        if 310 <= tag.code <= 319 or tag.code == 1004:
            if isinstance(value, str):
                require(len(value) % 2 == 0 and all(c in '0123456789abcdefABCDEF' for c in value),
                        'Malformed binary chunk')
                value = bytes.fromhex(value)
            require(isinstance(value, bytes), 'Binary chunk type differs')
        else:
            value = cast_tag_value(tag.code, value)
        tags.append((tag.code, value))
    return tags


def selected(tags):
    starts = [i for i, t in enumerate(tags) if t == (0, 'LWPOLYLINE')]
    require(len(starts) == 1, 'Expected one target polyline')
    start = starts[0]
    end = next(i for i in range(start + 1, len(tags)) if tags[i][0] == 0)
    return start, end


def vertex_packets(record):
    starts = [i for i, (c, _) in enumerate(record) if c == 10]
    # Matrix source has the extrusion after all vertex packets.
    end = next(i for i, (c, _) in enumerate(record) if c == 210)
    return [record[a:b] for a, b in zip(starts, starts[1:] + [end])], starts, end


def check(before, after, operation):
    a, b = selected(before)
    c, d = selected(after)
    require(a == c and list(map(key, before[:a])) == list(map(key, after[:c])), 'Prefix changed')
    require(list(map(key, before[b:])) == list(map(key, after[d:])), 'Suffix changed')
    source, result = before[a:b], after[c:d]
    packets, starts, vertex_end = vertex_packets(source)
    originals = [[(10, float(1 + i * 3)), (20, float(2 + i * 3)), (91, -7 if i == 1 else 100 + i)]
                 for i in range(3)]
    require(packets == originals, 'Regenerated source vertices differ')
    require([v for code, v in source if code == 90] == [3], 'Source count differs')
    insert = operation < 3
    index = (0, 1, 3)[operation] if insert else operation - 3
    new_packets = list(originals)
    if insert:
        new_packets.insert(index, INSERTED)
    else:
        del new_packets[index]
    expected = source[:starts[0]] + [tag for packet in new_packets for tag in packet] + source[vertex_end:]
    expected = [(code, 4 if insert else 2) if code == 90 else (code, value) for code, value in expected]
    require(list(map(key, result)) == list(map(key, expected)), 'Unexpected topology or unselected record change')
    return c, d


def points(operation=None):
    result = [(1., 2., 0., 0., 0.), (4., 5., 0., 0., 0.), (7., 8., 0., 0., 0.)]
    if operation is not None:
        if operation < 3:
            result.insert((0, 1, 3)[operation], (-8.5, 16.25, 1.25, .5, .25))
        else:
            del result[operation - 3]
    return result


def main(directory):
    specs = list(itertools.product(VERSIONS, (False, True), (False, True), range(6), (False, True)))
    stems = [f'raw-lw-topology-{v}-{bi}-{bl}-{op}-{out}' for v, bi, bl, op, out in specs]
    names = {stem + '-' + side + '.dxf' for stem in stems for side in ('before', 'after')}
    def inventory(actual):
        require(actual == names, 'Missing or extra topology fixtures')
    inventory({p.name for p in directory.glob('raw-lw-topology-*.dxf')})
    reject(lambda: inventory(names - {next(iter(names))}))
    reject(lambda: inventory(names | {'raw-lw-topology-extra.dxf'}))
    controls = 0
    for stem, (version, _, _, operation, binary) in zip(stems, specs):
        paths = [directory / (stem + '-' + side + '.dxf') for side in ('before', 'after')]
        before, after = map(load_tags, paths)
        for path, tags in zip(paths, (before, after)):
            data = path.read_bytes()
            require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
            at = tags.index((9, '$ACADVER'))
            require(tags[at + 1] == (1, VERSIONS[version]), 'Declared profile changed')
            if binary:
                require(data[22:24] == b'\0\0', 'Binary group-code framing changed')
        a, b = check(before, after, operation)
        for at in range(a + 1, b):
            code, value = after[at]
            for action in ('changed', 'missing', 'duplicate'):
                corrupted = list(after)
                if action == 'changed':
                    corrupted[at] = (code, value + .125 if isinstance(value, (int, float)) else str(value) + '_bad')
                elif action == 'missing':
                    del corrupted[at]
                else:
                    corrupted.insert(at, corrupted[at])
                controls += reject(lambda: check(before, corrupted, operation))
        for at in (next(i for i in range(b, len(after)) if after[i] == (1, 'unchanged')),
                   next(i for i in range(b, len(after)) if after[i][0] == 310)):
            corrupted = list(after)
            code, value = after[at]
            corrupted[at] = (code, value + (b'X' if isinstance(value, bytes) else '_bad'))
            controls += reject(lambda: check(before, corrupted, operation))
        for edited, path in enumerate(paths):
            doc = ezdxf.readfile(path)
            require(not any(any(c.values()) for c in audit_signature(doc)), 'Graph errors or repairs')
            entity = doc.entitydb['A']
            require(entity.dxftype() == 'LWPOLYLINE', 'Target entity identity changed')
            expected = points(operation if edited else None)
            require([tuple(p) for p in entity.get_points('xyseb')] == expected, 'Independent vertex topology or attributes differ')
            require(entity.dxf.count == len(expected) and entity.dxf.flags == 129, 'Count or closure changed')
            require(entity.dxf.elevation == 5 and entity.dxf.thickness == -2, 'Plane scalars changed')
            require(tuple(entity.dxf.extrusion) == (0., 0., -1.), 'Extrusion changed')
            require(entity.dxf.get('const_width', 0) == 0, 'Constant width changed')
    print(f'PASS: {len(specs)} complete source/edit pairs / {len(specs)*2} drawings; '
          f'{controls} actual-tag corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_raw_lwpolyline_topology.py ARTIFACTS')
    main(Path(sys.argv[1]))
