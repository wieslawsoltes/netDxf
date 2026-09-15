#!/usr/bin/env python3
"""Verify exact stored CELLSTYLEMAP packets, native ownership and raw entry fields."""
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
    from verify_stored_table_content import FILES, source, payload, check, verify_extraction, plain_records, verify_carrier_graph, symbol_name

    def maps(records):
        return {h: r for h, r in records.items() if r[0] == (0, 'CELLSTYLEMAP')}

    def verify_native(records, classes, original):
        expected = maps(original)
        check(set(maps(records)) == set(expected), 'Native CELLSTYLEMAP identity inventory changed')
        for handle, before in expected.items():
            row = records[handle]
            check(payload(row) == payload(before), 'Exact native map payload changed')
            owner = metadata(before)[0]
            check(metadata(row) == metadata(before), 'Native map metadata changed')
            check(owner in records and records[owner][0] == (0, 'DICTIONARY'), 'Native extension dictionary identity changed')
            check(('ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP', 360, handle) in dictionary_edges(records[owner]), 'Native map ownership edge changed')
            style = metadata(original[owner])[0]
            check(metadata(records[owner])[0] == style and style in records and records[style][0] == (0, 'TABLESTYLE'), 'Native TABLESTYLE owner changed')
            check(metadata(records[style])[2] == owner, 'Native TABLESTYLE extension identity changed')
            for code, target in before:
                if code == 340 and int(target, 16):
                    check(target in records and records[target][0] == original[target][0], 'Native formatting dependency identity changed')
                    check(symbol_name(records[target]) == symbol_name(original[target]), 'Native formatting resource name changed')
        definitions = [r for r in classes if (1, 'CELLSTYLEMAP') in r]
        check(len(definitions) == 1, 'Missing CELLSTYLEMAP class')
        check(all(t in definitions[0] for t in [(2, 'AcDbCellStyleMap'), (3, 'ObjectDBX Classes'), (90, 1152), (91, len(expected)), (280, 0), (281, 0)]), 'Map CLASS metadata changed')

    def synthetic():
        result = [(100, 'AcDbCellStyleMap'), (90, 2)]
        for stored_type in (-2147483648, 2147483647):
            result += [(300, 'CELLSTYLE'), (1, 'TABLEFORMAT_BEGIN'), (90, 5), (170, 0), (309, 'TABLEFORMAT_END'),
                       (1, 'CELLSTYLE_BEGIN'), (90, -7), (91, stored_type), (300, 'Custom'), (309, 'CELLSTYLE_END')]
        return result

    def owner_before_subclass(row):
        depth = 0
        owner = None
        for code, value in row:
            if code == 100:
                break
            if code == 102:
                depth += 1 if value.startswith('{') else -1
            elif code == 330 and depth == 0:
                owner = value
        return owner

    def verify_schema(records, classes):
        rows = maps(records)
        check(len(rows) == 1, 'Synthetic map inventory changed')
        handle, row = next(iter(rows.items()))
        start = row.index((100, 'AcDbCellStyleMap'))
        check(row[start:] == synthetic(), 'Stored entry fields, order or format packet changed')
        owner = owner_before_subclass(row)
        check(owner in records and ('MAP', 360, handle) in dictionary_edges(records[owner]), 'Synthetic owner identity changed')
        definitions = [r for r in classes if (1, 'CELLSTYLEMAP') in r]
        check(len(definitions) == 1 and (90, 1152) in definitions[0] and (91, 1) in definitions[0], 'Synthetic CLASS changed')

    def verify_opaque(records, variant):
        rows = maps(records)
        check(len(rows) == 1, 'Opaque map inventory changed')
        handle, row = next(iter(rows.items()))
        body = synthetic()
        prefix = []
        if variant == 1:
            body[0] = (100, 'PrivateCellStyleMap')
        if variant == 2:
            body.append((100, 'PrivateMapExtension'))
        if variant == 3:
            body[3] = (1, 'PRIVATEFORMAT_BEGIN')
        if variant == 4:
            body[5:5] = [(102, '{PRIVATE'), (1000, 'private value'), (102, '}')]
        if variant == 5:
            body += [(100, 'PrivateMapExtension'), (1000, 'private value')]
        if variant == 6:
            prefix = [(102, '{PRIVATE'), (1000, 'private header'), (102, '}')]
        if variant == 7:
            body[6] = (309, 'PRIVATEFORMAT_END')
        start = next(i for i, t in enumerate(row) if t[0] == 100)
        check([t for t in row[:start] if t[0] not in (0, 5, 330)] == prefix and row[start:] == body, 'Opaque map packet changed')
        owner = owner_before_subclass(row)
        check(owner in records and ('MAP', 360, handle) in dictionary_edges(records[owner]), 'Opaque owning dictionary changed')

    expected = {f'cell-style-map-native-{f}-{b}.dxf' for f in FILES for b in (False, True)}
    expected |= {f'cell-style-map-schema-AutoCad{year}-{b}.dxf' for year in (2004, 2007, 2010, 2013, 2018) for b in (False, True)}
    expected |= {f'cell-style-map-opaque-{v}-{b}.dxf' for v in range(8) for b in (False, True)}
    check({p.name for p in args.directory.glob('cell-style-map-*.dxf')} == expected, 'All 36 map outputs are mandatory')
    manifest = json.loads((args.repository / 'tests/fixtures/table-content/manifest.json').read_text())
    selected = sum(verify_extraction(args.repository, row) for row in manifest['files'])
    check(selected == 316, 'Source carrier inventory changed')
    controls = 0
    packets = 0
    selected_appearances = 0

    def rejects(action, message):
        nonlocal controls
        try:
            action()
        except (ValueError, KeyError, StopIteration):
            controls += 1
        else:
            raise ValueError(message)

    for file in FILES:
        original, _, year = source(args.repository, file)
        entry = next(r for r in manifest['files'] if r['file'] == file)
        selected_rows = entry.get('records', [])
        carrier = plain_records((args.repository / 'tests/fixtures/table-content' / entry['fixture']).read_bytes(), entry['profile']) if selected_rows else {}
        for binary in (False, True):
            name = f'cell-style-map-native-{file}-{binary}.dxf'
            doc, records, classes = load(args.directory / name, year, binary)
            audit = doc.audit()
            check(not audit.errors and not audit.fixes, 'Native map output audit failed')
            verify_native(records, classes, original)
            verify_carrier_graph(records, carrier, selected_rows)
            selected_appearances += len(selected_rows)
            packets += len(maps(original))
            for fault in range(10):
                bad = copy.deepcopy(records)
                bc = copy.deepcopy(classes)
                handle = next(iter(maps(bad)))
                row = bad[handle]
                owner = metadata(row)[0]
                if fault < 6:
                    code = (90, 91, 300, 1, 309, 340)[fault]
                    index = next(i for i, t in enumerate(row) if t[0] == code)
                    row[index] = (code, 9876 if code in (90, 91) else 'BROKEN')
                elif fault == 6:
                    bad.pop(owner)
                elif fault == 7:
                    index = next(i for i, t in enumerate(bad[owner]) if t == (360, handle))
                    bad[owner][index] = (360, '0')
                elif fault == 8:
                    target = next(v for c, v in row if c == 340 and int(v, 16))
                    bad.pop(target)
                else:
                    bc = []
                rejects(lambda: verify_native(bad, bc, original), 'Native map corruption accepted')
            print('PASS ' + name)
    for year in (2004, 2007, 2010, 2013, 2018):
        for binary in (False, True):
            name = f'cell-style-map-schema-AutoCad{year}-{binary}.dxf'
            doc, records, classes = load(args.directory / name, year, binary)
            audit = doc.audit()
            check(not audit.errors and not audit.fixes, 'Synthetic map output audit failed')
            verify_schema(records, classes)
            for offset in (1, 2, 3, 4, 6, 8, 9, 10):
                bad = copy.deepcopy(records)
                row = next(iter(maps(bad).values()))
                start = row.index((100, 'AcDbCellStyleMap'))
                code, value = row[start + offset]
                row[start + offset] = (code, 999 if isinstance(value, int) else 'BROKEN')
                rejects(lambda: verify_schema(bad, classes), 'Synthetic map corruption accepted')
            print('PASS ' + name)
    for variant in range(8):
        for binary in (False, True):
            name = f'cell-style-map-opaque-{variant}-{binary}.dxf'
            doc, records, _ = load(args.directory / name, 2000 if variant == 0 else 2018, binary)
            audit = doc.audit()
            check(not audit.errors and not audit.fixes, 'Opaque map output audit failed')
            verify_opaque(records, variant)
            for code in (91, 300):
                bad = copy.deepcopy(records)
                row = next(iter(maps(bad).values()))
                index = next(i for i, t in enumerate(row) if t[0] == code)
                row[index] = (code, 999 if code == 91 else 'BROKEN')
                rejects(lambda: verify_opaque(bad, variant), 'Opaque map corruption accepted')
            print('PASS ' + name)
    check(packets == 10 and selected_appearances == 632 and controls == 212, 'Map verification inventory changed')
    print('PASS 36 outputs, 10 native map packets, 632 carrier record appearances, 212 actual corruptions rejected; zero audit errors or repairs')


if __name__ == '__main__':
    main()
