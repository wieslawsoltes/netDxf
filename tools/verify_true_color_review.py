#!/usr/bin/env python3
"""Verify ordinary group-420 RGB across writer scopes and target profiles.

Packed AcCmColor and private/opaque slots are distinct and deliberately excluded
from this public RGB contract. R2000 generated output uses its ACI fallback.
"""
import argparse
import copy
from collections import Counter
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records
from verify_legacy_utilities import PROFILES
from verify_mleader_inputs import check

RGB = 0x12AB34
SCOPES = {'LAYER':1,'LINE':1,'ATTDEF':1,'ATTRIB':1,'INSERT':1,'POLYLINE':1,'VERTEX':4,'MLINE':1,'MLINESTYLE':3}


def selected(items):
    result = []
    for key,tags in items.items():
        kind = tags[0][1]
        if kind not in SCOPES: continue
        if kind == 'LAYER' and [2,'REVIEW_RGB'] not in tags: continue
        if kind == 'MLINESTYLE' and [2,'REVIEW_RGB_MLINE'] not in tags: continue
        result.append((key,tags))
    return result


def verify(items,profile):
    found = selected(items)
    counts = Counter()
    for _,tags in found:
        kind = tags[0][1]
        aci = [v for c,v in tags if c == 62]
        check(len(aci) == (3 if kind == 'MLINESTYLE' else 1),'Missing/repeated fallback color field')
        check(all(type(v) is int and 1 <= abs(v) <= 255 for v in aci),'Invalid ACI fallback')
        colors = [v for c,v in tags if c == 420]
        if profile == 'AC1015': check(not colors,'R2000 generated unsupported RGB')
        else:
            check(len(colors) == len(aci) and all(type(v) is int and v == RGB for v in colors),'Missing/repeated/tagged or incorrect group-420 RGB')
        counts[kind] += len(aci)
    check(dict(counts) == SCOPES,'Generated RGB scope inventory changed')
    total = [v for tags in items.values() for c,v in tags if c == 420]
    check(len(total) == (0 if profile == 'AC1015' else sum(SCOPES.values())),'RGB data appeared outside selected writer scopes')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory',type=Path)
    directory = parser.parse_args().directory
    wanted = {f'true-color-review-{version}-{binary}.dxf' for version in PROFILES for binary in (False,True)}
    check({p.name for p in directory.glob('true-color-review-*.dxf')} == wanted,'Exact color fixture inventory required')
    controls = 0
    for version,profile in PROFILES.items():
        for binary in (False,True):
            path = directory/f'true-color-review-{version}-{binary}.dxf'
            check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary,'RGB transport changed')
            doc = ezdxf.readfile(path); check(doc.dxfversion == profile,'RGB source profile changed')
            items = records(path); verify(items,profile)
            def rejects(candidate):
                nonlocal controls
                try: verify(candidate,profile)
                except ValueError: controls += 1
                else: raise AssertionError('Color corruption escaped the same verifier')
            for key,tags in selected(items):
                for index,(code,value) in enumerate(tags):
                    if code != 420: continue
                    for bad in (value ^ 1, value | 0xC2000000, -1):
                        candidate = dict(items); candidate[key] = copy.deepcopy(tags)
                        candidate[key][index] = [code,bad]; rejects(candidate)
                    candidate = dict(items); candidate[key] = tags[:index]+tags[index+1:]; rejects(candidate)
                candidate = dict(items); candidate[key] = tags+[[420,RGB]]; rejects(candidate)
                candidate = dict(items); del candidate[key]; rejects(candidate)
            audit = doc.audit(); check(not audit.errors and not audit.fixes,'RGB output needs graph repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: 12 RGB drawings, 9 writer record kinds, {controls} field/inventory corruptions rejected')


if __name__ == '__main__':
    main()
