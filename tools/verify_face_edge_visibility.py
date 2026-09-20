#!/usr/bin/env python3
"""Check physical 3DFACE visibility bits and absence of stale proxy packets."""
from __future__ import annotations
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, key, require, reject, audit_signature

VERSIONS = {k: v for k, v in PROFILES.items() if k not in ('AutoCad12', 'AutoCad13', 'AutoCad14')}
CORNERS = ((1., 2., 3.), (4., 5., 6.), (7., 8., 9.), (10., 11., 12.))


def check(tags, mask):
    starts = [i for i, t in enumerate(tags) if t == (0, '3DFACE')]
    require(len(starts) == 1, 'Expected one 3DFACE')
    start = starts[0]
    end = next(i for i in range(start + 1, len(tags)) if tags[i][0] == 0)
    face = tags[start:end]
    require([value for code, value in face if code == 70] == [mask], 'Visibility mask was changed, omitted or duplicated')
    require(not any(code in (92, 160, 310) for code, _ in face), 'Stale proxy packet retained')
    for i, point in enumerate(CORNERS):
        for j, coordinate in enumerate(point):
            code = 10 + i + 10*j
            require([key((code, float(v))) for c, v in face if c == code] == [key((code, coordinate))], 'Corner geometry changed')
    return start, end


def main(directory):
    specs = list(itertools.product(VERSIONS, (False, True), range(16)))
    names = {f'face-edge-visibility-{v}-{b}-{mask}.dxf' for v, b, mask in specs}
    def inventory(actual):
        require(actual == names, 'Missing or extra visibility drawings')
    inventory({p.name for p in directory.glob('face-edge-visibility-*.dxf')})
    reject(lambda: inventory(names - {next(iter(names))}))
    reject(lambda: inventory(names | {'face-edge-visibility-extra.dxf'}))
    controls = 0
    for version, binary, mask in specs:
        path = directory / f'face-edge-visibility-{version}-{binary}-{mask}.dxf'
        tags = load_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
        at = tags.index((9, '$ACADVER')); require(tags[at + 1] == (1, VERSIONS[version]), 'Version differs')
        a, b = check(tags, mask)
        at = next(i for i in range(a, b) if tags[i][0] == 70)
        changed = list(tags); changed[at] = (70, mask ^ 1)
        missing = tags[:at] + tags[at+1:]
        duplicate = tags[:at] + [tags[at]] + tags[at:]
        proxy = tags[:b] + [(92, 4), (310, bytes((1, 3, 7, 11)))] + tags[b:]
        for mutation in (changed, missing, duplicate, proxy):
            controls += reject(lambda: check(mutation, mask))
        doc = ezdxf.readfile(path)
        require(doc.dxfversion == VERSIONS[version], 'Independent version differs')
        signature = audit_signature(doc)
        require(not any(any(c.values()) for c in signature), 'Graph errors or repairs')
        faces = list(doc.modelspace().query('3DFACE')); require(len(faces) == 1, 'Independent face inventory differs')
        face = faces[0]; require(face.dxf.invisible_edges == mask, 'Independent flag decoding differs')
        require(face.proxy_graphic is None, 'Independent reader retained stale graphics')
        for i, point in enumerate(CORNERS):
            require(tuple(face.dxf.get(f'vtx{i}')) == point, 'Independent face geometry differs')
    print(f'PASS: {len(specs)} drawings, all 16 edge masks in six typed profiles and two transports; '
          f'{controls} actual-packet corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_face_edge_visibility.py ARTIFACTS')
    main(Path(sys.argv[1]))
