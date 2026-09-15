#!/usr/bin/env python3
"""Verify explicit TABLEGEOMETRY packet edits without claiming regenerated layout."""
import argparse
import copy
import json
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    sys.path.insert(0, str(args.repository / 'tools'))
    from verify_fourth_mixed_modules import load, metadata, dictionary_edges
    from verify_stored_table_content import FILES, source, payload, exact, check, verify_extraction, plain_records, verify_carrier_graph

    def geometries(records):
        return {h: row for h, row in records.items() if row[0] == (0, 'TABLEGEOMETRY')}

    def body(row):
        return row[next(i for i, tag in enumerate(row) if tag[0] == 100):]

    def transformed(row):
        result = []
        for code, value in body(row):
            if code in (90, 91, 92):
                value += {90: 7, 91: 11, 92: 1}[code]
            elif code in (93, 95):
                value ^= {93: 128, 95: 0x40000000}[code]
            elif code in (40, 41, 43, 44, 45, 46):
                value += {40: 0.5, 41: -0.25, 43: 0.125, 44: -0.125, 45: 0.75, 46: -0.75}[code]
            elif code == 10:
                value = tuple(a + b for a, b in zip(value, (1.0, 2.0, 3.0)))
            elif code == 11:
                value = tuple(a - b for a, b in zip(value, (3.0, 2.0, 1.0)))
            result.append((code, value))
        return result + [(93, -2147483648), (40, -1.0), (41, 2.0), (330, '0'), (94, 0)]

    def expected_payload(tags):
        return [(code, exact(value)) for code, value in tags]

    def verify_class(classes, count):
        rows = [row for row in classes if (1, 'TABLEGEOMETRY') in row]
        check(len(rows) == 1, 'Missing or duplicate TABLEGEOMETRY CLASS')
        check(all(tag in rows[0] for tag in [(2, 'AcDbTableGeometry'), (3, 'ObjectDBX Classes'), (90, 1152), (91, count), (280, 0), (281, 0)]), 'TABLEGEOMETRY CLASS differs')

    def verify_native(records, classes, original, edited):
        before = geometries(original)
        check(set(geometries(records)) == set(before), 'Native geometry identity inventory changed')
        for handle, row in before.items():
            expected = expected_payload(transformed(row)) if edited else payload(row)
            check(payload(records[handle]) == expected, 'Explicit native geometry packet differs')
            check(metadata(records[handle]) == metadata(row), 'Native geometry metadata changed')
            owner = metadata(row)[0]
            check(owner in records and records[owner][0] == (0, 'XRECORD'), 'Native owning wrapper identity changed')
            check(payload(records[owner]) == payload(original[owner]), 'Native wrapper payload changed')
        for handle, row in original.items():
            if row[0][1] in ('TABLECONTENT', 'TABLESTYLE', 'CELLSTYLEMAP'):
                check(handle in records and records[handle][0] == row[0], 'Unrelated native stored identity changed')
                check(payload(records[handle]) == payload(row), 'Unrelated native stored packet changed')
        verify_class(classes, len(before))

    def content_cell(target='0'):
        return [(93, -2147483648), (40, -0.0), (41, -20.0), (330, target), (94, 1),
                (10, (-1.0, 2.0, 3.0)), (11, (4.0, 5.0, 6.0)),
                (43, -7.0), (44, 8.0), (45, 9.0), (46, -10.0), (95, -2147483648)]

    def verify_synthetic(records, classes, kind=None):
        rows = geometries(records)
        check(len(rows) == 1, 'Synthetic geometry inventory changed')
        handle, row = next(iter(rows.items()))
        owner = metadata(row)[0]
        check(owner in records and ('GEOMETRY', 360, handle) in dictionary_edges(records[owner]), 'Synthetic geometry owner edge changed')
        if kind is None:
            expected = [(100, 'AcDbTableGeometry'), (90, 777), (91, 888), (92, 3)] + content_cell()
            expected += [(93, 2147483647), (40, -1.5), (41, 2.5), (330, '0'), (94, 0)] + content_cell()
        else:
            target = next(value for code, value in body(row) if code == 330)
            target_type = {'STYLE': 'STYLE', 'LTYPE': 'LTYPE', 'APPID': 'APPID', 'ENTITY': 'LINE',
                           'BLOCK_RECORD': 'BLOCK_RECORD', 'BLOCK_MEMBER': 'LINE', 'ATTRIB': 'ATTRIB', 'ENDBLK': 'ENDBLK'}[kind]
            check(target in records and records[target][0] == (0, target_type), 'Explicit target physical identity changed')
            if kind in ('STYLE', 'LTYPE', 'APPID', 'BLOCK_RECORD'):
                check((2, 'EDIT_TARGET') in records[target], 'Explicit resource identity/name changed')
            expected = [(100, 'AcDbTableGeometry'), (90, 1), (91, 1), (92, 2)] + content_cell(target) * 2
        check(payload(row) == expected_payload(expected), 'Explicit synthetic geometry fields or order changed')
        verify_class(classes, 1)

    kinds = ['STYLE', 'LTYPE', 'APPID', 'ENTITY', 'BLOCK_RECORD', 'BLOCK_MEMBER', 'ATTRIB', 'ENDBLK']
    expected = {f'editable-table-geometry-{mode}-{file}-{binary}.dxf' for mode in ('native', 'noop') for file in FILES for binary in (False, True)}
    expected |= {f'editable-table-geometry-schema-AutoCad{year}-{binary}.dxf' for year in (2004, 2007, 2010, 2013, 2018) for binary in (False, True)}
    expected |= {f'editable-table-geometry-reference-{kind}-{binary}.dxf' for kind in kinds for binary in (False, True)}
    check({path.name for path in args.directory.glob('editable-table-geometry-*.dxf')} == expected, 'All 46 editable geometry outputs are mandatory')
    manifest = json.loads((args.repository / 'tests/fixtures/table-content/manifest.json').read_text())
    check(sum(verify_extraction(args.repository, row) for row in manifest['files']) == 316, 'Native carrier inventory changed')
    controls = packets = appearances = 0

    def rejects(action):
        nonlocal controls
        try:
            action()
        except (ValueError, KeyError, StopIteration):
            controls += 1
        else:
            raise ValueError('Edited geometry output corruption accepted')

    def read(name, year, binary):
        doc, records, classes = load(args.directory / name, year, binary)
        audit = doc.audit()
        check(not audit.errors and not audit.fixes, 'Edited geometry output audit failed')
        return records, classes

    for file in FILES:
        original, _, year = source(args.repository, file)
        entry = next(row for row in manifest['files'] if row['file'] == file)
        selected = entry.get('records', [])
        carrier = plain_records((args.repository / 'tests/fixtures/table-content' / entry['fixture']).read_bytes(), entry['profile']) if selected else {}
        for mode in ('native', 'noop'):
            for binary in (False, True):
                name = f'editable-table-geometry-{mode}-{file}-{binary}.dxf'
                records, classes = read(name, year, binary)
                verify_native(records, classes, original, mode == 'native')
                verify_carrier_graph(records, carrier, selected)
                packets += len(geometries(original)); appearances += len(selected)
                for fault in range(8):
                    bad = copy.deepcopy(records); definitions = copy.deepcopy(classes)
                    row = next(iter(geometries(bad).values()))
                    if fault < 6:
                        code = (90, 91, 92, 93, 40, 41)[fault]
                        index = next(i for i, tag in enumerate(row) if tag[0] == code)
                        row[index] = (code, 123456.5 if code in (40, 41) else 123456)
                    elif fault == 6:
                        bad.pop(metadata(row)[0])
                    else:
                        definitions = []
                    rejects(lambda: verify_native(bad, definitions, original, mode == 'native'))
                print('PASS ' + name)
    for year in (2004, 2007, 2010, 2013, 2018):
        for binary in (False, True):
            name = f'editable-table-geometry-schema-AutoCad{year}-{binary}.dxf'
            records, classes = read(name, year, binary); verify_synthetic(records, classes)
            for code in (90, 91, 92, 93, 10, 11, 43, 95):
                bad = copy.deepcopy(records); row = next(iter(geometries(bad).values()))
                index = next(i for i, tag in enumerate(row) if tag[0] == code)
                row[index] = (code, (99.0, 98.0, 97.0) if code in (10, 11) else 999)
                rejects(lambda: verify_synthetic(bad, classes))
            print('PASS ' + name)
    for kind in kinds:
        for binary in (False, True):
            name = f'editable-table-geometry-reference-{kind}-{binary}.dxf'
            records, classes = read(name, 2018, binary); verify_synthetic(records, classes, kind)
            for fault in range(8):
                bad = copy.deepcopy(records); definitions = copy.deepcopy(classes)
                row = next(iter(geometries(bad).values())); target = next(v for c, v in body(row) if c == 330)
                if fault < 5:
                    code = (90, 91, 92, 40, 95)[fault]; index = next(i for i, tag in enumerate(row) if tag[0] == code); row[index] = (code, 123456)
                elif fault == 5:
                    bad.pop(target)
                elif fault == 6:
                    bad[target][0] = (0, 'PRIVATE_WRONG_TARGET')
                else:
                    definitions = []
                rejects(lambda: verify_synthetic(bad, definitions, kind))
            print('PASS ' + name)
    check(controls == 368 and packets == 32 and appearances == 1264, 'Editable geometry verification inventory changed')
    print('PASS 46 outputs, 32 native geometry packet appearances, 1264 carrier record appearances, 368 actual corruptions rejected; zero audit errors or repairs')


if __name__ == '__main__':
    main()
