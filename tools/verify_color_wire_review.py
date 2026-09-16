#!/usr/bin/env python3
"""Verify standard authored RGB wire integers and R2000 indexed fallback.

Checks each of the authored writer paths independently of the packed AciColor
helper. Retained opaque/private color packets are outside this authored gate.
"""
import argparse
import copy
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records
from verify_legacy_utilities import PROFILES
from verify_mleader_inputs import check

RGB = 0x12AB34
COUNTS = {'LAYER': 1, 'LINE': 1, 'ATTDEF': 1, 'ATTRIB': 1, 'POLYLINE': 1,
          'VERTEX': 4, 'MLINESTYLE': 2, 'HATCH': 2}


def verify(items, modern):
    counts = {kind: 0 for kind in COUNTS}
    selected = {kind: [] for kind in COUNTS}
    rgb_positions = []
    for handle, tags in items.items():
        kind = tags[0][1]
        for index, (code, value) in enumerate(tags):
            if code in (420, 421):
                check(modern and kind in counts and value == RGB, 'Wrong authored 24-bit RGB or profile')
                counts[kind] += 1
                rgb_positions.append((handle, index))
            if code == 62 and value == 102 and kind in selected:
                selected[kind].append((handle, index))
    if modern:
        check(counts == COUNTS, 'RGB writer-path inventory changed')
    else:
        check(not rgb_positions, 'R2000 gained authored RGB extension fields')
    for kind, count in COUNTS.items():
        if kind == 'HATCH':
            continue  # Gradient fallback colors use group 63, not common ACI 62.
        check(len(selected[kind]) == count, 'Declared nearest-palette fallback changed for ' + kind)
    return rgb_positions + [position for positions in selected.values() for position in positions]


def challenge(items, modern):
    positions = verify(items, modern)
    negative = 0
    for handle, index in positions:
        changed = dict(items)
        changed[handle] = copy.deepcopy(items[handle])
        changed[handle][index][1] += 1
        try:
            verify(changed, modern)
        except ValueError:
            negative += 1
        else:
            raise AssertionError('RGB/ACI corruption escaped its positive validator')
    handle = next(h for h, tags in items.items() if tags[0] == [0, 'LINE'])
    changed = dict(items)
    changed[handle] = items[handle] + [[420, RGB]]
    try:
        verify(changed, modern)
    except ValueError:
        negative += 1
    else:
        raise AssertionError('Extra RGB tag escaped its positive validator')
    return negative


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    expected = {f'color-wire-review-{version}-{binary}.dxf' for version in PROFILES for binary in (False, True)}
    check({p.name for p in directory.glob('color-wire-review-*.dxf')} == expected, 'Exact color-wire inventory required')
    negative = 0
    for version, profile in PROFILES.items():
        for binary in (False, True):
            path = directory / f'color-wire-review-{version}-{binary}.dxf'
            check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'RGB transport changed')
            negative += challenge(records(path), version != 'AutoCad2000')
            doc = ezdxf.readfile(path)
            check(doc.dxfversion == profile, 'RGB source profile changed')
            audit = doc.audit()
            check(not audit.errors and not audit.fixes, 'RGB drawing needs graph repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: 12 RGB/profile drawings, 8 authored writer paths; {negative} corruptions rejected; opaque packets are separate')


if __name__ == '__main__':
    main()
