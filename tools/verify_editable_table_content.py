#!/usr/bin/env python3
"""Verify explicit stored TABLECONTENT scalar edits against pinned native packets."""
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

    def contents(records):
        return {h: row for h, row in records.items() if row[0] == (0, 'TABLECONTENT')}

    def body(row):
        start = next(i for i, tag in enumerate(row) if tag[0] == 100)
        end = next((i for i in range(start, len(row)) if row[i][0] == 1001), len(row))
        return row[start:end]

    def table_body(row):
        start = row.index((100, 'AcDbBlockReference'))
        end = next((i for i in range(start, len(row)) if row[i][0] == 1001), len(row))
        return typed(row[start:end])

    def qualify(tags, year):
        frames = []
        for index, tag in enumerate(tags):
            if tag != (1, 'CELLCONTENT_BEGIN') or tags[index - 1] != (302, 'CONTENT'):
                continue
            if tags[index + 1:index + 3] != [(90, 1), (300, 'VALUE')]:
                continue
            scalar = index + 4 if year == 2004 else index + 5
            if year != 2004 and tags[index + 3] not in [(93, 2), (93, 4), (93, 6)]:
                continue
            kind = tags[scalar - 1]
            codes = {1: 91, 2: 140, 4: 1, 32: 11}
            if kind[0] != 90 or kind[1] not in codes or tags[scalar][0] != codes[kind[1]]:
                continue
            after = scalar + 1  # ezdxf compiles groups 11,21,31 into one point tag.
            display = None
            if year != 2004:
                if [code for code, _ in tags[after:after + 4]] != [94, 300, 302, 304] or tags[after + 3] != (304, 'ACVALUE_END'):
                    continue
                display = after + 2
                after += 4
            if tags[after:after + 2] != [(91, 0), (309, 'CELLCONTENT_END')]:
                continue
            frames.append((scalar, display))
        return frames

    def transformed(row, year):
        tags = list(body(row))
        tags[1] = (1, 'Edited name')
        tags[2] = (300, 'Edited description')
        frames = qualify(tags, year)
        for number, (index, display) in enumerate(frames):
            code, value = tags[index]
            if code == 91:
                value += 7
            elif code == 140:
                value += 0.125
            elif code == 1:
                value = 'Edited ' + value
            else:
                value = tuple(a + b for a, b in zip(value, (1.0, 2.0, 3.0)))
            tags[index] = code, value
            if display is not None:
                tags[display] = 302, 'Stored display ' + str(number)
        return tags

    def typed(tags):
        return [(code, exact(value)) for code, value in tags]

    def verify_class(classes, count):
        rows = [row for row in classes if (1, 'TABLECONTENT') in row]
        check(len(rows) == 1, 'Missing or duplicate TABLECONTENT CLASS')
        check(all(tag in rows[0] for tag in [(2, 'AcDbTableContent'), (3, 'ObjectDBX Classes'), (90, 1152), (91, count), (280, 0), (281, 0)]), 'TABLECONTENT CLASS differs')

    def verify_native(records, classes, original, year, edited, carrier):
        before = contents(original)
        check(set(contents(records)) == set(before), 'Native content identity inventory changed')
        for handle, row in before.items():
            expected = typed(transformed(row, year)) if edited else payload(row)
            check(payload(records[handle]) == expected, 'Explicit native content or untouched nested tags differ')
            check(metadata(records[handle]) == metadata(row), 'Native content metadata changed')
            owner = metadata(row)[0]
            check(owner in records and records[owner][0] == (0, 'XRECORD'), 'Native owning wrapper identity changed')
            check(payload(records[owner]) == payload(original[owner]), 'Native wrapper payload changed')
            check(metadata(records[owner]) == metadata(carrier.get(owner, original[owner])), 'Native wrapper common metadata changed')
        for handle, row in original.items():
            if row[0][1] in ('ACAD_TABLE', 'TABLEGEOMETRY', 'TABLESTYLE', 'CELLSTYLEMAP'):
                check(handle in records and records[handle][0] == row[0], 'Unrelated native identity changed')
                check(metadata(records[handle]) == metadata(carrier.get(handle, row)), 'Companion native common metadata changed outside the source-carrier mapping')
                if row[0][1] == 'ACAD_TABLE':
                    check(table_body(records[handle]) == table_body(row), 'Unrelated native TABLE subclass packet changed')
                    common_before = row[:row.index((100, 'AcDbBlockReference'))]
                    common_after = records[handle][:records[handle].index((100, 'AcDbBlockReference'))]
                    proxy_before = b''.join(value for code, value in common_before if code == 310)
                    proxy_after = b''.join(value for code, value in common_after if code == 310)
                    check(proxy_after == proxy_before, 'Native TABLE proxy bytes changed')
                    # Existing common-entity output normalizes the proxy byte-count code by
                    # target profile; R2010 native group160 becomes group92 without a data edit.
                    source_lengths = [value for code, value in common_before if code in (92, 160)]
                    output_lengths = [(code, value) for code, value in common_after if code in (92, 160)]
                    check(output_lengths == [(160 if year >= 2013 else 92, value) for value in source_lengths], 'Native TABLE proxy length differs')
                else:
                    check(payload(records[handle]) == payload(row), 'Unrelated native stored packet changed')
        verify_class(classes, len(before))

    def synthetic_body(year, edited=True):
        tags = [(100, 'AcDbLinkedData'), (1, 'name' if edited else 'Stored name'), (300, r'description\U+005CU+0041' if edited else 'Stored description'),
                (100, 'AcDbLinkedTableData'), (90, 0), (91, 1), (301, 'ROW'), (1, 'LINKEDTABLEDATAROW_BEGIN'), (90, 4)]
        values = [(1, 91, -2147483641 if edited else -2147483648), (2, 140, 0.125 if edited else -0.0),
                  (4, 1, 'Edited text' if edited else 'text'), (32, 11, (1.0, 4.0, 6.0) if edited else (-0.0, 2.0, 3.0))]
        for kind, code, value in values:
            tags += [(300, 'CELL'), (1, 'LINKEDTABLEDATACELL_BEGIN'), (95, 1), (302, 'CONTENT'), (1, 'CELLCONTENT_BEGIN'), (90, 1), (300, 'VALUE')]
            if year != 2004:
                tags += [(93, 6)]
            tags += [(90, kind), (code, value)]
            if year != 2004:
                tags += [(94, 0), (300, ''), (302, 'explicit display' if edited else 'display'), (304, 'ACVALUE_END')]
            tags += [(91, 0), (309, 'CELLCONTENT_END'), (309, 'LINKEDTABLEDATACELL_END')]
        return tags + [(309, 'LINKEDTABLEDATAROW_END'), (92, 0), (100, 'AcDbFormattedTableData'), (100, 'AcDbTableContent'), (340, '0')]

    def verify_synthetic(records, classes, year, edited):
        rows = contents(records)
        check(len(rows) == 1, 'Synthetic content inventory changed')
        handle, row = next(iter(rows.items()))
        owner = metadata(row)[0]
        check(owner in records and ('CONTENT', 360, handle) in dictionary_edges(records[owner]), 'Synthetic content owner edge changed')
        check(payload(row) == typed(synthetic_body(year, edited)), 'Synthetic content packet changed')
        verify_class(classes, 1)

    expected = {f'editable-table-content-{mode}-{file}-{input_binary}-{binary}.dxf' for mode in ('native', 'noop') for file in FILES for input_binary in (False, True) for binary in (False, True)}
    expected |= {f'editable-table-content-schema-AutoCad{year}-{binary}.dxf' for year in (2004, 2007, 2010, 2013, 2018) for binary in (False, True)}
    expected |= {f'editable-table-content-style-{binary}.dxf' for binary in (False, True)}
    check({path.name for path in args.directory.glob('editable-table-content-*.dxf')} == expected, 'All 52 editable content outputs are mandatory')
    manifest = json.loads((args.repository / 'tests/fixtures/table-content/manifest.json').read_text())
    check(sum(verify_extraction(args.repository, row) for row in manifest['files']) == 316, 'Native carrier inventory changed')
    controls = packets = appearances = scalars = 0

    def rejects(action):
        nonlocal controls
        try:
            action()
        except (ValueError, KeyError, StopIteration):
            controls += 1
        else:
            raise ValueError('Edited content output corruption accepted')

    def read(name, year, binary):
        doc, records, classes = load(args.directory / name, year, binary)
        audit = doc.audit()
        check(not audit.errors and not audit.fixes, 'Edited content output audit failed')
        return records, classes

    for file in FILES:
        original, _, year = source(args.repository, file)
        entry = next(row for row in manifest['files'] if row['file'] == file)
        selected = entry.get('records', [])
        carrier = plain_records((args.repository / 'tests/fixtures/table-content' / entry['fixture']).read_bytes(), entry['profile']) if selected else {}
        for mode in ('native', 'noop'):
            for input_binary in (False, True):
                for binary in (False, True):
                    name = f'editable-table-content-{mode}-{file}-{input_binary}-{binary}.dxf'
                    records, classes = read(name, year, binary)
                    verify_native(records, classes, original, year, mode == 'native', carrier)
                    verify_carrier_graph(records, carrier, selected)
                    packets += len(contents(original)); appearances += len(selected)
                    if mode == 'native':
                        scalars += sum(len(qualify(body(row), year)) for row in contents(original).values())
                    for fault in range(11):
                        bad = copy.deepcopy(records); definitions = copy.deepcopy(classes)
                        row = next(iter(contents(bad).values()))
                        if fault < 6:
                            code = (1, 300, 90, 95, 340, 309)[fault]
                            index = next(i for i, tag in enumerate(row) if tag[0] == code)
                            row[index] = (code, 123456 if code in (90, 95) else 'BAD')
                        elif fault == 6:
                            bad.pop(metadata(row)[0])
                        elif fault == 7:
                            definitions = []
                        elif fault == 8:
                            table = next(value for value in bad.values() if value[0] == (0, 'ACAD_TABLE'))
                            table.append((300, 'UNREQUESTED_TABLE_CHANGE'))
                        elif fault == 9:
                            table = next(value for value in bad.values() if value[0] == (0, 'ACAD_TABLE'))
                            index = next(i for i, tag in enumerate(table) if tag[0] == 310)
                            data = bytearray(table[index][1]); data[0] ^= 1; table[index] = 310, bytes(data)
                        else:
                            table = next(value for value in bad.values() if value[0] == (0, 'ACAD_TABLE'))
                            index = next(i for i, tag in enumerate(table) if tag[0] == 330)
                            table[index] = 330, '0'
                        rejects(lambda: verify_native(bad, definitions, original, year, mode == 'native', carrier))
                    print('PASS ' + name)
    for year in (2004, 2007, 2010, 2013, 2018):
        for binary in (False, True):
            name = f'editable-table-content-schema-AutoCad{year}-{binary}.dxf'
            records, classes = read(name, year, binary); verify_synthetic(records, classes, year, True)
            for code in (1, 90, 91, 140, 11, 340):
                bad = copy.deepcopy(records); row = next(iter(contents(bad).values()))
                index = next(i for i, tag in enumerate(row) if tag[0] == code)
                row[index] = (code, (99.0, 98.0, 97.0) if code == 11 else 999)
                rejects(lambda: verify_synthetic(bad, classes, year, True))
            print('PASS ' + name)
    for binary in (False, True):
        name = f'editable-table-content-style-{binary}.dxf'
        records, classes = read(name, 2018, binary); verify_synthetic(records, classes, 2018, False)
        print('PASS ' + name)
    check(controls == 500 and packets == 64 and appearances == 2528 and scalars == 344, 'Editable content verification inventory changed')
    print('PASS 52 outputs, 64 native content packet appearances, 344 explicit scalar changes, 2528 carrier record appearances, 500 actual corruptions rejected; zero audit errors or repairs')


if __name__ == '__main__':
    main()
