#!/usr/bin/env python3
"""Check typed SOLID/TRACE edits and absence of stale common proxy graphics."""
from __future__ import annotations
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, key, require, reject, audit_signature

VERSIONS = {k: v for k, v in PROFILES.items() if k not in ('AutoCad12', 'AutoCad13', 'AutoCad14')}
BASE = ((1., 2., 3.), (5., 2., 3.), (1., 6., 3.), (5., 6., 3.))


def expected_geometry(index):
    points = list(BASE)
    thickness = -2.5
    if index < 4:
        points[index] = (-8., 9., 3.)
    elif index == 4:
        points = [(x, y, -9.) for x, y, _ in points]
    else:
        thickness = -9.
    return points, thickness


def check(tags, trace, index):
    kind = 'TRACE' if trace else 'SOLID'
    starts = [i for i, t in enumerate(tags) if t == (0, kind)]
    require(len(starts) == 1, 'Expected exactly one selected quad')
    first = starts[0]
    end = next(i for i in range(first + 1, len(tags)) if tags[i][0] == 0)
    record = tags[first:end]
    require(not any(code in (92, 160, 310) for code, _ in record), 'Stale common proxy packet retained')
    points, thickness = expected_geometry(index)
    expected = {39: thickness, 210: 0., 220: .6, 230: .8}
    for i, point in enumerate(points):
        for axis, value in enumerate(point):
            expected[10 + i + 10 * axis] = value
    for code, value in expected.items():
        actual = [key((code, float(v))) for c, v in record if c == code]
        require(actual == [key((code, value))], f'Quad component {code} changed, omitted or repeated')
    return first, end


def main(directory):
    specs = list(itertools.product(VERSIONS, (False, True), (False, True), range(6)))
    names = {f'quad-mutation-{v}-{b}-{t}-{i}.dxf' for v, b, t, i in specs}
    def inventory(actual):
        require(actual == names, 'Missing or extra quad mutation fixture')
    inventory({p.name for p in directory.glob('quad-mutation-*.dxf')})
    reject(lambda: inventory(names - {next(iter(names))}))
    reject(lambda: inventory(names | {'quad-mutation-extra.dxf'}))
    corruptions = 0
    for version, binary, trace, index in specs:
        path = directory / f'quad-mutation-{version}-{binary}-{trace}-{index}.dxf'
        data = path.read_bytes()
        tags = load_tags(path)
        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
        at = tags.index((9, '$ACADVER'))
        require(tags[at + 1] == (1, VERSIONS[version]), 'Declared profile changed')
        a, b = check(tags, trace, index)
        for n in range(a + 1, b):
            code, value = tags[n]
            if not (10 <= code <= 13 or 20 <= code <= 23 or 30 <= code <= 33 or code in (39, 210, 220, 230)):
                continue
            for op in ('change', 'remove', 'repeat'):
                bad = list(tags)
                if op == 'change':
                    bad[n] = (code, value + .125)
                elif op == 'remove':
                    del bad[n]
                else:
                    bad.insert(n, bad[n])
                corruptions += reject(lambda: check(bad, trace, index))
        bad = tags[:b] + [(92, 4), (310, bytes((1, 3, 7, 11)))] + tags[b:]
        corruptions += reject(lambda: check(bad, trace, index))
        doc = ezdxf.readfile(path)
        require(doc.dxfversion == VERSIONS[version], 'Independent version differs')
        require(not any(any(c.values()) for c in audit_signature(doc)), 'Independent graph errors or repairs')
        entities = list(doc.modelspace().query('TRACE' if trace else 'SOLID'))
        require(len(entities) == 1, 'Independent entity inventory differs')
        entity = entities[0]
        points, thickness = expected_geometry(index)
        for i, point in enumerate(points):
            require(tuple(entity.dxf.get(f'vtx{i}')) == tuple(point), 'Independent OCS corner differs')
        require(entity.dxf.thickness == thickness, 'Independent thickness differs')
        require(tuple(entity.dxf.extrusion) == (0., .6, .8), 'Independent extrusion differs')
        require(entity.proxy_graphic is None, 'Independent stale proxy')
    print(f'PASS: {len(specs)} SOLID/TRACE drawings across six typed profiles and two transports; '
          f'{corruptions} actual-packet corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_quad_mutation.py ARTIFACTS')
    main(Path(sys.argv[1]))
