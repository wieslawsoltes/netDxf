#!/usr/bin/env python3
"""Independently verify addressed numeric table edits against every physical record.

The numeric inputs deliberately replace seven native string scalars with zeros.
They are synthetic requests in a pinned native TABLECONTENT family, not new
native-producer observations or native formula FIELD serialization.
"""
from __future__ import annotations
import argparse
import copy
import gzip
import hashlib
import json
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata
from verify_cell_style_consumers import content_slots, value_end
from verify_mleader_inputs import check

EXPECTED = {(0, 0): 7.0, (1, 0): 21.0, (2, 0): 28.0}


def scalar_slots(tags):
    content_slots(tags)  # Independent frame-tree qualification, not production indices.
    start = tags.index([100, 'AcDbLinkedTableData'])
    limit = tags.index([100, 'AcDbFormattedTableData'])
    row = column = -1
    index = start + 1
    result = {}
    while index < limit:
        tag = tags[index]
        if tag == [1, 'LINKEDTABLEDATAROW_BEGIN']:
            row += 1
            column = -1
        elif tag == [1, 'LINKEDTABLEDATACELL_BEGIN']:
            column += 1
        elif tag == [1, 'CELLCONTENT_BEGIN']:
            check((row, column) not in result, 'Repeated synthetic scalar address')
            check([t[0] for t in tags[index + 1:index + 10]] == [90, 300, 93, 90, 140, 94, 300, 302, 304], 'Numeric AcValue shape changed')
            check(tags[index + 1:index + 5] == [[90, 1], [300, 'VALUE'], [93, 2], [90, 2]], 'Numeric AcValue kind changed')
            check(tags[index + 6:index + 8] == [[94, 0], [300, '']], 'Unit or raw expression changed')
            check(tags[index + 9] == [304, 'ACVALUE_END'], 'AcValue terminator changed')
            result[row, column] = (index + 5, index + 8)
            index = value_end(tags, index + 2, limit)
        index += 1
    check(set(result) == {(0, 0), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2)}, 'Addressed scalar inventory changed')
    return result


def expected_records(before):
    wanted = copy.deepcopy(before)
    contents = [handle for handle, tags in before.items() if tags[0] == [0, 'TABLECONTENT']]
    check(len(contents) == 1, 'Expected one numeric backing content')
    handle = contents[0]
    slots = scalar_slots(before[handle])
    for address, (number, display) in slots.items():
        check(before[handle][number] == [140, 0.0], 'Synthetic zero source changed')
        if address in EXPECTED:
            wanted[handle][number] = [140, EXPECTED[address]]
            wanted[handle][display] = [302, str(int(EXPECTED[address]))]
    return wanted, handle, slots


def compare(wanted, actual):
    check(list(wanted) == list(actual), 'Physical record identity/order changed')
    check(wanted == actual, 'Change outside the exact calculated numeric/display slots')


def check_pair(before, after):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    wanted, handle, slots = expected_records(before)
    compare(wanted, after)
    controls = 0
    # Challenge every backing-content tag, including unused values, styles and
    # geometry links, rather than checking only successful arithmetic values.
    for index, (code, value) in enumerate(after[handle]):
        corrupt = dict(after)
        corrupt[handle] = copy.deepcopy(after[handle])
        changed = value + '_CORRUPT' if isinstance(value, str) else value + [999] if isinstance(value, list) else value + 1
        corrupt[handle][index] = [code, changed]
        try:
            compare(wanted, corrupt)
        except ValueError:
            controls += 1
        else:
            raise AssertionError('Corrupt content tag escaped the comparator')
    for key in after:
        corrupt = dict(after)
        del corrupt[key]
        try:
            compare(wanted, corrupt)
        except ValueError:
            controls += 1
        else:
            raise AssertionError('Missing physical record escaped the comparator')
    return controls


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    expected = {f'table-calculation-{phase}-{binary}.dxf' for phase in ('before', 'after') for binary in (False, True)}
    check({p.name for p in args.directory.glob('table-calculation-*.dxf')} == expected, 'Exact calculation fixture inventory required')
    root = Path(__file__).resolve().parents[1]
    file = 'acad_table_simple.dxf'
    pin = next(p for p in json.loads((root / 'tools/table_oracle/fixtures.json').read_text())['files'] if p['file'] == file)
    native = gzip.decompress((root / 'tests/fixtures/table-oracle' / (file + '.gz')).read_bytes())
    check(hashlib.sha256(native).hexdigest() == pin['sha256'], 'Native family source hash changed')
    controls = 0
    for binary in (False, True):
        paths = [args.directory / f'table-calculation-{phase}-{binary}.dxf' for phase in ('before', 'after')]
        for path in paths:
            check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Calculation transport changed')
            doc = ezdxf.readfile(path)
            check(doc.dxfversion == pin['profile'], 'Source profile changed')
            audit = doc.audit()
            check(not audit.errors and not audit.fixes, 'Calculated drawing needed repairs')
        controls += check_pair(records(paths[0]), records(paths[1]))
    print(f'PASS: 2 calculated drawing pairs, 6 addressed results, {controls} parsed-output corruption controls; no native FIELD or geometry-regeneration claim')


if __name__ == '__main__':
    main()
