#!/usr/bin/env python3
"""DIMPOST/DIMAPOST complete-pair fidelity, independent loads and closed inventories."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_dimension_text_literals import load, wire
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one, corrupt
from verify_raw_line_geometry import key, require, reject

BASE = ('S:', ':END', 'A:', ':ALT')
CASES = (
    (None, None, None, None), ('O:', None, None, None), (None, ':TAIL', None, None),
    ('', None, None, None), (None, '', None, None), ('', '', None, None),
    (None, None, 'Q:', None), (None, None, None, ':TAIL'), (None, None, '', None),
    (None, None, None, ''), (None, None, '', ''), (' \t', '\u00b5\u03a9', ' \t', '\u00b5\u03a9'),
    ('P:', '::<>tail', 'Q:', '::[]tail'), ('O:', ':TAIL', 'Q:', ':ALT2'), ('', ':TAIL', 'Q:', '')
)
# Independent values follow the fixture's geometric definitions, not a netDxf report.
MEASURES = ('8.6603', '10.0000', '90\u00b0', '10.0000', '5.0000', '90\u00b0', '2.0000', '7.8540')


def expected_pairs(variant):
    spec = CASES[variant]
    result = {}
    for pair, placeholder in enumerate(('<>', '[]')):
        a, b = spec[pair*2:pair*2+2]
        if a is not None or b is not None:
            result[3+pair] = (BASE[pair*2] if a is None else a) + placeholder + (BASE[pair*2+1] if b is None else b)
    return result


def expected_label(kind, variant):
    p, s = CASES[variant][:2]
    p = BASE[0] if p is None else p
    s = BASE[1] if s is None else s
    if not p: p = '\u00d8' if kind == 3 else 'R' if kind == 4 else ''
    return p + MEASURES[kind] + s


def overrides(record, variant, year):
    apps = [i for i, t in enumerate(record) if t == (1001, 'ACAD')]
    require(len(apps) == 1, 'One ACAD application')
    start = apps[0]+1
    end = next((i for i in range(start, len(record)) if record[i][0] == 1001), len(record))
    wanted = [(1000, 'DSTYLE'), (1002, '{'), (1070, 140), (1040, 0.75)]
    for code, text in expected_pairs(variant).items():
        wanted.extend(((1070, code), (1000, wire(text, year))))
    wanted.append((1002, '}'))
    require([key(t) for t in record[start:end]] == [key(t) for t in wanted], 'Complete affix/scalar packet')
    return [start+i for i in range(len(wanted))]


def check_file(path, year, binary, placement, kind):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Physical transport')
    tags = load(path, year)
    av = tags.index((9, '$ACADVER')); require(tags[av+1] == (1, PROFILES[year]), 'Physical version')
    entries = [tags[a:b] for a, b in records(tags)]
    hosts = [r for r in entries if r[0][1] in ('DIMENSION', 'ARC_DIMENSION', 'LEADER')]
    require(len(hosts) == len(CASES), 'Physical host count')
    require({one(r, 8)[1] for r in hosts} == {f'AFFIX_{v:02d}' for v in range(len(CASES))}, 'Variant identity inventory')
    style, = [r for r in entries if r[0] == (0, 'DIMSTYLE') and (2, 'TEXT_BLOCK_STYLE') in r]
    controls = corrupt(style, {3: 'S:<>:END', 4: 'A:[]:ALT'})
    for host in hosts:
        variant = int(one(host, 8)[1].split('_')[-1])
        require(host[0][1] == ('LEADER' if kind == 8 else 'ARC_DIMENSION' if kind == 7 else 'DIMENSION'), 'Host family')
        for at in overrides(host, variant, year):
            for operation in ('replace', 'remove', 'duplicate', 'wrong-code'):
                bad = list(host); code, value = bad[at]
                if operation == 'replace': bad[at] = (code, value+'_BAD' if isinstance(value, str) else value+1)
                elif operation == 'remove': del bad[at]
                elif operation == 'duplicate': bad.insert(at, bad[at])
                else: bad[at] = (1071, value)
                controls += reject(lambda: overrides(bad, variant, year))
        if kind != 8:
            controls += corrupt(host, {1: '<>', 11: 17.25, 21: 0.0, 31: 0.0, 70: 160 | kind | (64 if kind == 6 else 0)})
            block, = [r for r in entries if r[0] == (0, 'BLOCK_RECORD') and (2, one(host, 2)[1]) in r]
            handle = one(block, 5)[1]
            texts = [r for r in entries if r[0] == (0, 'MTEXT') and (330, handle) in r]
            require(len(texts) == 1, 'One generated label')
            controls += corrupt(texts[0], {1: wire(expected_label(kind, variant), year), 330: handle, 10: 17.25, 20: 0.0, 30: 0.0})
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('AFFIX_PAPER') if placement == 1 else doc.blocks['AFFIX_HOLDER']
    loaded = list(space.query('DIMENSION ARC_DIMENSION LEADER'))
    require(len(loaded) == len(CASES), 'Independent host placement/count')
    style = doc.dimstyles.get('TEXT_BLOCK_STYLE')
    require(style.dxf.dimpost == 'S:<>:END' and style.dxf.dimapost == 'A:[]:ALT', 'Independent base affixes')
    for host in loaded:
        variant = int(host.dxf.layer.split('_')[-1])
        require(host.dxf.owner == space.block_record_handle, 'Independent owner')
        values = {'dimtxt': 0.75}
        values.update({('dimpost' if code == 3 else 'dimapost'): wire(text, year) for code, text in expected_pairs(variant).items()})
        require(host.get_acad_dstyle(style) == values, 'Independent complete pair overrides')
        require([(t.code, t.value) for t in host.get_xdata('AFFIX_KEEP')] == [(1000, 'unchanged')], 'Other application XData')
        if kind != 8:
            require(host.dxf.dimtype == 160 | kind | (64 if kind == 6 else 0), 'Independent dimension family/flags')
            require(tuple(host.dxf.text_midpoint) == (17.25, 0., 0.), 'Independent manual anchor')
            label, = host.get_geometry_block().query('MTEXT')
            require(label.text == wire(expected_label(kind, variant), year), 'Independent generated label')
    if placement == 2:
        insert, = doc.modelspace().query('INSERT'); require(insert.dxf.name == 'AFFIX_HOLDER', 'Referenced block placement')
    if placement == 3:
        require(not any(e.dxftype() == 'INSERT' and e.dxf.name == 'AFFIX_HOLDER' for e in doc.entitydb.values()), 'Unreferenced block unexpectedly instantiated')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following line geometry')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls


def check_style(path, year, binary, mask):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Style transport')
    post = ('P:<>' if mask & 1 else '') + (':S' if mask & 2 else '')
    apost = ('A:[]' if mask & 4 else '') + (':B' if mask & 8 else '')
    tags = load(path, year)
    av = tags.index((9, '$ACADVER')); require(tags[av+1] == (1, PROFILES[year]), 'Style physical version')
    entries = [tags[a:b] for a, b in records(tags)]
    style, = [r for r in entries if r[0] == (0, 'DIMSTYLE') and (2, 'AFFIX_STYLE_HEADER') in r]
    controls = corrupt(style, {3: post, 4: apost})
    for name, value in (('$DIMPOST', post), ('$DIMAPOST', apost)):
        positions = [i for i, t in enumerate(tags) if t == (9, name)]
        require(len(positions) == 1, 'One header variable')
        at = positions[0]+1
        stop = next(i for i in range(at, len(tags)) if tags[i][0] in (0, 9))
        controls += corrupt(tags[at:stop], {1: value})
    doc = ezdxf.readfile(path); style = doc.dimstyles.get('AFFIX_STYLE_HEADER')
    require(doc.dxfversion == PROFILES[year], 'Independent style version')
    require(style.dxf.dimpost == post and style.dxf.dimapost == apost, 'Independent style pairs')
    require(doc.header['$DIMPOST'] == post and doc.header['$DIMAPOST'] == apost, 'Independent active header')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Style graph errors/repairs')
    return controls


def main(directory):
    matrix = [s for s in itertools.product(PROFILES, (False, True), range(4), range(9), ('source', 'False', 'True')) if s[0] != 2000 or s[3] != 7]
    styles = list(itertools.product(PROFILES, (False, True), range(16)))
    def name(s):
        year, binary, placement, kind, output = s
        return f'dimension-affix-AutoCad{year}-{binary}-{placement}-{kind}-{output}.dxf'
    def style_name(s):
        year, binary, mask = s
        return f'dimension-affix-style-AutoCad{year}-{binary}-{mask}.dxf'
    expected = {name(s) for s in matrix} | {style_name(s) for s in styles}
    def inventory(actual): require(actual == expected, f'Affix inventory missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('dimension-affix-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)})); reject(lambda: inventory(expected | {'dimension-affix-extra.dxf'}))
    controls = 0
    for s in matrix:
        year, binary, placement, kind, output = s
        controls += check_file(directory/name(s), year, binary if output == 'source' else output == 'True', placement, kind)
    for s in styles: controls += check_style(directory/style_name(s), *s)
    print(f'PASS: {len(expected)} affix drawings; {len(matrix)*len(CASES)} independently loaded dimension/leader records; '
          f'{controls} actual-packet corruptions and two inventory controls rejected; zero graph errors/repairs. '
          'Alternate-unit storage is checked, not full alternate-unit label rendering or native AutoCAD acceptance.')

if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimension_affix_fidelity.py ARTIFACTS')
    main(Path(sys.argv[1]))
