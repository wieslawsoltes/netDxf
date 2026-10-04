#!/usr/bin/env python3
"""Verify actual netDxf AC1032 output against explicit parent/embedded expectations."""
from pathlib import Path
import itertools
import math
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, require, reject, audit_signature

VALUE = 'Embedded αβγ\\PSecond line'
STAGES = ('source', 'output', 'resave')


def records(tags):
    record = []
    for tag in tags:
        if tag[0] == 0:
            if record:
                yield record
            record = [tag]
        elif record:
            record.append(tag)
    if record:
        yield record


def one(tags, code):
    values = [value for key, value in tags if key == code]
    require(len(values) == 1, f'Expected one group {code}, got {values}')
    return values[0]


def near(expected, actual, label):
    if isinstance(expected, (tuple, list)):
        require(len(expected) == len(actual), label + ' cardinality')
        for x, y in zip(expected, actual):
            near(x, y, label)
    else:
        require(math.isfinite(float(actual)) and math.isclose(float(expected), float(actual), rel_tol=1e-12, abs_tol=1e-12), label)


def check_body(body, value, edited):
    expected = {210: 0., 220: 0., 230: 1., 10: 30., 20: 40., 30: 5.,
                40: 2.5, 41: 12., 46: 7., 71: 5, 72: 1, 1: value, 7: 'EMBEDDED_ONLY',
                11: 1., 21: 0., 31: 0., 73: 2, 44: 1.2, 90: 17, 45: 1.5,
                63: 3, 421: 0x2468ac, 441: 0x02000050}
    if not edited:
        expected.update({42: 11., 43: 6.})
    require(len(body) == len(expected), 'Embedded packet inventory')
    for code, expected_value in expected.items():
        actual = one(body, code)
        if isinstance(expected_value, float):
            near(expected_value, actual, f'Embedded group {code}')
        else:
            require(actual == expected_value, f'Embedded group {code}: {actual!r} != {expected_value!r}')


def check_rows(tags, edited=False):
    rows = [row for row in records(tags) if row[0][1] in ('ATTRIB', 'ATTDEF')]
    require(sorted(row[0][1] for row in rows) == ['ATTDEF', 'ATTRIB'], 'Attribute row inventory')
    for row in rows:
        definition = row[0][1] == 'ATTDEF'
        split = [i for i, tag in enumerate(row) if tag[0] == 101]
        require(len(split) == 1 and row[split[0]][1] == 'Embedded Object', 'Embedded object boundary')
        parent, body = row[:split[0]], row[split[0]+1:]
        subtype = 'AcDbAttributeDefinition' if definition else 'AcDbAttribute'
        boundary = [i for i, tag in enumerate(parent) if tag == (100, subtype)]
        require(len(boundary) == 1, 'Parent subclass boundary')
        before, after = parent[:boundary[0]], parent[boundary[0]+1:]
        require(one(before, 1) == ('Fallback definition' if definition else 'Fallback instance'), 'Fallback value')
        near(9. if definition else 6., one(before, 40), 'Fallback height')
        near(.8 if definition else 1.3, one(before, 41), 'Fallback width factor')
        require(one(after, 71) == (4 if definition else 2), 'Parent attribute content type')
        require(one(after, 72) == 0, 'Parent multiline metadata')
        require(one(after, 2) == 'MULTILINE', 'Attribute tag')
        if definition:
            require(one(after, 3) == 'Independent prompt', 'Definition prompt')
            require(one(before, 71) == 6, 'Parent generation flags')
        check_body(body, VALUE if definition else ('Changed multiline\\PText' if edited else VALUE + ' instance'), edited and not definition)


def check_document(doc, edited=False):
    require(doc.dxfversion == 'AC1032', 'AC1032 family')
    roots = list(doc.modelspace().query('INSERT'))
    require(len(roots) == 1 and len(roots[0].attribs) == 1, 'Typed INSERT/ATTRIB inventory')
    definitions = list(doc.blocks.get('MTEXT_ATTRIBUTES').query('ATTDEF'))
    require(len(definitions) == 1, 'Typed ATTDEF inventory')
    for definition, host in ((True, definitions[0]), (False, roots[0].attribs[0])):
        require(host.has_embedded_mtext_entity, 'Missing independently parsed embedded MTEXT')
        require(host.dxf.text == ('Fallback definition' if definition else 'Fallback instance'), 'Typed fallback value')
        text = host.virtual_mtext_entity()
        require(text.text == (VALUE if definition else ('Changed multiline\\PText' if edited else VALUE + ' instance')), 'Typed multiline content')
        near((30., 40., 5.), tuple(text.dxf.insert), 'Embedded WCS insertion')
        near(2.5, text.dxf.char_height, 'Embedded height')
        near(12., text.dxf.width, 'Embedded width')
        require(text.dxf.style == 'EMBEDDED_ONLY', 'Embedded style name')
        require(text.dxf.attachment_point == 5, 'Attachment point')
        require(text.dxf.bg_fill == 17, 'Background flags')
    require(doc.styles.get('EMBEDDED_ONLY').dxf.font == 'txt.shx', 'Embedded style table entry')
    require(not any(any(values.values()) for values in audit_signature(doc)), 'Independent audit errors or repairs')


def main(directory):
    expected = {f'attribute-mtext-{transport}-{stage}.dxf' for transport, stage in itertools.product(('text', 'binary'), STAGES)}
    actual = {p.name for p in directory.glob('attribute-mtext-*.dxf')}
    require(actual == expected, f'Exact attribute fixture inventory: missing={expected-actual}, extra={actual-expected}')
    corruptions = 0
    for name in sorted(expected):
        path = directory / name
        edited = not name.endswith('-source.dxf')
        binary = ('-binary-' in name) != name.endswith('-output.dxf')
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Physical transport')
        tags = load_tags(path)
        check_rows(tags, edited)
        check_document(ezdxf.readfile(path), edited)
        for row in records(tags):
            if row[0][1] not in ('ATTRIB', 'ATTDEF'):
                continue
            split = next(i for i, tag in enumerate(row) if tag[0] == 101)
            definition = row[0][1] == 'ATTDEF'
            body = row[split+1:]
            value = VALUE if definition else ('Changed multiline\\PText' if edited else VALUE + ' instance')
            for i, (code, content) in enumerate(body):
                for mode in ('remove', 'duplicate', 'change'):
                    damaged = list(body)
                    if mode == 'remove': damaged.pop(i)
                    elif mode == 'duplicate': damaged.insert(i, damaged[i])
                    else: damaged[i] = (code, content + '_bad' if isinstance(content, str) else content + 1)
                    corruptions += reject(lambda: check_body(damaged, value, edited and not definition))
    print(f'PASS: {len(expected)} actual C# multiline attribute drawings; parent/embedded separation, '
          f'{corruptions} embedded packet corruptions rejected; zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_attribute_mtext.py ARTIFACTS')
    main(Path(sys.argv[1]))
