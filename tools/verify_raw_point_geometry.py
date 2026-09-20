#!/usr/bin/env python3
"""Verify complete raw POINT source/edit tag sequences and independent WCS geometry."""
from __future__ import annotations
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, key, require, reject, audit_signature

CODES = (10, 20, 30)
TARGET = (-8.5, 16.25, -32.)

def point_range(tags):
    first = next(i for i, t in enumerate(tags) if t == (0, 'POINT'))
    end = next(i for i in range(first + 1, len(tags)) if tags[i][0] == 0)
    return first, end

def check_pair(before, after):
    a, b = point_range(before); c, d = point_range(after)
    require(a == c, 'POINT record moved')
    require(list(map(key, before[:a])) == list(map(key, after[:c])), 'Prefix changed')
    require(list(map(key, before[b:])) == list(map(key, after[d:])), 'Suffix changed')
    source, result = before[a:b], after[c:d]
    expected = []
    had_z = any(code == 30 for code, _ in source)
    for code, value in source:
        expected.append((code, TARGET[CODES.index(code)] if code in CODES else value))
        if code == 20 and not had_z:
            expected.append((30, TARGET[2]))
    require(list(map(key, expected)) == list(map(key, result)), 'POINT packet differs from exact selected-slot edit')
    for code, value in zip(CODES, TARGET):
        values = [v for c, v in result if c == code]
        require(len(values) == 1 and struct.pack('>d', values[0]) == struct.pack('>d', value), 'POINT WCS coordinate differs')

def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), (False, True), range(4), (False, True)))
    stems = [f'raw-point-{v}-{bi}-{bl}-{var}-{out}' for v, bi, bl, var, out in specs]
    names = {stem + '-' + side + '.dxf' for stem in stems for side in ('before', 'after')}
    def inventory(actual): require(actual == names, 'Missing or extra raw POINT fixtures')
    inventory({p.name for p in directory.glob('raw-point-*.dxf')})
    reject(lambda: inventory(names - {next(iter(names))}))
    reject(lambda: inventory(names | {'raw-point-extra.dxf'}))
    corruptions = 0
    for stem, (version, _, _, variant, binary) in zip(stems, specs):
        paths = [directory / (stem + '-' + side + '.dxf') for side in ('before', 'after')]
        before, after = map(load_tags, paths)
        for path, tags in zip(paths, (before, after)):
            data = path.read_bytes()
            require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
            at = tags.index((9, '$ACADVER')); require(tags[at + 1] == (1, PROFILES[version]), 'Declared dialect differs')
            if binary: require((data[23] != 0) == (version == 'AutoCad12'), 'Legacy binary group-code framing differs')
        # Independent fixture input checks, in addition to exact before/after comparison.
        a, b = point_range(before); source = before[a:b]
        for code, value in ((10, 1.25), (20, -2.), (30, 3.)):
            values = [v for c, v in source if c == code]
            require(values == ([] if code == 30 and variant == 1 else [value]), 'Source position differs')
        check_pair(before, after)
        a, b = point_range(after)
        for i in range(a + 1, b):
            code, value = after[i]
            # All target non-framing tags are guarded, not just coordinates.
            if code == 100: continue
            for operation in ('change', 'missing', 'duplicate'):
                damaged = list(after)
                if operation == 'missing': del damaged[i]
                elif operation == 'duplicate': damaged.insert(i, damaged[i])
                else:
                    changed = value + .5 if isinstance(value, (float, int)) else str(value) + '_corrupt'
                    damaged[i] = (code, changed)
                corruptions += reject(lambda: check_pair(before, damaged))
        # Also corrupt the neighboring POINT and a retained private section string.
        for code, original in ((1, 'unchanged'), (10, 11.)):
            at = next(i for i in range(b, len(after)) if after[i] == (code, original))
            damaged = list(after); damaged[at] = (code, original + '_bad' if isinstance(original, str) else 12.)
            corruptions += reject(lambda: check_pair(before, damaged))
        documents = [ezdxf.readfile(path) for path in paths]
        signatures = [audit_signature(doc) for doc in documents]
        require(not any(any(counts.values()) for signature in signatures for counts in signature), 'POINT graph errors or repairs')
        point = documents[1].entitydb['A']
        require(point.dxftype() == 'POINT' and tuple(point.dxf.location) == TARGET, 'Independent POINT WCS position differs')
        require(point.dxf.thickness == (0 if variant == 1 else -2.5), 'POINT thickness differs')
        require(point.dxf.angle == (0 if variant == 1 else -35.5), 'POINT UCS angle differs')
        normal = (0., 0., 1.) if variant == 1 else (0., 0., -2.) if variant == 2 else (0., .6, .8)
        require(tuple(point.dxf.extrusion) == normal, 'POINT extrusion was changed or normalized')
    print(f'PASS: {len(stems)} complete source/edit pairs / {2 * len(stems)} drawings across nine raw profiles; '
          f'{corruptions} tag corruptions and two inventory controls rejected; zero graph errors or repairs.')

if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_raw_point_geometry.py ARTIFACTS')
    main(Path(sys.argv[1]))
