#!/usr/bin/env python3
"""Independently check measured literal-table display geometry and resource use.

The explicit blueprint is a 456-unit three-column grid with a full-width first
row. It is not a native AutoCAD layout oracle; height metrics are test inputs.
"""
from __future__ import annotations
import argparse
import copy
from collections import Counter
from pathlib import Path
import ezdxf
from verify_mleader_inputs import check

MODES = ('fixed', 'grow', 'fill', 'double', 'hidden', 'literal')
ANCHORS = ((0, 0), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2))


def point(value):
    return tuple(round(float(v), 10) for v in value)


def snapshot(block):
    result = {'lines': [], 'fills': [], 'text': []}
    for entity in block:
        kind = entity.dxftype()
        if kind == 'LINE':
            result['lines'].append((point(entity.dxf.start), point(entity.dxf.end), entity.dxf.color, entity.dxf.linetype, entity.dxf.lineweight))
        elif kind == 'SOLID':
            result['fills'].append((tuple(point(getattr(entity.dxf, f'vtx{i}')) for i in range(4)), entity.dxf.color))
        elif kind == 'MTEXT':
            result['text'].append((entity.text, point(entity.dxf.insert), entity.dxf.char_height, entity.dxf.width,
                                   entity.dxf.attachment_point, entity.dxf.color, entity.dxf.style))
        else:
            raise ValueError('Unexpected generated entity: ' + kind)
    return result


def blueprint(mode):
    ys = (0, 24, 48, 72) if mode == 'grow' else (0, 11, 20, 29)
    xs = (0, 152, 304, 456)
    result = {'lines': [], 'fills': [], 'text': []}
    if mode != 'hidden':
        # Explicit 12 horizontal and 10 vertical elementary segments; two
        # vertical segments in the full-width first-row merge must be absent.
        for row in range(4):
            for column in range(3):
                for shift in (-.1, .1) if mode == 'double' else (0,):
                    result['lines'].append((point((xs[column], -ys[row] + shift, 0)), point((xs[column + 1], -ys[row] + shift, 0)), 7, 'Continuous', -1))
        for row in range(3):
            for column in range(4):
                if row == 0 and column in (1, 2):
                    continue
                for shift in (-.1, .1) if mode == 'double' else (0,):
                    result['lines'].append((point((xs[column] + shift, -ys[row], 0)), point((xs[column] + shift, -ys[row + 1], 0)), 7, 'Continuous', -1))
    for row, column in ANCHORS:
        address = chr(65 + column) + str(row + 1)
        text = '\\{\\\\C1;literal\\}\\P' + address if mode == 'literal' else address
        width = 456 if row == 0 else 152
        result['text'].append((text, point((xs[column] + 2, -ys[row] - 1, 0)), 2.0, width - 6.0, 1, 7, 'Standard'))
        if mode == 'fill':
            vertices = ((xs[column], -ys[row], 0), (xs[column] + width, -ys[row], 0),
                        (xs[column], -ys[row + 1], 0), (xs[column] + width, -ys[row + 1], 0))
            result['fills'].append((tuple(point(v) for v in vertices), 3))
    return result


def compare(actual, mode):
    expected = blueprint(mode)
    check(set(actual) == set(expected), 'Generated entity kind inventory changed')
    for kind in expected:
        check(Counter(actual[kind]) == Counter(expected[kind]), 'Generated ' + kind + ' geometry, count, style or literal text differs')


def negative_controls(actual, mode):
    controls = 0
    for kind, items in actual.items():
        for index, item in enumerate(items):
            for field in range(len(item)):
                corrupt = copy.deepcopy(actual)
                changed = list(item)
                changed[field] = ('CORRUPT',)
                corrupt[kind][index] = tuple(changed)
                try:
                    compare(corrupt, mode)
                except ValueError:
                    controls += 1
                else:
                    raise AssertionError('Corrupted display field escaped comparator')
        corrupt = copy.deepcopy(actual)
        corrupt[kind].append(('EXTRA',))
        try:
            compare(corrupt, mode)
        except ValueError:
            controls += 1
        else:
            raise AssertionError('Extra entity escaped comparator')
    return controls


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    expected = {f'table-layout-{mode}-{binary}.dxf' for mode in MODES for binary in (False, True)}
    check({p.name for p in args.directory.glob('table-layout-*.dxf')} == expected, 'Exact layout fixture inventory required')
    controls = 0
    for mode in MODES:
        for binary in (False, True):
            path = args.directory / f'table-layout-{mode}-{binary}.dxf'
            check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Display transport changed')
            doc = ezdxf.readfile(path)
            check(doc.dxfversion == 'AC1015', 'Portable display profile changed')
            audit = doc.audit()
            check(not audit.errors and not audit.fixes, 'Display drawing needed audit repairs')
            model = list(doc.modelspace())
            check(len(model) == 1 and model[0].dxftype() == 'INSERT' and model[0].dxf.name == 'GENERATED_TABLE', 'Expected one explicit display insert')
            check(point(model[0].dxf.insert) == (0., 0., 0.), 'Display insertion moved')
            actual = snapshot(doc.blocks['GENERATED_TABLE'])
            compare(actual, mode)
            controls += negative_controls(actual, mode)
            print('PASS ' + path.name)
    print(f'PASS: 12 generated table displays and {controls} parsed-geometry corruption controls; no native-font or source-TABLE-cache claim')


if __name__ == '__main__':
    main()
