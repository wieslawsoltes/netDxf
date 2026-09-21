#!/usr/bin/env python3
"""Check regenerated dimension label packets, independent anchors and complete inventory."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import key, load_tags, require, reject
from verify_dimlfac_fidelity import PROFILES, records


def rotation(kind):
    # These angles follow the explicit fixture geometry, not netDxf's output.
    if kind == 0: return 55.0
    if kind == 1: return 25.0
    if kind in (2, 5, 7): return 340.0
    if kind in (3, 4): return (math.degrees(math.atan2(-8.5, 17.25)) + 25.0) % 360.0
    return 115.0


def near(actual, wanted, label):
    require(math.isfinite(actual) and abs(actual-wanted) < 1e-12, label)


def one(record, code):
    values = [(i, v) for i, (c, v) in enumerate(record) if c == code]
    require(len(values) == 1, f'Missing or duplicate group {code}')
    return values[0]


def packet(record, wanted, directions=None):
    positions = []
    for code, value in wanted.items():
        at, actual = one(record, code)
        require(key((code, actual)) == key((code, value)), f'Incorrect stored group {code}')
        positions.append(at)
    for code, value in (directions or {}).items():
        at, actual = one(record, code)
        require(isinstance(actual, float), 'Direction must be a real value')
        near(actual, value, f'Incorrect direction {code}')
        positions.append(at)
    return positions


def corrupt(record, wanted, directions=None):
    count = 0
    for at in packet(record, wanted, directions):
        for operation in ('change', 'remove', 'duplicate', 'wrong-group'):
            bad = list(record)
            code, value = bad[at]
            if operation == 'change':
                bad[at] = (code, value + '_CORRUPT' if isinstance(value, str) else value + 0.125)
            elif operation == 'remove': del bad[at]
            elif operation == 'duplicate': bad.insert(at, bad[at])
            else: bad[at] = (1071, value)
            count += reject(lambda: packet(bad, wanted, directions))
    return count


def inspect_file(path, year, binary, placement, kind):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_tags(path)
    av = [i for i, tag in enumerate(tags) if tag == (9, '$ACADVER')]
    require(len(av) == 1 and tags[av[0]+1] == (1, PROFILES[year]), 'Physical version')
    content = [tags[a:b] for a, b in records(tags)]
    dims = [r for r in content if r[0][1] in ('DIMENSION', 'ARC_DIMENSION')]
    require(len(dims) == 1 and dims[0][0][1] == ('ARC_DIMENSION' if kind == 7 else 'DIMENSION'), 'Physical dimension kind/count')
    parent = dims[0]
    _, geometry = one(parent, 2)
    block_records = [r for r in content if r[0] == (0, 'BLOCK_RECORD') and (2, geometry) in r]
    require(len(block_records) == 1, 'Geometry block reference')
    _, block_handle = one(block_records[0], 5)
    labels = [r for r in content if r[0] == (0, 'MTEXT') and (330, block_handle) in r]
    require(len(labels) == 1, 'Exactly one manual geometry label')
    direction = (math.cos(math.radians(rotation(kind))), math.sin(math.radians(rotation(kind))), 0.0)
    normal = (0.0, 0.0, 1.0) if placement == 0 else (0.0, 1.0, 0.0)
    expected_parent = {11: 17.25, 21: -8.5, 31: 2.5, 53: 25.0,
                       70: 160 | kind | (64 if kind == 6 else 0), 71: kind+1,
                       72: 2, 41: 1.5, 1: 'UPPER\\XLOWER',
                       210: normal[0], 220: normal[1], 230: normal[2]}
    expected_label = {10: 17.25, 20: -8.5, 30: 0.0, 210: 0.0, 220: 0.0, 230: 1.0,
                      1: 'UPPER\\PLOWER', 40: 0.75, 44: 1.5, 71: kind+1, 73: 2, 330: block_handle}
    corruptions = corrupt(parent, expected_parent)
    corruptions += corrupt(labels[0], expected_label, dict(zip((11, 21, 31), direction)))

    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('TEXT_PAPER') if placement == 1 else doc.blocks['TEXT_HOLDER']
    entities = list(space.query('DIMENSION ARC_DIMENSION'))
    require(len(entities) == 1, 'Independent dimension placement/count')
    dimension = entities[0]
    require(dimension.dxf.owner == space.block_record_handle, 'Dimension owner')
    require(tuple(dimension.dxf.text_midpoint) == (17.25, -8.5, 2.5), 'Independent stored anchor')
    require(dimension.dxf.dimtype == expected_parent[70], 'Independent manual/type flags')
    require(dimension.dxf.text_rotation == 25.0 and dimension.dxf.attachment_point == kind+1, 'Independent text settings')
    require(dimension.dxf.line_spacing_style == 2 and dimension.dxf.line_spacing_factor == 1.5, 'Independent dimension spacing')
    require(tuple(dimension.dxf.extrusion) == normal, 'Independent plane')
    block = dimension.get_geometry_block()
    texts = list(block.query('MTEXT'))
    require(len(texts) == 1, 'Independent geometry label count')
    text = texts[0]
    require(text.dxf.owner == block.block_record_handle and tuple(text.dxf.insert) == (17.25, -8.5, 0.0), 'Independent label anchor/owner')
    require(text.text == 'UPPER\\PLOWER' and text.dxf.attachment_point == kind+1, 'Independent paragraph/attachment')
    require(text.dxf.line_spacing_style == 2 and text.dxf.line_spacing_factor == 1.5, 'Independent label spacing')
    require(text.dxf.char_height == 0.75, 'Independent text style height')
    for actual, wanted in zip(text.dxf.text_direction, direction): near(actual, wanted, 'Independent label direction')
    world_checks = 0
    if placement != 0:
        virtual = [e for e in dimension.virtual_entities() if e.dxftype() == 'MTEXT']
        require(len(virtual) == 1, 'Independent virtual label count')
        require(tuple(virtual[0].dxf.insert) == (-17.25, 2.5, -8.5), 'Independent tilted WCS anchor')
        for actual, wanted in zip(virtual[0].dxf.text_direction, (-direction[0], 0.0, direction[1])):
            near(actual, wanted, 'Independent tilted WCS direction')
        world_checks = 1
    audit = doc.audit()
    require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return corruptions, world_checks


def main(directory):
    specs = [s for s in itertools.product(PROFILES, (False, True), range(3), range(8), ('source', 'False', 'True'))
             if s[0] != 2000 or s[3] != 7]
    def name(spec):
        year, source, placement, kind, output = spec
        return f'dimension-text-block-AutoCad{year}-{source}-{placement}-{kind}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, f'Incomplete inventory: missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('dimension-text-block-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'dimension-text-block-extra.dxf'}))
    corruptions = world_checks = 0
    for spec in specs:
        year, source, placement, kind, output = spec
        count, worlds = inspect_file(directory / name(spec), year, source if output == 'source' else output == 'True', placement, kind)
        corruptions += count; world_checks += worlds
    print(f'PASS: {len(specs)} regenerated dimension drawings, physical and independent labels; '
          f'{world_checks} tilted WCS anchors/directions; {corruptions} actual-tag corruptions '
          'and two inventory controls rejected; zero graph errors/repairs. '
          'This does not qualify native font metrics, DIMTMOVE leader routing or AutoCAD rendering.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimension_text_blocks.py ARTIFACTS')
    main(Path(sys.argv[1]))
