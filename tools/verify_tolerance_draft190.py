#!/usr/bin/env python3
"""Verify physical tolerance labels and independently parsed MTEXT stack semantics."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.tools.text import MTextParser, TokenType
from ezdxf.lldxf.encoding import decode_dxf_unicode
from verify_raw_line_geometry import key, require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_literals import load, wire
from verify_dimension_text_blocks import one, corrupt

# Derived from the authored reference geometry, not the production formatter.
MEASURES = (10 * math.cos(math.pi / 6), 10., 90., 10., 5., 90., 2., 5 * math.pi / 2)


def expected(kind, variant):
    value = MEASURES[kind]
    mode = variant % 4
    angular = kind in (2, 5)
    prefix = '\u00d8' if kind == 3 else 'R' if kind == 4 else ''
    angle = '\u00b0' if angular else ''
    nominal = '90\u00b0' if angular else prefix + f'{value:.4f}'
    upper = .125 if mode == 1 else .25
    lower = .125
    stacks = []

    def component(measure, text, up, down, precision, affix, suffix, unit):
        if mode == 0:
            return text
        if mode == 1:
            return '{\\A1;' + text + '{\\H0.5x;\u00b1' + f'{up:.{precision}f}' + unit + '}}'
        if mode == 2:
            a, b = f'+{up:.{precision}f}' + unit, f'-{down:.{precision}f}' + unit
            stacks.append((a, b, '^'))
            return '{\\A1;' + text + '{\\H0.5x;\\S' + a + '^ ' + b + ';}}'
        a, b = f'{measure+up:.{precision}f}' + unit, f'{measure-down:.{precision}f}' + unit
        stacks.append((a, b, '^'))
        return affix + '{\\H0.5x;\\S' + a + '^ ' + b + ';}' + suffix

    text = component(value, nominal, upper, lower, 3, prefix, '', angle)
    if variant >= 4 and not angular:
        text += '[' + component(2 * value, 'A:' + f'{2*value:.2f}' + 'u',
                                2 * upper, 2 * lower, 2, 'A:', 'u', '') + ']'
    return text, stacks


def semantic(text, kind, variant):
    """The real independent MTEXT parser must see distinct numerator/denominator."""
    tokens = list(MTextParser(decode_dxf_unicode(text)))
    stacks = [t for t in tokens if t.type == TokenType.STACK]
    require([t.data for t in stacks] == expected(kind, variant)[1], 'Parsed tolerance stack rows/separator')
    require(all(t.ctx.cap_height == .5 for t in stacks), 'Parsed relative tolerance height')
    # Formatting groups must restore the nominal text size outside tolerance rows.
    words = [t for t in tokens if t.type == TokenType.WORD]
    for t in words:
        if t.data in ('[', ']', 'A:', 'u'):
            require(t.ctx.cap_height == 1., 'Tolerance height leaked outside its group')


def override_packet(parent, variant):
    starts = [i for i, tag in enumerate(parent) if tag == (1001, 'ACAD')]
    require(len(starts) == 1, 'One ACAD application')
    start = starts[0] + 1
    end = next((i for i in range(start, len(parent)) if parent[i][0] == 1001), len(parent))
    body = parent[start:end]
    require(body[:2] == [(1000, 'DSTYLE'), (1002, '{')] and body[-1:] == [(1002, '}')], 'DSTYLE framing')
    pairs = body[2:-1]
    require(len(pairs) % 2 == 0, 'Incomplete DSTYLE pair')
    values = {}
    for at in range(0, len(pairs), 2):
        code, identifier = pairs[at]
        require(code == 1070 and identifier not in values, 'Duplicate/mistyped identifier')
        values[identifier] = pairs[at+1]
    mode = variant % 4
    wanted = {178: (1070, 3)}
    if variant >= 4:
        wanted.update({71: (1070, int(mode in (1, 2))), 72: (1070, int(mode == 3)),
                       47: (1040, .125 if mode == 1 else .25), 48: (1040, .125),
                       272: (1070, 3), 274: (1070, 2), 283: (1070, 1)})
    require(values.keys() == wanted.keys(), 'Complete tolerance override field set')
    for identifier, value in wanted.items():
        require(key(values[identifier]) == key(value), f'Tolerance override {identifier}')
    return range(start, end)


def mutate_packet(record, positions, check):
    controls = 0
    for at in positions:
        for op in ('change', 'remove', 'duplicate', 'wrong-group'):
            bad = list(record)
            code, value = bad[at]
            if op == 'change': bad[at] = (code, value + '_BAD' if isinstance(value, str) else value + 1)
            elif op == 'remove': del bad[at]
            elif op == 'duplicate': bad.insert(at, bad[at])
            else: bad[at] = (999, value)
            controls += reject(lambda: check(bad))
    return controls


def inspect(path, year, binary, kind, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load(path, year)
    av = tags.index((9, '$ACADVER'))
    require(tags[av+1] == (1, PROFILES[year]), 'Physical version')
    entries = [tags[a:b] for a, b in records(tags)]
    dims = [r for r in entries if r[0][1] in ('DIMENSION', 'ARC_DIMENSION')]
    require(len(dims) == 8 and {one(r, 8)[1] for r in dims} == {f'TL_{v:02d}' for v in range(8)}, 'Physical dimension inventory')
    controls = 0
    for parent in dims:
        variant = int(one(parent, 8)[1][3:]); mode = variant % 4
        name = f'TL_STYLE_{variant:02d}'
        require(parent[0] == (0, 'ARC_DIMENSION' if kind == 7 else 'DIMENSION'), 'Dimension family')
        styles = [r for r in entries if r[0] == (0, 'DIMSTYLE') and (2, name) in r]
        require(len(styles) == 1, 'Named tolerance style')
        overrides = variant >= 4
        controls += corrupt(styles[0], {
            71: int(not overrides and mode in (1, 2)), 72: int(not overrides and mode == 3),
            47: .75 if overrides else .125 if mode == 1 else .25, 48: .5 if overrides else .125,
            272: 0 if overrides else 3, 274: 1 if overrides else 2, 283: 0 if overrides else 1,
            146: .5, 40: 2., 140: .75, 170: int(overrides), 143: 2., 171: 2, 4: 'A:[]u'})
        controls += corrupt(parent, {3: name, 1: '<>', 11: 17.25+variant, 21: 0., 31: 2.5,
                                     70: 160 | kind | (64 if kind == 6 else 0), 71: 5})
        controls += mutate_packet(parent, override_packet(parent, variant), lambda bad: override_packet(bad, variant))
        block, = [r for r in entries if r[0] == (0, 'BLOCK_RECORD') and (2, one(parent, 2)[1]) in r]
        handle = one(block, 5)[1]
        labels = [r for r in entries if r[0] == (0, 'MTEXT') and (330, handle) in r]
        def inventory(actual): require(len(actual) == 1, 'Tolerance label count')
        inventory(labels)
        controls += reject(lambda: inventory([])) + reject(lambda: inventory(labels+labels))
        text = wire(expected(kind, variant)[0], year)
        controls += corrupt(labels[0], {1: text, 330: handle, 40: 1.5, 10: 17.25+variant, 20: 0., 30: 0., 71: 5})
        semantic(one(labels[0], 1)[1], kind, variant)
        if mode in (2, 3):
            controls += reject(lambda: semantic(text.replace('^ ', '^'), kind, variant))

    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('TL_PAPER') if placement == 1 else doc.blocks['TL_HOLDER']
    hosts = list(space.query('DIMENSION ARC_DIMENSION'))
    require(len(hosts) == 8, 'Independent placement/count')
    for host in hosts:
        v = int(host.dxf.layer[3:]); mode = v % 4
        require(host.dxf.owner == space.block_record_handle, 'Independent owner')
        require(host.dxf.dimtype == (160 | kind | (64 if kind == 6 else 0)), 'Independent type/manual flag')
        require(tuple(host.dxf.text_midpoint) == (17.25+v, 0., 2.5), 'Independent manual position')
        style = doc.dimstyles.get(host.dxf.dimstyle)
        wanted = {'dimclrt': 3}
        if v >= 4:
            wanted.update(dimtol=int(mode in (1, 2)), dimlim=int(mode == 3), dimtp=.125 if mode == 1 else .25,
                          dimtm=.125, dimtdec=3, dimalttd=2, dimtolj=1)
        require(host.get_acad_dstyle(style) == wanted, 'Independent override dictionary')
        require(style.dxf.dimtp == (.75 if v >= 4 else .125 if mode == 1 else .25)
                and style.dxf.dimtm == (.5 if v >= 4 else .125), 'Independent inherited bounds')
        require([(t.code, t.value) for t in host.get_xdata('TL_KEEP')] == [(1000, 'untouched')], 'Independent neighboring XData')
        block = host.get_geometry_block(); text, = block.query('MTEXT')
        require(text.dxf.owner == block.block_record_handle, 'Independent MTEXT owner')
        require(text.text == wire(expected(kind, v)[0], year), 'Independent generated label')
        require(text.dxf.char_height == 1.5 and tuple(text.dxf.insert) == (17.25+v, 0., 0.), 'Independent height/anchor')
        semantic(text.text, kind, v)
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following geometry')
    audit = doc.audit()
    require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls


def symmetry_packet(parent, variant):
    apps = [i for i, t in enumerate(parent) if t == (1001, 'ACAD')]
    require(len(apps) == 1, 'Symmetry ACAD application')
    body = parent[apps[0]+1:]
    require(body[:2] == [(1000, 'DSTYLE'), (1002, '{')] and body[-1:] == [(1002, '}')], 'Symmetry framing')
    require((len(body)-3) % 2 == 0, 'Symmetry pairs')
    values = {}
    for i in range(2, len(body)-1, 2):
        code, identifier = body[i]
        require(code == 1070 and identifier not in values, 'Symmetry identifier')
        values[identifier] = body[i+1]
    expected = {178: (1070, 3)}
    if variant in (1, 2): expected.update({71: (1070, 1), 72: (1070, 0), 48: (1040, .125 if variant == 1 else .25)})
    if variant == 3: expected.update({47: (1040, .5), 48: (1040, .5)})
    if variant == 4: expected[48] = (1040, .25)
    require(values.keys() == expected.keys(), 'No unnecessary bound/method materialization')
    for code, value in expected.items(): require(key(values[code]) == key(value), 'Symmetry scalar value')
    return range(apps[0]+1, len(parent))


def inspect_symmetry(path, year, binary, variant):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Symmetry transport')
    tags = load(path, year)
    require(tags[tags.index((9, '$ACADVER'))+1] == (1, PROFILES[year]), 'Symmetry version')
    lower = .125 if variant == 2 else -0.0 if variant == 5 else .25
    at = tags.index((9, '$DIMTM'))+1
    require(key(tags[at]) == key((40, lower)), 'Symmetrical header value and zero sign')
    rs = [tags[a:b] for a,b in records(tags)]
    style, = [r for r in rs if r[0] == (0, 'DIMSTYLE') and (2, 'SN_STYLE') in r]
    count = corrupt(style, {47: 0. if variant == 5 else .25, 48: lower, 71: 1, 72: 0, 272: 3, 146: .5})
    parent, = [r for r in rs if r[0] == (0, 'DIMENSION')]
    count += mutate_packet(parent, symmetry_packet(parent, variant), lambda bad: symmetry_packet(bad, variant))
    doc = ezdxf.readfile(path); host, = doc.modelspace().query('DIMENSION')
    require(host.dxf.owner == doc.modelspace().block_record_handle, 'Symmetry host owner')
    style = doc.dimstyles.get('SN_STYLE')
    require(key((48, style.dxf.dimtm)) == key((48, lower)), 'Independent native base lower and zero sign')
    overrides = {'dimclrt': 3}
    if variant in (1, 2): overrides.update(dimtol=1, dimlim=0, dimtm=.125 if variant == 1 else .25)
    if variant == 3: overrides.update(dimtp=.5, dimtm=.5)
    if variant == 4: overrides.update(dimtm=.25)
    require(host.get_acad_dstyle(style) == overrides, 'Independent minimal native override projection')
    block = host.get_geometry_block(); text, = block.query('MTEXT')
    expected_text = (r'{\A1;10.0000{\H0.5x;\S+0.250^ -0.125;}}' if variant == 1 else
                     r'{\A1;10.0000{\H0.5x;±' + ('0.500' if variant == 3 else '0.000' if variant == 5 else '0.250') + '}}')
    require(text.text == wire(expected_text, year), 'Independent symmetry label')
    tokens = list(MTextParser(decode_dxf_unicode(text.text)))
    require([t.data for t in tokens if t.type == TokenType.STACK] == ([('+0.250', '-0.125', '^')] if variant == 1 else []), 'Symmetry parsed rows')
    label, = [r for r in rs if r[0] == (0, 'MTEXT') and (330, block.block_record_handle) in r]
    count += corrupt(label, {1: wire(expected_text, year), 40: 1.5, 330: block.block_record_handle})
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Symmetry independent graph')
    return count


def main(directory):
    specs = [s for s in itertools.product(PROFILES, (False, True), range(8), range(3), ('source', 'False', 'True'))
             if not (s[0] == 2000 and s[2] == 7)]
    def name(s):
        year, binary, kind, placement, output = s
        return f'draft190-tolerance-label-AutoCad{year}-{binary}-{kind}-{placement}-{output}.dxf'
    expected_files = {name(s) for s in specs}
    def inventory(actual):
        require(actual == expected_files, f'Incomplete tolerance corpus missing={len(expected_files-actual)}, extra={len(actual-expected_files)}')
    inventory({p.name for p in directory.glob('draft190-tolerance-label-*.dxf')})
    reject(lambda: inventory(expected_files-{min(expected_files)}))
    reject(lambda: inventory(expected_files|{'draft190-tolerance-label-extra.dxf'}))
    symmetry = list(itertools.product(PROFILES, (False, True), range(6), ('source', 'False', 'True')))
    def symmetry_name(s):
        year, binary, variant, output = s
        return f'draft190-tolerance-symmetric-AutoCad{year}-{binary}-{variant}-{output}.dxf'
    symmetry_files = {symmetry_name(s) for s in symmetry}
    def symmetry_inventory(actual): require(actual == symmetry_files, 'Incomplete symmetry corpus')
    symmetry_inventory({p.name for p in directory.glob('draft190-tolerance-symmetric-*.dxf')})
    reject(lambda: symmetry_inventory(symmetry_files-{min(symmetry_files)}))
    reject(lambda: symmetry_inventory(symmetry_files|{'draft190-tolerance-symmetric-extra.dxf'}))
    controls = 0
    for s in symmetry:
        year, binary, variant, output = s
        controls += inspect_symmetry(directory/symmetry_name(s), year, binary if output == 'source' else output == 'True', variant)
    for s in specs:
        year, binary, kind, placement, output = s
        controls += inspect(directory/name(s), year, binary if output == 'source' else output == 'True', kind, placement)
    # Escaped fractions remain row content, not the outer stack separator.
    tokens = list(MTextParser(r'{\H0.5x;\S+1 1\/2^ -0 1\/4;}'))
    require([t.data for t in tokens if t.type == TokenType.STACK] == [('+1 1/2', '-0 1/4', '^')], 'Independent fractional-row grammar')
    print(f'PASS: {len(specs)+len(symmetry)} tolerance drawings / {8*len(specs)+len(symmetry)} independent dimension records; '
          f'{controls} packet/label/parser corruptions and four inventory controls rejected; no graph errors/repairs. '
          'Numeric MTEXT grammar is checked, not native AutoCAD font metrics or placement.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_tolerance_labels.py ARTIFACTS')
    main(Path(sys.argv[1]))
