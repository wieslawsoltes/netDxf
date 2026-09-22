#!/usr/bin/env python3
"""Verify literal DIMENSION/ARC_DIMENSION text, suppression and regenerated MTEXT."""
import io
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from verify_raw_line_geometry import require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one, corrupt

LITERALS = (' ', '  ', '\t', ' \t ', '', '<>', '  FIXED  ', '\u00a0')
# Independently derived from the fixed source geometry and its declared precision.
MEASURED = (f'{10 * math.cos(math.pi/6):.4f}', '10.0000', '90\u00b0',
            '\u00d810.0000', 'R5.0000', '90\u00b0', '2.0000', f'{5 * math.pi/2:.4f}')


def wire(value, year):
    return ''.join(f'\\U+{ord(c):04X}' if ord(c) > 127 else c for c in value) if year < 2007 else value


def load(path, year):
    data = path.read_bytes()
    tags = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(
        io.StringIO(data.decode('utf-8' if year >= 2007 else 'cp1252'), newline=None))
    return [(t.code, cast_tag_value(t.code, t.value)) for t in tags]


def inspect(path, year, binary, placement, kind):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load(path, year)
    at = tags.index((9, '$ACADVER')); require(tags[at + 1] == (1, PROFILES[year]), 'Wrong physical profile')
    entries = [tags[a:b] for a, b in records(tags)]
    dims = [r for r in entries if r[0][1] in ('DIMENSION', 'ARC_DIMENSION')]
    require(len(dims) == len(LITERALS), 'Missing/extra dimension')
    require({one(r, 8)[1] for r in dims} == {f'LITERAL_{v}' for v in range(len(LITERALS))}, 'Literal layer inventory')
    corruptions = 0
    for parent in dims:
        v = int(one(parent, 8)[1].split('_')[1])
        require(parent[0][1] == ('ARC_DIMENSION' if kind == 7 else 'DIMENSION'), 'Entity type')
        wanted = {1: wire(LITERALS[v], year), 11: 17.25 + v, 21: 0., 31: 2.5,
                  70: 160 | kind | (64 if kind == 6 else 0), 71: 5}
        corruptions += corrupt(parent, wanted)
        name = one(parent, 2)[1]
        blocks = [r for r in entries if r[0] == (0, 'BLOCK_RECORD') and (2, name) in r]
        require(len(blocks) == 1, 'Dimension geometry block')
        handle = one(blocks[0], 5)[1]
        labels = [r for r in entries if r[0] == (0, 'MTEXT') and (330, handle) in r]
        def inventory(actual): require(len(actual) == (0 if v == 0 else 1), 'Suppression/label count')
        inventory(labels)
        if v == 0:
            corruptions += reject(lambda: inventory([[(0, 'MTEXT'), (330, handle), (1, 'WRONG')]]))
        else:
            corruptions += reject(lambda: inventory([]))
            corruptions += reject(lambda: inventory(labels + labels))
            value = MEASURED[kind] if v in (4, 5) else LITERALS[v]
            corruptions += corrupt(labels[0], {1: wire(value, year), 10: 17.25 + v, 20: 0., 30: 0., 330: handle, 71: 5})
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent profile')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('LITERAL_PAPER') if placement == 1 else doc.blocks['LITERAL_HOLDER']
    loaded = list(space.query('DIMENSION ARC_DIMENSION'))
    require(len(loaded) == len(LITERALS), 'Independent placement/count')
    for parent in loaded:
        v = int(parent.dxf.layer.split('_')[1])
        require(parent.dxf.owner == space.block_record_handle, 'Independent dimension owner')
        require(parent.dxf.text == wire(LITERALS[v], year), 'Independent literal text')
        require(tuple(parent.dxf.text_midpoint) == (17.25 + v, 0., 2.5), 'Independent manual anchor')
        require(parent.dxf.dimtype == 160 | kind | (64 if kind == 6 else 0), 'Independent type/manual flag')
        block = parent.get_geometry_block(); labels = list(block.query('MTEXT'))
        require(len(labels) == (0 if v == 0 else 1), 'Independent text suppression')
        if labels:
            text = labels[0]
            value = MEASURED[kind] if v in (4, 5) else LITERALS[v]
            require(text.text == wire(value, year), 'Independent label text')
            require(text.dxf.owner == block.block_record_handle and tuple(text.dxf.insert) == (17.25 + v, 0., 0.), 'Independent label owner/anchor')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following LINE')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return corruptions


def main(directory):
    specs = [s for s in itertools.product(PROFILES, (False, True), range(3), range(8), ('source', 'False', 'True'))
             if s[0] != 2000 or s[3] != 7]
    def name(s):
        year, binary, placement, kind, output = s
        return f'dimension-text-literal-AutoCad{year}-{binary}-{placement}-{kind}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, f'Incomplete literal corpus: missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('dimension-text-literal-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'dimension-text-literal-extra.dxf'}))
    corruptions = 0
    for spec in specs:
        year, binary, placement, kind, output = spec
        corruptions += inspect(directory / name(spec), year, binary if output == 'source' else output == 'True', placement, kind)
    print(f'PASS: {len(specs)} literal-text drawings / {8*len(specs)} independent dimension records; '
          f'{corruptions} packet/label corruptions and two inventory controls rejected; zero graph errors/repairs. '
          'Literal preservation and generated label checks do not certify native font or fit rendering.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimension_text_literals.py ARTIFACTS')
    main(Path(sys.argv[1]))
