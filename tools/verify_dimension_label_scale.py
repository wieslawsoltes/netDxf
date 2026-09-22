#!/usr/bin/env python3
"""Check dimension label scaling/rounding, affixes, overrides and owner contexts."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import key, load_tags, require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one, corrupt


def label(variant, placement, culture):
    # The fixture measures 10.125. Scaling precedes rounding to half a unit.
    # Detached block definitions do not infer paper space from an INSERT instance.
    text = '10.000' if variant & 2 and placement != 1 else '30.500'
    if variant & 1 and culture == 1: text = text[:-1].replace('.', ',')
    return ('O:' if variant & 4 else 'S:') + text + ':END'


def override_packet(parent, variant):
    apps = [i for i, t in enumerate(parent) if t == (1001, 'ACAD')]
    require(len(apps) == (1 if variant & 4 else 0), 'Override application presence')
    if not apps: return []
    wanted = [(1000, 'DSTYLE'), (1002, '{'), (1070, 144),
              (1040, -3. if variant & 2 else 3.), (1070, 3), (1000, 'O:<>:END'), (1002, '}')]
    start = apps[0]+1
    require([key(t) for t in parent[start:]] == [key(t) for t in wanted], 'Combined scale/DIMPOST overrides')
    return [start+3, start+5]


def inspect(path, year, binary, placement, culture):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_tags(path)
    av = tags.index((9, '$ACADVER')); require(tags[av+1] == (1, PROFILES[year]), 'Physical version')
    content = [tags[a:b] for a, b in records(tags)]
    parents = [r for r in content if r[0] == (0, 'DIMENSION')]
    require(len(parents) == 8 and {one(r, 8)[1] for r in parents} == {f'LABEL_SCALE_{v}' for v in range(8)}, 'Physical dimension inventory')
    corruptions = 0
    for parent in parents:
        v = int(one(parent, 8)[1].split('_')[-1]); name = f'LABEL_SCALE_{v}'
        style, = [r for r in content if r[0] == (0, 'DIMSTYLE') and (2, name) in r]
        scale = 9. if v & 4 else -3. if v & 2 else 3.
        corruptions += corrupt(style, {144: scale, 3: 'S:<>:END', 45: 0.5, 271: 3, 277: 6 if v & 1 else 2, 278: 46})
        corruptions += corrupt(parent, {3: name, 70: 33, 1: '<>', 13: 0., 23: 0., 33: 0., 14: 10.125, 24: 0., 34: 0.})
        flags = [t for t in parent if t[0] == 67]
        require(flags == ([(67, 1 if placement == 1 else 0)] if placement < 2 else []), "Physical paper-space flag")
        for at in override_packet(parent, v):
            for op in ('change', 'remove', 'duplicate', 'wrong-group'):
                bad = list(parent); code, value = bad[at]
                if op == 'change': bad[at] = (code, value + '_BAD' if isinstance(value, str) else value+1.)
                elif op == 'remove': del bad[at]
                elif op == 'duplicate': bad.insert(at, bad[at])
                else: bad[at] = (1071, value)
                corruptions += reject(lambda: override_packet(bad, v))
        block, = [r for r in content if r[0] == (0, 'BLOCK_RECORD') and (2, one(parent, 2)[1]) in r]
        handle = one(block, 5)[1]
        texts = [r for r in content if r[0] == (0, 'MTEXT') and (330, handle) in r]
        def inventory(actual): require(len(actual) == 1, 'Label count')
        inventory(texts)
        corruptions += reject(lambda: inventory([]))
        corruptions += reject(lambda: inventory(texts+texts))
        corruptions += corrupt(texts[0], {1: label(v, placement, culture), 330: handle, 10: 5.0625, 20: 3.09, 30: 0.})
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent profile')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('SCALE_PAPER') if placement == 1 else doc.blocks['SCALE_HOLDER']
    dimensions = list(space.query('DIMENSION')); require(len(dimensions) == 8, 'Independent dimension placement/count')
    for dim in dimensions:
        v = int(dim.dxf.layer.split('_')[-1]); style = doc.dimstyles.get(dim.dxf.dimstyle)
        require(dim.dxf.owner == space.block_record_handle, 'Independent dimension owner')
        require(tuple(dim.dxf.defpoint2) == (0., 0., 0.) and tuple(dim.dxf.defpoint3) == (10.125, 0., 0.), 'Measured geometry changed')
        require(style.dxf.dimlfac == (9. if v & 4 else -3. if v & 2 else 3.), 'Independent stored scale')
        require(style.dxf.dimrnd == 0.5 and style.dxf.dimpost == 'S:<>:END' and style.dxf.dimlunit == (6 if v & 1 else 2), 'Independent style settings')
        expected = {'dimlfac': -3. if v & 2 else 3., 'dimpost': 'O:<>:END'} if v & 4 else {}
        require(dim.get_acad_dstyle(style) == expected, 'Independent complete overrides')
        block = dim.get_geometry_block(); text, = block.query('MTEXT')
        require(text.text == label(v, placement, culture), 'Independent scaled/rounded/affixed label')
        require(text.dxf.owner == block.block_record_handle and tuple(text.dxf.insert) == (5.0625, 3.09, 0.), 'Independent label anchor/owner')
    if placement >= 2:
        inserts = list((doc.modelspace() if placement == 2 else doc.layouts.get('SCALE_PAPER')).query('INSERT'))
        require(len(inserts) == 1 and inserts[0].dxf.name == 'SCALE_HOLDER', 'Independent block instance placement')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return corruptions


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), range(4), range(2), ('source', 'False', 'True')))
    def name(s):
        year, binary, placement, culture, output = s
        return f'dimension-label-scale-AutoCad{year}-{binary}-{placement}-{culture}-{output}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, 'Missing/extra scale fixtures')
    inventory({p.name for p in directory.glob('dimension-label-scale-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'dimension-label-scale-extra.dxf'}))
    corruptions = 0
    for spec in specs:
        year, binary, placement, culture, output = spec
        corruptions += inspect(directory/name(spec), year, binary if output == 'source' else output == 'True', placement, culture)
    print(f'PASS: {len(specs)} scale/affix drawings / {8*len(specs)} independent dimension records; '
          f'{corruptions} packet/label corruptions and two inventory controls rejected; zero graph errors/repairs. '
          'Stored labels and explicit owner-context policy do not certify native instance-dependent rendering.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimension_label_scale.py ARTIFACTS')
    main(Path(sys.argv[1]))
