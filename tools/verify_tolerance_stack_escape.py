#!/usr/bin/env python3
"""Parse literal tolerance rows, not merely matching MTEXT writer strings."""
import itertools
import json
from pathlib import Path
import sys
import ezdxf
from ezdxf.tools.text import MTextParser, TokenType
from verify_dimension_text_literals import load
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one, corrupt
from verify_raw_line_geometry import require, reject

LEXEMES = ('', '1/2', '0#5', '0;5', '0^5', '0\\5', '0{5', '0}5', '1/2;3\\4^5', 'Ł±', '#/{};\\^')
SEPARATORS = '#/^;\\{}'
# Independent exact rational arithmetic: nominal 10, allowances +1/2 and -1/4,
# alternate multiplier 2; fractional precision is eighths, decimal precision 3.
ROWS = (
    ('+0 1/2', '-0 1/4'), ('10 1/2', '9 3/4'),
    ('+0\'-0 1/2"', '-0\'-0 1/4"'), ('0\'-10 1/2"', '0\'-9 3/4"'),
    ('+0.500', '-0.250', '+1', '-0 1/2'),
    ('+0.500', '-0.250', '+0\'-1"', '-0\'-0 1/2"'),
    ('10.500', '9.750', '21', '19 1/2'),
    ('10.500', '9.750', '1\'-9"', '1\'-7 1/2"'),
)


def rows(variant):
    return ROWS[variant] if variant < 8 else ('10' + SEPARATORS[variant-8] + '500', '9' + SEPARATORS[variant-8] + '750')


def semantic(text, expected):
    tokens = list(MTextParser(text))
    stacks = [t for t in tokens if t.type == TokenType.STACK]
    wanted = [(expected[i], expected[i+1], '^') for i in range(0, len(expected), 2)]
    require([t.data for t in stacks] == wanted, 'Decoded tolerance rows or stack divider differ')
    require(all(t.ctx.cap_height == .5 for t in stacks), 'Stack height differs')
    require(tokens and tokens[0].type == TokenType.WORD and tokens[0].data == 'BEGIN', 'Prefix scope lost')
    require(tokens[-1].type == TokenType.WORD and tokens[-1].data.endswith('END') and tokens[-1].ctx.cap_height == 1., 'Suffix scope/height not restored')
    require(not any(t.type in (TokenType.NEW_PARAGRAPH, TokenType.NEW_COLUMN) for t in tokens), 'Unexpected layout command in row')


def check_label(record, expected):
    # These bounded fixtures fit one terminal content tag; reject missing/extra chunks.
    require(not any(c == 3 for c, _ in record), 'Unexpected MTEXT continuation')
    semantic(one(record, 1)[1], expected)


def label_controls(record, expected):
    check_label(record, expected)
    at, text = one(record, 1)
    count = 0
    # These mutations are made to the actual exported MTEXT content tag.
    bad_texts = [text.replace('\\S', '\\SBAD', 1), text.replace('^ ', '^', 1), text.replace('\\H0.5x;', '\\H0.75x;', 1)]
    for char in '/#;\\{}^':
        token = '\\' + char + (' ' if char == '^' else '')
        if token in text:
            bad_texts.append(text.replace(token, f'\\U+{ord(char):04X}', 1))
    for value in bad_texts:
        bad = list(record); bad[at] = (1, value)
        count += reject(lambda: check_label(bad, expected))
    for operation in ('remove', 'duplicate'):
        bad = list(record)
        if operation == 'remove': del bad[at]
        else: bad.insert(at, bad[at])
        count += reject(lambda: check_label(bad, expected))
    return count


