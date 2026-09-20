#!/usr/bin/env python3
"""Independently check same-dialect raw CIRCLE/ARC edits and complete retained tags."""
from __future__ import annotations
import collections
import io
import itertools
import math
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

PROFILES = {'AutoCad12':'AC1009', 'AutoCad13':'AC1012', 'AutoCad14':'AC1014',
            'AutoCad2000':'AC1015', 'AutoCad2004':'AC1018', 'AutoCad2007':'AC1021',
            'AutoCad2010':'AC1024', 'AutoCad2013':'AC1027', 'AutoCad2018':'AC1032'}
FIELDS = (10, 20, 30, 40, 50, 51)
CENTER = (-8.5, 16.25, -32.0)
VALUES = CENTER + (3.75, 350.0, 35.0)


def require(ok, message):
    if not ok:
        raise ValueError(message)


def load_tags(path):
    data = path.read_bytes()
    tags = (binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else
            ascii_tags_loader(io.StringIO(data.decode('cp1252'), newline=None)))
    return [(tag.code, cast_tag_value(tag.code, tag.value)) for tag in tags]


def key(tag):
    code, value = tag
    return code, struct.pack('>d', value) if isinstance(value, float) else value


def selected(tags, arc):
    start = tags.index((0, 'ARC' if arc else 'CIRCLE'))
    end = next(i for i in range(start + 1, len(tags)) if tags[i][0] == 0)
    return start, end


def one(record, code, default=None):
    values = [v for c, v in record if c == code]
    require(len(values) == 1 or (not values and default is not None), f'Missing/duplicate group {code}')
    return values[0] if values else default


def check_pair(before, after, arc, omit):
    first, last = selected(before, arc)
    a, b = selected(after, arc)
    require(a == first, 'Conic record moved')
    require(list(map(key, before[:first])) == list(map(key, after[:a])), 'Prefix changed')
    require(list(map(key, before[last:])) == list(map(key, after[b:])), 'Suffix changed')
    source, target = before[first:last], after[a:b]
    active = FIELDS if arc else FIELDS[:4]
    # Inputs are independently specified, not accepted solely because the two outputs agree.
    initial = (1.25, -2.0, 0.0 if omit else 3.0, 7.5, 15.0, 270.0)
    for code, value in zip(active, initial):
        require(one(source, code, 0.0 if code == 30 else None) == value, 'Source geometry changed')
    require(tuple(one(source, c) for c in (210, 220, 230)) ==
            ((0.0, .6, .8) if omit else (0.0, 0.0, -1.0)), 'Source extrusion differs')
    require([key(t) for t in source if t[0] not in active] ==
            [key(t) for t in target if t[0] not in active], 'Unselected tags changed')
    for code, value in zip(active, VALUES):
        require(struct.pack('>d', float(one(target, code))) == struct.pack('>d', value), 'Edited geometry differs')
    expected_order = [c for c, _ in source]
    if omit:
        expected_order.insert(expected_order.index(20) + 1, 30)
    require([c for c, _ in target] == expected_order, 'Tag order/insertion changed')


def rejected(action):
    try:
        action()
    except ValueError:
        return 1
    raise AssertionError('Actual corruption escaped the positive validator')


def diagnostics(doc):
    audit = doc.audit()
    return collections.Counter(x.code for x in audit.errors), collections.Counter(x.code for x in audit.fixes)


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), (False, True),
                                   (False, True), (False, True), (False, True)))
    def stem(spec):
        return 'raw-conic-' + '-'.join(map(str, spec))
    expected = {stem(s) + '-' + side + '.dxf' for s in specs for side in ('before', 'after')}
    def inventory(actual):
        require(actual == expected, 'Missing/extra conic fixtures')
    inventory({p.name for p in directory.glob('raw-conic-*.dxf')})
    rejected(lambda: inventory(expected - {min(expected)}))
    rejected(lambda: inventory(expected | {'raw-conic-extra.dxf'}))
    corruptions = inherited_errors = inherited_repairs = 0
    for spec in specs:
        version, arc, _, _, omit, binary = spec
        paths = [directory / (stem(spec) + '-' + side + '.dxf') for side in ('before', 'after')]
        before, after = map(load_tags, paths)
        for path, tags in zip(paths, (before, after)):
            data = path.read_bytes()
            require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
            at = tags.index((9, '$ACADVER'))
            require(tags[at + 1] == (1, PROFILES[version]), 'Physical DXF version changed')
            if binary:
                require((data[23] != 0) == (version == 'AutoCad12'), 'Binary group-code framing differs')
        check_pair(before, after, arc, omit)
        first, last = selected(after, arc)
        for code in (FIELDS if arc else FIELDS[:4]):
            at = next(i for i in range(first, last) if after[i][0] == code)
            for operation in ('change', 'remove', 'duplicate'):
                damaged = list(after)
                if operation == 'change': damaged[at] = (code, float(damaged[at][1]) + .5)
                elif operation == 'remove': del damaged[at]
                else: damaged.insert(at, damaged[at])
                corruptions += rejected(lambda: check_pair(before, damaged, arc, omit))
        # Test preservation checks inside the conic and in unrelated records/sections.
        for code, value in ((8, 'OTHER'), (39, 9.0), (230, .25), (1000, 'CHANGED')):
            at = next(i for i in range(first, last) if after[i][0] == code)
            damaged = list(after); damaged[at] = code, value
            corruptions += rejected(lambda: check_pair(before, damaged, arc, omit))
        damaged = list(after); at = after.index((1, 'unchanged')); damaged[at] = 1, 'CHANGED'
        corruptions += rejected(lambda: check_pair(before, damaged, arc, omit))
        documents = [ezdxf.readfile(path) for path in paths]
        previous, current = map(diagnostics, documents)
        require(previous == current, 'Edit added graph diagnostic codes or counts')
        inherited_errors += sum(previous[0].values()); inherited_repairs += sum(previous[1].values())
        conic = documents[1].entitydb['A']
        require(conic.dxftype() == ('ARC' if arc else 'CIRCLE'), 'Target identity/type changed')
        require(tuple(conic.dxf.center) == CENTER and conic.dxf.radius == 3.75, 'Independent conic geometry differs')
        if arc:
            require(conic.dxf.start_angle == 350.0 and conic.dxf.end_angle == 35.0, 'Independent angles differ')
        # Explicit independent OCS basis for these two fixed extrusion vectors.
        x, y, z = CENTER
        expected_wcs = (-x, -.8 * y + .6 * z, .6 * y + .8 * z) if omit else (-x, y, -z)
        actual_wcs = conic.ocs().to_wcs(conic.dxf.center)
        require(all(math.isclose(a, b, rel_tol=0, abs_tol=2e-14) for a, b in zip(actual_wcs, expected_wcs)),
                'OCS interpretation differs')
    print(f'PASS: {len(specs)} complete source/edit pairs / {len(expected)} drawings across nine raw families; '
          f'{corruptions} corruptions and two inventory controls rejected; no added graph diagnostics; '
          f'inherited source errors={inherited_errors}, repairs={inherited_repairs}. Synthetic corpus, not native AutoCAD qualification.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_raw_conic_geometry.py ARTIFACTS')
    main(Path(sys.argv[1]))
