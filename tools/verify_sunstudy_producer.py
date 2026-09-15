#!/usr/bin/env python3
"""Verify pinned SUNSTUDY producer evidence and optional raw netDxf outputs.

This gate checks records and source identities without invoking a high-level
CAD audit, interpreting date values, or claiming native application support.
"""
import argparse
from collections import Counter
import copy
import gzip
import hashlib
import io
import json
from pathlib import Path

from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler
from ezdxf.lldxf.tagwriter import TagWriter, BinaryTagWriter
from ezdxf.lldxf.types import dxftag

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / 'tests/fixtures/sunstudy-producer'
PACKAGE_SHA = '3b08f5b604c958ea6c29f107f751d75abdf3328713ec586bc4fdbf0302e24c81'
ASSEMBLY_SHA = '70460eae6fd10ca6ca35653cb52eb03c2cbba2240b0afbda98afc744ad5dc67f'


def check(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def records(data):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    rows, row = [], []
    for tag in tag_compiler(loader):
        if tag.code == 0:
            if row:
                rows.append(row)
            row = []
        value = tag.value
        if isinstance(value, bytes):
            value = value.hex()
        elif isinstance(value, bool):
            value = int(value)
        elif not isinstance(value, (str, int, float)):
            value = list(value)
        row.append([tag.code, value])
    if row:
        rows.append(row)
    return rows


def single(row, code):
    values = [v for c, v in row if c == code]
    check(len(values) == 1, f'Expected exactly one group {code}')
    return values[0]


def carrier_records(rows):
    """Disclose the producer's ten unrelated empty DIMSTYLE pointers as nulls."""
    result = copy.deepcopy(rows)
    changes = []
    for row in result:
        if row[0] != [0, 'DIMSTYLE']:
            continue
        for tag in row:
            if 340 <= tag[0] <= 344 and tag[1] == '':
                changes.append({'recordType': 'DIMSTYLE', 'handle': single(row, 105),
                                'code': tag[0], 'before': '', 'after': '0'})
                tag[1] = '0'
    check(len(changes) == 10, 'Unexpected empty DIMSTYLE pointer count')
    return result, changes


def encode_records(rows, binary):
    stream = io.BytesIO() if binary else io.StringIO(newline='\n')
    writer = BinaryTagWriter(stream) if binary else TagWriter(stream)
    if binary:
        writer.write_signature()
    for row in rows:
        for code, value in row:
            writer.write_tag(dxftag(code, value))
    value = stream.getvalue()
    return value if binary else value.encode('utf-8')


def packet_graph(rows, hours):
    studies = [row for row in rows if row[0] == [0, 'SUNSTUDY']]
    check(len(studies) == 1, 'Expected one actual SUNSTUDY record')
    study = studies[0]
    by_handle = {}
    for row in rows:
        handles = [v for c, v in row if c == 5]
        # HEADER variables are not database records.
        if row[0] == [0, 'SECTION']:
            continue
        if handles:
            check(len(handles) == 1 and handles[0] not in by_handle, 'Duplicate database handle')
            by_handle[handles[0]] = row
    owner = by_handle.get(single(study, 330))
    check(owner is not None and owner[0] == [0, 'DICTIONARY'], 'Missing owner dictionary')
    entry = owner.index([3, 'SUN_STUDY'])
    check(owner[entry + 1] in ([350, single(study, 5)], [360, single(study, 5)]), 'Owner entry changed')
    dependencies = []
    for code, kind in ((340, 'PLOTSETTINGS'), (341, 'VIEW'), (342, 'VISUALSTYLE'), (343, 'STYLE')):
        handle = single(study, code)
        target = by_handle.get(handle)
        check(target is not None and target[0] == [0, kind], f'Missing/wrong group {code} dependency')
        dependencies.append({'code': code, 'handle': handle, 'record': target})
    expected = [[100, 'AcDbSunStudy'], [90, 0], [1, 'Stored solar study'], [2, 'Producer storage probe'],
                [70, 0], [3, 'SHEET_SET'], [290, 1], [4, 'SHEET_SUBSET'], [291, 0], [91, 0],
                [292, 1], [93, 28800], [94, 64800], [95, 3600], [73, 4 if hours else 0]]
    if hours:
        expected.extend([[290, value] for value in (0, 1, 0, 1)])
    expected.extend([[item['code'], item['handle']] for item in dependencies[:3]])
    expected.extend([[74, 2], [75, 6], [76, 2], [77, 3], [40, 2.5], [293, 1], [294, 1], [343, dependencies[3]['handle']]])
    check(study[study.index([100, 'AcDbSunStudy']):] == expected, 'SUNSTUDY body/order/count changed')
    return {'study': study, 'owner': owner, 'dependencies': dependencies}


def verify(fixtures, artifacts=None):
    manifest = json.loads((fixtures / 'source-manifest.json').read_text())
    attempts = json.loads((fixtures / 'producer-results.json').read_text())
    check(manifest['packageSha256'] == PACKAGE_SHA, 'Package pin differs')
    check(attempts['assemblySha256'] == ASSEMBLY_SHA, 'Assembly pin differs')
    check(attempts['unexpected'] is False and len(attempts['attempts']) == 16, 'Producer matrix differs')
    check(sum(item['saved'] for item in attempts['attempts']) == 8, 'Saved source count differs')
    check(sum(item['reloaded'] for item in attempts['attempts']) == 6, 'Self-reload count differs')
    check(sum(item['stage'] == 'save' for item in attempts['attempts']) == 8, 'Date failure count differs')
    check(sum(item['stage'] == 'reload' for item in attempts['attempts']) == 2, 'Binary-hour failure count differs')
    check(len(manifest['files']) == 22, 'Expected six originals, six resaves, two failed reloads and eight partial failures')
    check(Counter(item['kind'] for item in manifest['files'])
          == {'original': 6, 'resaved': 6, 'failed-reload': 2, 'failed-save': 8}, 'Source outcome classification differs')
    check(len({item['name'] for item in manifest['files']}) == 22, 'Duplicate source entry')
    check({str(path.relative_to(fixtures)) for path in fixtures.glob('*-gzip/*.gz')}
          == {item['storedFile'] for item in manifest['files']}, 'Compressed source inventory differs')
    check({str(path.relative_to(fixtures)) for path in fixtures.glob('carriers/*.dxf')}
          == {item['carrier']['file'] for item in manifest['files'] if item['kind'] == 'original'}, 'Carrier inventory differs')
    completed = outputs = 0
    control_rows = None
    for item in manifest['files']:
        compressed = (fixtures / item['storedFile']).read_bytes()
        check(digest(compressed) == item['gzipSha256'], 'Compressed hash differs: ' + item['name'])
        raw = gzip.decompress(compressed)
        check(len(raw) == item['bytes'] and digest(raw) == item['sha256'], 'Original bytes differ: ' + item['name'])
        if item['kind'] == 'failed-save':
            continue
        rows = records(raw)
        if item['kind'] == 'failed-reload':
            study = next(row for row in rows if row[0] == [0, 'SUNSTUDY'])
            check(study == item['malformedPacket'], 'Malformed producer packet differs')
            check([tag for tag in study if tag[0] > 1071] == [[8704, '\x01\x01'], [8704, '\x01\x01']],
                  'Expected binary hour-list framing failure')
            continue
        graph = packet_graph(rows, item['hours'])
        check(graph == item['graph'], 'Pinned source graph differs: ' + item['name'])
        if item['kind'] == 'original':
            completed += 1
            if item['hours']:
                control_rows = rows
            expected_rows, changes = carrier_records(rows)
            carrier = item['carrier']
            check(carrier['transformations'] == changes, 'Carrier change disclosure differs')
            carrier_bytes = (fixtures / carrier['file']).read_bytes()
            check(digest(carrier_bytes) == carrier['sha256'], 'Carrier bytes differ')
            check(records(carrier_bytes) == expected_rows, 'Carrier changed undisclosed records')
            check(packet_graph(expected_rows, item['hours']) == graph, 'Carrier changed the SUNSTUDY dependency graph')
            if artifacts:
                base = item['name'][:-4]
                for transport in ('ascii', 'binary'):
                    path = artifacts / f'sunstudy-producer-{base}-{transport}.dxf'
                    output = path.read_bytes()
                    check(output.startswith(b'AutoCAD Binary DXF') == (transport == 'binary'), 'Wrong output transport')
                    check(records(output) == expected_rows, 'Raw normalized output changed records: ' + path.name)
                    outputs += 1
                path = artifacts / f'sunstudy-producer-{base}-exact.dxf'
                check(path.read_bytes() == carrier_bytes, 'Exact raw save changed carrier bytes: ' + path.name)
                outputs += 1
    check(completed == 6, 'Usable original SUNSTUDY packet count differs')
    packet_graph(control_rows, True)
    controls = []
    for name in ('hour-count', 'range-time', 'missing-study', 'broken-target', 'wrong-target-type', 'owner-entry'):
        damaged = copy.deepcopy(control_rows)
        study = next(row for row in damaged if row[0] == [0, 'SUNSTUDY'])
        if name == 'hour-count':
            next(tag for tag in study if tag[0] == 73)[1] = 100
        elif name == 'range-time':
            next(tag for tag in study if tag[0] == 93)[1] = 1
        elif name == 'missing-study':
            damaged.remove(study)
        elif name == 'broken-target':
            next(tag for tag in study if tag[0] == 340)[1] = 'FFFFF'
        elif name == 'wrong-target-type':
            next(row for row in damaged if [5, single(study, 340)] in row)[0][1] = 'XRECORD'
        elif name == 'owner-entry':
            owner = next(row for row in damaged if [5, single(study, 330)] in row)
            owner[owner.index([3, 'SUN_STUDY']) + 1][1] = 'FFFFF'
        try:
            packet_graph(damaged, True)
        except ValueError:
            controls.append(name)
        else:
            raise ValueError('Undetected corruption: ' + name)
    return {'producer': attempts['producer'], 'usableSunStudyPackets': completed, 'producerSelfRoundtrips': 6,
            'retainedFailedSaves': 8, 'retainedFailedReloads': 2, 'nativeApplicationValidation': False,
            'unchangedWholeFileRawImport': False, 'disclosedDimstyleNullPointersPerCarrier': 10,
            'netDxfRawOutputs': outputs, 'negativeControls': controls}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', nargs='?', type=Path)
    parser.add_argument('--fixtures', type=Path, default=FIXTURES)
    args = parser.parse_args()
    print(json.dumps(verify(args.fixtures, args.artifacts), indent=2))


if __name__ == '__main__':
    main()
