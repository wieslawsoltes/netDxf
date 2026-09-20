#!/usr/bin/env python3
"""Independently check complete before/after raw RAY/XLINE records and WCS geometry."""
from __future__ import annotations
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, key, require, reject, audit_signature

VERSIONS = {name: code for name, code in PROFILES.items() if name != 'AutoCad12'}
CODES = (10, 20, 30, 11, 21, 31)
ORIGIN = (-8.5, 16.25, -32.)


def selected_range(tags, xline):
    kind = 'XLINE' if xline else 'RAY'
    starts = [i for i, tag in enumerate(tags) if tag == (0, kind)]
    require(len(starts) == 1, 'Expected exactly one selected infinite-line record')
    start = starts[0]
    end = next(i for i in range(start + 1, len(tags)) if tags[i][0] == 0)
    return start, end


def original(variant):
    origin = (1.25, -2., 0. if variant == 1 else 3.)
    direction = (0., 0., -1.) if variant == 2 else (.6, .8, 0.)
    return origin, direction


def target(xline):
    return (-.6, 0., -.8) if xline else (.6, 0., .8)


def check_pair(before, after, xline, variant):
    a, b = selected_range(before, xline)
    c, d = selected_range(after, xline)
    require(a == c, 'Infinite-line record moved')
    require(list(map(key, before[:a])) == list(map(key, after[:c])), 'Prefix changed')
    require(list(map(key, before[b:])) == list(map(key, after[d:])), 'Suffix changed')
    source, result = before[a:b], after[c:d]
    stored = dict(zip(CODES, sum(original(variant), ())))
    for code, value in stored.items():
        expected = [] if variant == 1 and code in (30, 31) else [key((code, value))]
        require([key(t) for t in source if t[0] == code] == expected, 'Regenerated source geometry differs')
    subclasses = [] if variant == 3 else ['AcDbEntity', 'AcDbXline' if xline else 'AcDbRay']
    require([v for c, v in source if c == 100] == subclasses, 'Source subclass layout changed')
    values = dict(zip(CODES, ORIGIN + target(xline)))
    present = {code for code, _ in source}
    expected = []
    for code, value in source:
        expected.append((code, values[code] if code in values else value))
        if code in (20, 21) and code + 10 not in present:
            expected.append((code + 10, values[code + 10]))
    require(list(map(key, result)) == list(map(key, expected)), 'Selected packet does not match exact expected edit')
    return c, d


def main(directory):
    specs = list(itertools.product(VERSIONS, (False, True), (False, True), (False, True), range(4), (False, True)))
    stems = [f'raw-infinite-{v}-{x}-{bi}-{bl}-{var}-{out}' for v, x, bi, bl, var, out in specs]
    names = {stem + '-' + side + '.dxf' for stem in stems for side in ('before', 'after')}
    def inventory(actual):
        require(actual == names, 'Missing or extra infinite-line fixtures')
    inventory({p.name for p in directory.glob('raw-infinite-*.dxf')})
    reject(lambda: inventory(names - {next(iter(names))}))
    reject(lambda: inventory(names | {'raw-infinite-extra.dxf'}))
    corruptions = 0
    for stem, (version, xline, _, _, variant, binary) in zip(stems, specs):
        paths = [directory / (stem + '-' + side + '.dxf') for side in ('before', 'after')]
        before, after = map(load_tags, paths)
        for path, tags in zip(paths, (before, after)):
            data = path.read_bytes()
            require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
            at = tags.index((9, '$ACADVER'))
            require(tags[at + 1] == (1, VERSIONS[version]), 'Declared profile changed')
            if binary:
                require(data[22:24] == b'\0\0', 'Modern two-byte group-code framing changed')
        a, b = check_pair(before, after, xline, variant)
        for at in range(a + 1, b):
            code, value = after[at]
            for action in ('change', 'missing', 'repeat'):
                damaged = list(after)
                if action == 'change':
                    damaged[at] = (code, value + .125 if isinstance(value, (int, float)) else str(value) + '_corrupted')
                elif action == 'missing':
                    del damaged[at]
                else:
                    damaged.insert(at, damaged[at])
                corruptions += reject(lambda: check_pair(before, damaged, xline, variant))
        for code, value in ((1, 'unchanged'), (10, 11.)):
            at = next(i for i in range(b, len(after)) if after[i] == (code, value))
            damaged = list(after)
            damaged[at] = (code, 'corrupted' if isinstance(value, str) else 12.)
            corruptions += reject(lambda: check_pair(before, damaged, xline, variant))
        docs = [ezdxf.readfile(path) for path in paths]
        for doc, (origin, direction) in zip(docs, (original(variant), (ORIGIN, target(xline)))):
            require(not any(any(c.values()) for c in audit_signature(doc)), 'Independent graph errors or repairs')
            entity = doc.entitydb['A']
            require(entity.dxftype() == ('XLINE' if xline else 'RAY'), 'Independent target identity changed')
            require(tuple(entity.dxf.start) == origin, 'Independent WCS origin differs')
            require(tuple(entity.dxf.unit_vector) == direction, 'Independent direction orientation or magnitude differs')
    print(f'PASS: {len(specs)} complete source/edit pairs / {len(specs)*2} drawings across eight raw profiles; '
          f'{corruptions} actual-tag corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_raw_infinite_geometry.py ARTIFACT_DIRECTORY')
    main(Path(sys.argv[1]))
