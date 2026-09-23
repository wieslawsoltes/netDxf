#!/usr/bin/env python3
"""Inspect generated DIMENSION label masks independently from stored DIMTFILLCLR."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject
from verify_dimlfac_fidelity import PROFILES, records

BASE = (None, 2, 2, 2, 2, None, 0, 256, 152, None, 2, 2)
OVERRIDES = {3: 4, 4: None, 5: 3, 10: 256, 11: 0}
MASK_CODES = {90, 45, 63, 421, 431, 441}


def expected_mask(variant, year):
    color = OVERRIDES.get(variant, BASE[variant])
    return color if year >= 2007 else None


def mask_packet(record, expected):
    tags = [t for t in record if t[0] in MASK_CODES]
    wanted = [] if expected is None else [(90, 1), (45, 1.5), (63, expected)]
    require([key(t) for t in tags] == [key(t) for t in wanted], 'Mask presence/order/values or unexpected true color')
    return [i for i, t in enumerate(record) if t[0] in MASK_CODES]


def changed(record, check, positions):
    count = 0
    for at in positions:
        for op in ('change', 'remove', 'duplicate', 'wrong-group'):
            bad = list(record); code, value = bad[at]
            if op == 'change': bad[at] = (code, value + 1)
            elif op == 'remove': del bad[at]
            elif op == 'duplicate': bad.insert(at, bad[at])
            else: bad[at] = (999, value)
            count += reject(lambda: check(bad))
    return count


def one(record, code):
    values = [value for c, value in record if c == code]
    require(len(values) == 1, f'Absent/duplicate field {code}')
    return values[0]


def style_packet(record, variant):
    # DIMSTYLE has its table flags before the optional DIMTFILL fields.
    positions = [i for i, t in enumerate(record) if t[0] == 69]
    color = BASE[variant]
    require(len(positions) == (0 if color is None else 1), 'Stored fill mode presence')
    flags = [t for t in record if t[0] == 70]
    require(flags == ([(70, 0)] if color is None else [(70, 0), (70, color)]), 'Flags/fill color fields')
    if color is None: return []
    at = positions[0]
    require(record[at:at + 2] == [(69, 2), (70, color)], 'Indexed style fill packet')
    return [at, at + 1]


def override_packet(record, variant):
    starts = [i for i, t in enumerate(record) if t == (1001, 'ACAD')]
    wanted = []
    if variant in OVERRIDES:
        color = OVERRIDES[variant]
        if color is not None: wanted += [(1070, 70), (1070, color)]
        wanted += [(1070, 69), (1070, 0 if color is None else 2)]
    if variant in (2, 9): wanted += [(1070, 140), (1040, 1.25)]
    require(len(starts) == (1 if wanted else 0), 'Sparse override application')
    if not wanted: return []
    start = starts[0] + 1
    end = next((i for i in range(start, len(record)) if record[i][0] == 1001), len(record))
    framed = [(1000, 'DSTYLE'), (1002, '{')] + wanted + [(1002, '}')]
    require([key(t) for t in record[start:end]] == [key(t) for t in framed], 'Full override packet')
    return [i for i in range(start, end) if record[i][0] in (1070, 1040)]


def inspect(path, year, binary, kind, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_tags(path)
    at = tags.index((9, '$ACADVER')); require(tags[at + 1] == (1, PROFILES[year]), 'Physical version')
    entries = [tags[a:b] for a, b in records(tags)]
    hosts = [r for r in entries if r[0][1] in ('DIMENSION', 'ARC_DIMENSION')]
    require(len(hosts) == 12 and {one(r, 8) for r in hosts} == {f'FILL_{v:02d}' for v in range(12)}, 'Host inventory')
    controls = 0
    for host in hosts:
        variant = int(one(host, 8).split('_')[1]); expected = expected_mask(variant, year)
        require(host[0][1] == ('ARC_DIMENSION' if kind == 7 else 'DIMENSION'), 'Host family')
        require(one(host, 1) == 'FILL', 'Parent literal changed')
        style, = [r for r in entries if r[0] == (0, 'DIMSTYLE') and (2, f'FILL_STYLE_{variant:02d}') in r]
        controls += changed(style, lambda bad: style_packet(bad, variant), style_packet(style, variant))
        controls += changed(host, lambda bad: override_packet(bad, variant), override_packet(host, variant))
        block, = [r for r in entries if r[0] == (0, 'BLOCK_RECORD') and (2, one(host, 2)) in r]
        labels = [r for r in entries if r[0] == (0, 'MTEXT') and (330, one(block, 5)) in r]
        require(len(labels) == 1, 'Exactly one owned label')
        label = labels[0]; require(one(label, 1) == 'FILL', 'Generated literal changed')
        positions = mask_packet(label, expected)
        controls += changed(label, lambda bad: mask_packet(bad, expected), positions)
        if expected is None:
            controls += reject(lambda: mask_packet(label + [(90, 1), (45, 1.5), (63, 2)], expected))
        else:
            controls += reject(lambda: mask_packet(label + [(421, 0x123456)], expected))
    doc = ezdxf.readfile(path); require(doc.dxfversion == PROFILES[year], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('FILL_PAPER') if placement == 1 else doc.blocks['FILL_HOLDER']
    loaded = list(space.query('DIMENSION ARC_DIMENSION')); require(len(loaded) == 12, 'Independent host placement/count')
    for host in loaded:
        v = int(host.dxf.layer.split('_')[1]); color = expected_mask(v, year)
        require(host.dxf.owner == space.block_record_handle, 'Independent host ownership')
        style = doc.dimstyles.get(host.dxf.dimstyle)
        require(style.dxf.dimtfill == (0 if BASE[v] is None else 2), 'Independent base mode')
        if BASE[v] is not None: require(style.dxf.dimtfillclr == BASE[v], 'Independent base color')
        expected = {}
        if v in OVERRIDES:
            expected['dimtfill'] = 0 if OVERRIDES[v] is None else 2
            if OVERRIDES[v] is not None: expected['dimtfillclr'] = OVERRIDES[v]
        if v in (2, 9): expected['dimtxt'] = 1.25
        require(host.get_acad_dstyle(style) == expected, 'Independent sparse override values')
        block = host.get_geometry_block(); text, = block.query('MTEXT')
        require(text.text == 'FILL' and text.dxf.owner == block.block_record_handle, 'Independent label text/owner')
        require(text.dxf.char_height == (1.25 if v in (2, 9) else .75), 'Unrelated height override')
        for name in ('bg_fill', 'box_fill_scale', 'bg_fill_color'):
            require(text.dxf.hasattr(name) == (color is not None), 'Independent mask field presence')
        if color is not None:
            require(text.dxf.bg_fill == 1 and text.dxf.box_fill_scale == 1.5 and text.dxf.bg_fill_color == color, 'Independent mask values')
        require(not any(text.dxf.hasattr(n) for n in ('bg_fill_true_color', 'bg_fill_color_name', 'bg_fill_transparency')), 'Invented independent mask attributes')
        require([(t.code, t.value) for t in host.get_xdata('FILL_KEEP')] == [(1000, 'unchanged')], 'Unrelated application')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following geometry')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls


def main(directory):
    specs = [s for s in itertools.product(PROFILES, (False, True), range(8), range(3), ('source', 'False', 'True')) if not (s[0] == 2000 and s[2] == 7)]
    def name(s):
        year, binary, kind, placement, output = s
        return f'dimension-text-fill-AutoCad{year}-{binary}-{kind}-{placement}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, f'Fill inventory missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('dimension-text-fill-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)})); reject(lambda: inventory(expected | {'dimension-text-fill-extra.dxf'}))
    controls = 0
    for spec in specs:
        year, binary, kind, placement, output = spec
        controls += inspect(directory / name(spec), year, binary if output == 'source' else output == 'True', kind, placement)
    print(f'PASS: {len(specs)} fill drawings / {12*len(specs)} independent dimension labels; {controls} actual-packet corruptions and two inventory controls rejected; zero graph errors/repairs. Mask fields are not native font/occlusion qualification.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimension_text_fill.py ARTIFACTS')
    main(Path(sys.argv[1]))
