#!/usr/bin/env python3
"""Check angular allowance units and parsed MTEXT stack rows, not just output strings."""
import itertools
from fractions import Fraction
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.tools.text import MTextParser, TokenType
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_literals import load, wire
from verify_dimension_text_blocks import one, corrupt
from verify_raw_line_geometry import require, reject, key

SEPARATORS = '/#;^{}\\'
# The nominal angle is 90 degrees. Allowances are .25/.125 in the SELECTED units.
NOMINAL = ('90.00°', "90°0'", '100.00g', '1.57r')
UPPER = ('0.250°', '0°15\'0"', '0.250g', '0.250r')
LOWER = ('0.125°', '0°7\'30"', '0.125g', '0.125r')
HIGH = ('90.250°', '90°15\'0"', '100.250g', '1.821r')
LOW = ('89.875°', '89°52\'30"', '99.875g', '1.446r')


def escape(value):
    # Caret decoding precedes stack parsing; preserve a literal caret in both passes.
    return ''.join('\\^ ' if c == '^' else '\\' + c if c in '\\/#;{}' else c for c in value)


def stack(high, low):
    return '{\\H0.5x;\\S' + escape(high) + '^ ' + escape(low) + ';}'


def fraction(value, architectural):
    value = Fraction(value)
    feet = int(value // 12) if architectural else 0
    if architectural:
        value -= 12 * feet
    integer = int(value)
    part = value - integer
    body = str(integer) + (f' {part.numerator}/{part.denominator}' if part else '')
    return (str(feet) + "'-" if feet else '') + body + ('"' if architectural else '')


def expected(layer):
    parts = layer.split('_')
    if parts[1] == 'A':
        unit, mode, variant = map(int, parts[2:])
        if mode == 1:
            return '{\\A1;' + NOMINAL[unit] + '{\\H0.5x;±' + UPPER[unit] + '}}TAIL', []
        rows = [(('+' + UPPER[unit], '-' + LOWER[unit]) if mode == 2 else (HIGH[unit], LOW[unit]))]
        text = stack(*rows[0])
        if mode == 2:
            text = '{\\A1;' + NOMINAL[unit] + text + '}'
        return text + 'TAIL', rows
    if parts[1] == 'C':
        sep = SEPARATORS[int(parts[2])]
        rows = [('11' + sep + '00', '10' + sep + '25')]
        return stack(*rows[0]) + 'TAIL', rows
    units, mode, alternate = map(int, parts[2:])
    all_rows = []
    def component(factor):
        fmt = lambda v: fraction(v * factor, units == 1)
        rows = ('+' + fmt(Fraction(1, 2)), '-' + fmt(Fraction(1, 4))) if mode == 2 else (fmt(11), fmt(Fraction(41, 4)))
        all_rows.append(rows)
        text = stack(*rows)
        return '{\\A1;' + fmt(Fraction(21, 2)) + text + '}' if mode == 2 else text
    text = component(1)
    if alternate:
        text += '[' + component(2) + ']'
    return text + 'TAIL', all_rows


def semantic_tokens(text, rows):
    tokens = list(MTextParser(decode_dxf_unicode(text)))
    stacks = [token for token in tokens if token.type == TokenType.STACK]
    require([token.data for token in stacks] == [(u, l, '^') for u, l in rows], 'Decoded tolerance rows/separator changed')
    require(all(token.ctx.cap_height == .5 for token in stacks), 'Tolerance stack height changed')
    tails = [token for token in tokens if token.type == TokenType.WORD and token.data.endswith('TAIL')]
    require(len(tails) == 1 and tails[0].ctx.cap_height == 1., 'Tolerance formatting leaked beyond its group')
    if not rows:
        signs = [token for token in tokens if token.type == TokenType.WORD and token.data.startswith('±')]
        require(len(signs) == 1 and signs[0].ctx.cap_height == .5, 'Symmetric allowance height changed')


def checked_text(text, layer, year):
    wanted, rows = expected(layer)
    require(text == wire(wanted, year), 'Physical generated expression changed')
    semantic_tokens(text, rows)
    controls = 0
    # These exercise actual parser failures independently of exact-string comparison.
    if rows:
        controls += reject(lambda: semantic_tokens(text.replace('^ ', '^', 1), rows))
        controls += reject(lambda: semantic_tokens(text.replace('\\H0.5x;', '\\H0.25x;', 1), rows))
        controls += reject(lambda: semantic_tokens(text.replace('\\S', '\\S9', 1), rows))
        if '/' in rows[0][0]:
            controls += reject(lambda: semantic_tokens(text.replace('\\/', '/', 1), rows))
    return controls


def override_packet(host, unit, mode, variant):
    apps = [i for i, tag in enumerate(host) if tag == (1001, 'ACAD')]
    require(len(apps) == (1 if variant else 0), 'Sparse override presence')
    if not apps:
        return
    start = apps[0] + 1
    end = next((i for i in range(start, len(host)) if host[i][0] == 1001), len(host))
    tags = host[start:end]
    require(tags[:2] == [(1000, 'DSTYLE'), (1002, '{')] and tags[-1] == (1002, '}'), 'Override framing')
    values = {}
    for at in range(2, len(tags)-1, 2):
        require(tags[at][0] == 1070 and tags[at][1] not in values and at+1 < len(tags)-1, 'Override pair structure')
        values[tags[at][1]] = tags[at+1]
    wanted = {178: (1070, 4)} if variant == 1 else {
        275: (1070, unit), 71: (1070, int(mode != 3)), 72: (1070, int(mode == 3)),
        47: (1040, .25), 48: (1040, .25 if mode == 1 else .125), 272: (1070, 3)}
    require(values.keys() == wanted.keys() and all(key(values[c]) == key(t) for c, t in wanted.items()), 'Complete angular override values')


def layers(group):
    if group < 2:
        return {f'TSEM_A_{u}_{m}_{v}' for u, m, v in itertools.product(range(4), range(1, 4), range(3))}
    return {f'TSEM_F_{u}_{m}_{a}' for u, m, a in itertools.product(range(2), (2, 3), range(2))} | {f'TSEM_C_{i}' for i in range(7)}


def inspect(path, year, binary, placement, group):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load(path, year)
    at = tags.index((9, '$ACADVER'))
    require(tags[at+1] == (1, PROFILES[year]), 'Version')
    content = [tags[a:b] for a, b in records(tags)]
    hosts = [r for r in content if r[0] == (0, 'DIMENSION')]
    require(len(hosts) == len(layers(group)) and {one(h, 8)[1] for h in hosts} == layers(group), 'Physical host inventory')
    controls = 0
    for host in hosts:
        layer = one(host, 8)[1]
        controls += corrupt(host, {1: '<>TAIL', 70: (162 if group == 0 else 165 if group == 1 else 161), 11: 20., 21: 40., 31: 0.})
        style, = [r for r in content if r[0] == (0, 'DIMSTYLE') and (2, one(host, 3)[1]) in r]
        if group < 2:
            u, m, v = map(int, layer.split('_')[2:])
            controls += corrupt(style, {275: 0 if v == 2 else u, 272: 1 if v == 2 else 3,
                47: 9. if v == 2 else .25, 48: 8. if v == 2 else .25 if m == 1 else .125})
            override_packet(host, u, m, v)
        else:
            controls += corrupt(style, {47: .5, 48: .25, 272: 2})
        block, = [r for r in content if r[0] == (0, 'BLOCK_RECORD') and (2, one(host, 2)[1]) in r]
        handle = one(block, 5)[1]
        texts = [r for r in content if r[0] == (0, 'MTEXT') and (330, handle) in r]
        require(len(texts) == 1, 'Physical label inventory')
        text = texts[0]
        controls += corrupt(text, {1: wire(expected(layer)[0], year), 330: handle, 10: 20., 20: 40., 30: 0., 40: .75})
        controls += checked_text(one(text, 1)[1], layer, year)
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('TSEM_PAPER') if placement == 1 else doc.blocks['TSEM_BLOCK']
    dims = list(space.query('DIMENSION'))
    require(len(dims) == len(layers(group)) and {d.dxf.layer for d in dims} == layers(group), 'Independent host placement')
    for dim in dims:
        require(dim.dxf.owner == space.block_record_handle, 'Independent owner')
        text, = dim.get_geometry_block().query('MTEXT')
        require(text.text == wire(expected(dim.dxf.layer)[0], year), 'Independent label')
        semantic_tokens(text.text, expected(dim.dxf.layer)[1])
        require(tuple(text.dxf.insert) == (20., 40., 0.) and text.dxf.owner == dim.get_geometry_block().block_record_handle, 'Independent label placement')
        style = doc.dimstyles.get(dim.dxf.dimstyle)
        overrides = dim.get_acad_dstyle(style)
        get = lambda field: overrides.get(field, style.dxf.get(field))
        if group < 2:
            u, m, v = map(int, dim.dxf.layer.split('_')[2:])
            require(get('dimaunit') == u and get('dimtp') == .25 and get('dimtm') == (.25 if m == 1 else .125), 'Independent angular units/allowances')
            require(get('dimtdec') == 3 and get('dimtol') == int(m != 3) and get('dimlim') == int(m == 3), 'Independent angular precision/mode')
        require([(t.code, t.value) for t in dim.get_xdata('TSEM_KEEP')] == [(1000, 'unchanged')], 'Other application data')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (1.25, -2.5, 3.75) and tuple(line.dxf.end) == (-4.5, 5.25, 6.75), 'Following geometry')
    audit = doc.audit()
    require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), range(3), range(3), ('source', 'False', 'True')))
    def name(s):
        y, b, p, g, o = s
        return f'tolerance-unit-semantic-AutoCad{y}-{b}-{p}-{g}-{o}.dxf'
    wanted = {name(s) for s in specs}
    def inventory(actual):
        require(actual == wanted, f'Missing={len(wanted-actual)} /extra={len(actual-wanted)} semantic drawings')
    inventory({p.name for p in directory.glob('tolerance-unit-semantic-*.dxf')})
    reject(lambda: inventory(wanted - {min(wanted)}))
    reject(lambda: inventory(wanted | {'tolerance-unit-semantic-extra.dxf'}))
    controls = 0
    for s in specs:
        y, b, p, g, o = s
        controls += inspect(directory/name(s), y, b if o == 'source' else o == 'True', p, g)
    print(f'PASS: {len(specs)} semantic drawings / {sum(len(layers(s[3])) for s in specs)} independent dimensions; '
          f'{controls} actual packet/parser mutations and two inventory controls rejected; zero graph errors/repairs. '
          'Parsed text and units are qualified, not native AutoCAD glyph/fit equivalence.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_tolerance_unit_semantics.py ARTIFACTS')
    main(Path(sys.argv[1]))