def inspect(path, year, binary, placement, overridden):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong physical transport')
    tags = load(path, year)
    av = [i for i, tag in enumerate(tags) if tag == (9, '$ACADVER')]
    require(len(av) == 1 and tags[av[0]+1] == (1, PROFILES[year]), 'Wrong physical version')
    content = [tags[a:b] for a, b in records(tags)]
    hosts = [r for r in content if r[0] == (0, 'DIMENSION')]
    require(len(hosts) == 15 and {one(h, 8)[1] for h in hosts} == {f'STACK_ROW_{v:02d}' for v in range(15)}, 'Physical host inventory')
    controls = 0
    physical = {}
    for host in hosts:
        v = int(one(host, 8)[1].split('_')[-1]); limits = v in (1, 3, 6, 7) or v >= 8
        name = f'STACK_STYLE_1_{v}_{overridden}'
        styles = [r for r in content if r[0] == (0, 'DIMSTYLE') and (2, name) in r]
        require(len(styles) == 1, 'Named style count')
        controls += corrupt(styles[0], {47: 9. if overridden else .5, 48: 8. if overridden else .25,
            71: 0 if overridden or limits else 1, 72: 1 if not overridden and limits else 0,
            272: 3, 274: 3, 146: .5, 277: 5 if v < 2 else 4 if v < 4 else 2,
            278: ord(SEPARATORS[v-8]) if v >= 8 else 46})
        controls += corrupt(host, {1: 'BEGIN<>END', 3: name, 70: 33,
            13: 0., 23: 0., 33: 0., 14: 10., 24: 0., 34: 0.})
        blocks = [r for r in content if r[0] == (0, 'BLOCK_RECORD') and (2, one(host, 2)[1]) in r]
        require(len(blocks) == 1, 'Dimension geometry block')
        handle = one(blocks[0], 5)[1]
        labels = [r for r in content if r[0] == (0, 'MTEXT') and (330, handle) in r]
        def inventory(items): require(len(items) == 1, 'Exactly one geometry label')
        inventory(labels)
        controls += reject(lambda: inventory([])) + reject(lambda: inventory(labels + labels))
        controls += corrupt(labels[0], {330: handle, 40: .75})
        controls += label_controls(labels[0], rows(v))
        physical[v] = one(labels[0], 1)[1]
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('STACK_PAPER') if placement == 1 else doc.blocks['STACK_HOLDER']
    dims = list(space.query('DIMENSION')); require(len(dims) == 15, 'Independent dimension placement')
    for host in dims:
        v = int(host.dxf.layer.split('_')[-1]); limits = v in (1, 3, 6, 7) or v >= 8
        require(host.dxf.owner == space.block_record_handle and host.dxf.dimtype == 33, 'Independent ownership/type')
        require(tuple(host.dxf.defpoint2) == (0., 0., 0.) and tuple(host.dxf.defpoint3) == (10., 0., 0.), 'Independent measured geometry')
        style = doc.dimstyles.get(host.dxf.dimstyle)
        wanted = {'dimclrt': 4}
        if overridden: wanted.update(dimtol=0 if limits else 1, dimlim=1 if limits else 0, dimtp=.5, dimtm=.25)
        require(host.get_acad_dstyle(style) == wanted, 'Independent complete override set')
        require([(t.code, t.value) for t in host.get_xdata('STACK_KEEP')] == [(1000, 'untouched')], 'Unrelated application')
        block = host.get_geometry_block(); text, = block.query('MTEXT')
        require(text.dxf.owner == block.block_record_handle and text.dxf.char_height == .75, 'Independent label owner/height')
        require(text.text == physical[v], 'Physical and independently loaded MTEXT differ')
        semantic(text.text, rows(v))
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following LINE')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls


def lexemes(path):
    data = json.loads(path.read_text(encoding='utf-8'))
    def validate(items):
        require(len(items) == len(LEXEMES), 'Lexeme inventory')
        for index, item in enumerate(items):
            require(item['index'] == index and item['raw'] == LEXEMES[index], 'Lexeme input/identity')
            text = 'BEGIN{\\H0.5x;\\S' + item['encoded'] + '^ ' + item['encoded'] + ';}END'
            semantic(text, (item['raw'], item['raw']))
    validate(data)
    count = reject(lambda: validate(data[:-1])) + reject(lambda: validate(data + data[:1]))
    for at in range(len(data)):
        bad = [dict(item) for item in data]; bad[at]['encoded'] += 'BAD'
        count += reject(lambda: validate(bad))
    return count


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), range(3), (False, True), ('source', 'False', 'True')))
    def name(s):
        year, binary, placement, overridden, output = s
        return f'tolerance-stack-escape-AutoCad{year}-{binary}-{placement}-{overridden}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, f'Stack corpus missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('tolerance-stack-escape-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'tolerance-stack-escape-extra.dxf'}))
    controls = lexemes(directory / 'tolerance-stack-lexemes.json')
    for spec in specs:
        year, binary, placement, overridden, output = spec
        controls += inspect(directory / name(spec), year, binary if output == 'source' else output == 'True', placement, overridden)
    print(f'PASS: {len(specs)} drawings / {15*len(specs)} independently parsed dimension labels and {len(LEXEMES)} lexemes; '
          f'{controls} packet/parser/lexeme controls and two drawing inventory controls rejected; zero graph errors/repairs. '
          'Parsed text and scope do not certify native font metrics or visual layout.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_tolerance_stack_escape.py ARTIFACTS')
    main(Path(sys.argv[1]))
