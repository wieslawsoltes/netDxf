#!/usr/bin/env python3
"""Verify all retained tags and independently decoded 3DFACE corners/visibility."""
from __future__ import annotations
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, key, require, reject, audit_signature

CODES = (10, 20, 30, 11, 21, 31, 12, 22, 32, 13, 23, 33)
TARGETS = ((-8.5, 16.25, -32.), (64., -128.5, 256.25), (7., 9., -11.), (-17., 19., 23.))
VALUES = dict(zip(CODES, (v for p in TARGETS for v in p)))
FLAGS = 9


def face_range(tags):
    matches = [i for i, t in enumerate(tags) if t == (0, '3DFACE')]
    require(len(matches) == 1, 'Expected one physical target 3DFACE')
    first = matches[0]
    last = next(i for i in range(first + 1, len(tags)) if tags[i][0] == 0)
    return first, last


def original_points(variant):
    points = [(1., 2., 3.), (4., 5., 6.), (7., 8., 9.), (10., 11., 12.)]
    if variant == 1:
        points = [(x, y, 0.) for x, y, _ in points]
    if variant == 2:
        points[-1] = points[-2]
    return points


def check_pair(before, after, variant):
    a, b = face_range(before)
    c, d = face_range(after)
    require(a == c, '3DFACE moved')
    require(list(map(key, before[:a])) == list(map(key, after[:c])), 'Prefix changed')
    require(list(map(key, before[b:])) == list(map(key, after[d:])), 'Suffix changed')
    source, result = before[a:b], after[c:d]
    source_codes = [c for c, _ in source]
    for i, point in enumerate(original_points(variant)):
        for axis, coordinate in enumerate(point):
            code = 10 + i + axis * 10
            expected = [] if variant == 1 and axis == 2 else [coordinate]
            require([v for c, v in source if c == code] == expected, 'Regenerated source corner differs')
    original_flags = [] if variant == 1 else [10 if variant == 2 else 5]
    require([v for c, v in source if c == 70] == original_flags, 'Regenerated source visibility differs')
    expected = []
    last_coordinate = max(i for i, (c, _) in enumerate(source) if c in CODES)
    for i, (code, value) in enumerate(source):
        expected.append((code, VALUES[code] if code in CODES else FLAGS if code == 70 else value))
        if 20 <= code <= 23 and code + 10 not in source_codes:
            expected.append((code + 10, VALUES[code + 10]))
        if i == last_coordinate and 70 not in source_codes:
            expected.append((70, FLAGS))
    require(list(map(key, result)) == list(map(key, expected)), 'Selected 3DFACE edit differs from exact expected packet')
    for code in CODES:
        require(len([v for c, v in result if c == code]) == 1, 'Missing or repeated corner component')


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), (False, True), range(4), (False, True)))
    stems = [f'raw-face3d-{v}-{bi}-{bl}-{var}-{out}' for v, bi, bl, var, out in specs]
    names = {stem + '-' + side + '.dxf' for stem in stems for side in ('before', 'after')}
    def inventory(actual):
        require(actual == names, 'Missing or extra 3DFACE fixtures')
    inventory({p.name for p in directory.glob('raw-face3d-*.dxf')})
    reject(lambda: inventory(names - {next(iter(names))}))
    reject(lambda: inventory(names | {'raw-face3d-extra.dxf'}))
    corruptions = 0
    for stem, (version, _, _, variant, binary) in zip(stems, specs):
        paths = [directory / (stem + '-' + side + '.dxf') for side in ('before', 'after')]
        before, after = map(load_tags, paths)
        for path, tags in zip(paths, (before, after)):
            data = path.read_bytes()
            require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
            at = tags.index((9, '$ACADVER'))
            require(tags[at + 1] == (1, PROFILES[version]), 'Declared DXF profile changed')
            if binary:
                require((data[23] != 0) == (version == 'AutoCad12'), 'Historical binary group-code framing changed')
        check_pair(before, after, variant)
        a, b = face_range(after)
        for i in range(a + 1, b):
            code, value = after[i]
            for action in ('change', 'remove', 'repeat'):
                bad = list(after)
                if action == 'remove':
                    del bad[i]
                elif action == 'repeat':
                    bad.insert(i, bad[i])
                else:
                    bad[i] = (code, value + .5 if isinstance(value, (int, float)) else str(value) + '_corrupt')
                corruptions += reject(lambda: check_pair(before, bad, variant))
        for code, value in ((1, 'unchanged'), (10, 11.)):
            i = next(i for i in range(b, len(after)) if after[i] == (code, value))
            bad = list(after); bad[i] = (code, 'corrupt' if isinstance(value, str) else 12.)
            corruptions += reject(lambda: check_pair(before, bad, variant))
        docs = [ezdxf.readfile(p) for p in paths]
        for doc, points, flags in zip(docs, (original_points(variant), TARGETS), (0 if variant == 1 else 10 if variant == 2 else 5, FLAGS)):
            signature = audit_signature(doc)
            require(not any(any(counts.values()) for counts in signature), '3DFACE graph errors or repairs')
            face = doc.entitydb['A']
            require(face.dxftype() == '3DFACE', 'Independent identity resolved to a different entity')
            for i, expected in enumerate(points):
                require(tuple(face.dxf.get(f'vtx{i}')) == expected, 'Independent WCS vertex differs')
            require(face.dxf.invisible_edges == flags, 'Independent invisible-edge flags differ')
    print(f'PASS: {len(stems)} complete source/edit pairs / {len(stems)*2} drawings across nine raw families; '
          f'{corruptions} actual-tag corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_raw_face3d_geometry.py ARTIFACTS')
    main(Path(sys.argv[1]))
