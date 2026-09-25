#!/usr/bin/env python3
"""Validate actual INSERT terminator records, ownership, metadata and identity stability."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_dimlfac_fidelity import PROFILES, records
from verify_raw_line_geometry import require, reject, audit_signature

NAMES = {f'SQ_{place}_{array}' for place, array in itertools.product(range(4), range(2))}


def one(row, code):
    values = [v for c, v in row if c == code]
    require(len(values) == 1, f'Missing/duplicate field {code}')
    return values[0]


def common_header(row):
    fields, groups = [], {}
    current = None
    for code, value in row[1:]:
        if code == 100:
            require(current is None, 'Unterminated header group')
            break
        if code == 102:
            if value == '}':
                require(current is not None, 'Unmatched header group close')
                current = None
            else:
                require(current is None and value in ('{ACAD_XDICTIONARY', '{ACAD_REACTORS') and value not in groups,
                        'Nested, unknown or duplicate header group')
                current = value; groups[current] = []
        elif current is None:
            fields.append((code, value))
        else:
            groups[current].append((code, value))
    require(current is None, 'Open header group')
    return fields, groups


def check_sequence(insert, attributes, end, line_handle):
    name, owner = one(insert, 2), one(insert, 5)
    require(name in NAMES and one(insert, 66) == 1, 'INSERT inventory/sequence flag')
    require(len(attributes) == 2, 'ATTRIB count')
    for row, tag, value in zip(attributes, ('A', 'B'), ('FIRST', 'SECOND')):
        require(row[0] == (0, 'ATTRIB') and one(row, 330) == owner, 'ATTRIB type/owner')
        require(one(row, 2) == tag and one(row, 1) == value, 'ATTRIB order/data')
    require(end[0] == (0, 'SEQEND'), 'Missing terminator')
    fields, groups = common_header(end)
    handle = one(fields, 5)
    require(one(fields, 330) == owner and handle not in (owner, '0'), 'SEQEND owner/identity')
    require(groups.get('{ACAD_REACTORS') == [(330, line_handle)], 'SEQEND reactor header')
    extension = one(groups.get('{ACAD_XDICTIONARY', []), 360)
    require([t for t in end if t[0] >= 1000] == [(1001, 'SQ_DATA'), (1000, name)], 'SEQEND XData')
    require(end.count((100, 'AcDbEntity')) == 1 and one(end, 8) == '0', 'SEQEND subclass/layer')
    return (owner, one(insert, 330), *(one(row, 5) for row in attributes), handle, extension)


def inspect(path, year, binary):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load_visibility_tags(path); index = tags.index((9, '$ACADVER'))
    require(tags[index + 1] == (1, PROFILES[year]), 'Wrong version')
    rows = [tags[a:b] for a, b in records(tags)]
    following = [row for row in rows if row[0] == (0, 'LINE') and (10, 101.) in row]
    require(len(following) == 1, 'Following LINE inventory'); line = one(following[0], 5)
    selected = [(i, row) for i, row in enumerate(rows) if row[0] == (0, 'INSERT') and any(c == 2 and v in NAMES for c, v in row)]
    require(len(selected) == 8 and {one(row, 2) for i, row in selected} == NAMES, 'INSERT name inventory')
    result, controls = {}, 0
    for index, insert in selected:
        attributes, end = rows[index + 1:index + 3], rows[index + 3]
        result[one(insert, 2)] = check_sequence(insert, attributes, end, line)
        fields, groups = common_header(end)
        header_owner = end.index((330, one(insert, 5)))
        handle_at = end.index((5, one(fields, 5)))
        for code_at in (header_owner, handle_at):
            for mode in ('remove', 'duplicate', 'replace'):
                bad = list(end)
                if mode == 'remove': bad.pop(code_at)
                elif mode == 'duplicate': bad.insert(code_at, bad[code_at])
                else: bad[code_at] = (bad[code_at][0], '0')
                controls += reject(lambda: check_sequence(insert, attributes, bad, line))
        bad = list(end); at = bad.index((1000, one(insert, 2))); bad[at] = (1000, 'changed')
        controls += reject(lambda: check_sequence(insert, attributes, bad, line))
        controls += reject(lambda: check_sequence(insert, list(reversed(attributes)), end, line))
    all_handles = [handle for identity in result.values() for handle in (identity[0], identity[2], identity[3], identity[4])]
    require(len(set(all_handles)) == len(all_handles), 'Duplicate sequence identity')
    doc = ezdxf.readfile(path)
    for name, identity in result.items():
        insert = doc.entitydb[identity[0]]
        require(insert.dxf.name == name and insert.dxf.owner == identity[1], 'Independent INSERT identity/owner')
        # ezdxf.link_seqend rewrites the owner to INSERT's layout owner on load.
        # The actual wire owner is checked above, before that normalization.
        require(insert.seqend.dxf.handle == identity[4] and doc.entitydb[identity[4]] is insert.seqend,
                'Independent SEQEND identity/link')
        require([item.dxf.handle for item in insert.attribs] == list(identity[2:4]), 'Independent attribute identities')
        require(insert.seqend.get_reactors() == [line], 'Independent reactor')
        extension = insert.seqend.get_extension_dict().dictionary
        require(extension.dxf.handle == identity[5] and extension.dxf.owner == identity[4], 'Independent extension ownership')
        require(list(extension['SQ_PAYLOAD'].tags) == [(1, 'sequence-metadata')], 'Independent extension payload')
        place = int(name.split('_')[1])
        layout = doc.modelspace() if place == 0 else doc.layouts.get('SQ_PAPER') if place == 1 else doc.blocks[f'SQ_CONTAINER_{place}']
        require(insert.dxf.owner == layout.block_record_handle, 'Independent placement')
    references = doc.rootdict['SQ_REFERENCES']
    require(list(references.tags) == [(330, result[f'SQ_{place}_{array}'][4]) for place, array in itertools.product(range(4), range(2))],
            'Independent references to registered terminators')
    require(not any(any(counts.values()) for counts in audit_signature(doc)), 'Independent audit errors/repairs')
    return result, controls


def main(directory):
    names = {f'insert-sequence-AutoCad{year}-{transport}-{stage}.dxf' for year, transport, stage in
             itertools.product(PROFILES, ('text', 'binary'), ('source', 'output', 'resave'))}
    def inventory(actual): require(actual == names, 'Sequence fixture inventory')
    inventory({p.name for p in directory.glob('insert-sequence-*.dxf')})
    controls = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year, transport in itertools.product(PROFILES, ('text', 'binary')):
        previous = None
        for stage in ('source', 'output', 'resave'):
            actual, count = inspect(directory/f'insert-sequence-AutoCad{year}-{transport}-{stage}.dxf', year,
                                    (transport == 'binary') != (stage == 'output'))
            require(previous is None or actual == previous, 'Cross-save sequence identity changed')
            previous = actual; controls += count
    print(f'PASS: {len(names)} drawings, 288 registered SEQEND records and 576 ATTRIB records; '
          f'{controls} corruption/inventory controls rejected; zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_insert_sequences.py ARTIFACTS')
    main(Path(sys.argv[1]))
