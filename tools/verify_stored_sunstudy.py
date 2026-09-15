#!/usr/bin/env python3
"""Verify immutable typed SUNSTUDY payloads, physical identities and source pointers."""
import argparse
import copy
import json
from pathlib import Path
from verify_sunstudy_producer import FIXTURES, check, digest, records, packet_graph, single


def verify_graph(rows, expected):
    graph = packet_graph(rows, len([tag for tag in expected['study'] if tag[0] == 290]) > 1)
    check(graph['study'] == expected['study'], 'Complete source SUNSTUDY record changed')
    check(single(graph['owner'], 5) == single(expected['owner'], 5), 'Source owner identity changed')
    for actual, source in zip(graph['dependencies'], expected['dependencies']):
        check(actual['code'] == source['code'] and actual['handle'] == source['handle'], 'Source pointer identity changed')
        check(actual['record'][0] == source['record'][0], 'Source pointer target class changed')
    return graph


def verify(artifacts):
    manifest = json.loads((FIXTURES / 'source-manifest.json').read_text())
    typed_manifest = json.loads((FIXTURES / 'typed-manifest.json').read_text())
    outputs = 0
    example = None
    expected = None
    hour_example = hour_expected = None
    for item in manifest['files']:
        if item['kind'] != 'original':
            continue
        source = (FIXTURES / item['carrier']['file']).read_bytes()
        check(digest(source) == item['carrier']['sha256'], 'Pinned source carrier differs')
        source_rows = records(source)
        typed_entry = next(entry for entry in typed_manifest['files'] if entry['name'] == item['name'])
        typed = (FIXTURES / 'typed-carriers' / item['name']).read_bytes()
        check(digest(typed) == typed_entry['sha256'] and digest(source) == typed_entry['sourceSha256'], 'Typed carrier hash differs')
        expected_rows = copy.deepcopy(source_rows)
        check(len(typed_entry['changes']) == 6, 'Expected three STYLE fields, two invalid plot scales and reciprocal plot owner')
        for change in typed_entry['changes']:
            row = next(row for row in expected_rows if row[0] == [0,change['recordType']] and [5,change['handle']] in row)
            index = change['index']
            if change['recordType'] == 'STYLE':
                check(row[index] == change['removed'] == [1071,0] and not any(c == 1001 for c,v in row[:index]), 'Invalid typed metadata adaptation')
                del row[index]
            else:
                check(row[index] == change['before'], 'Unexpected source plot field')
                if row[index][0] == 330:
                    check(row[index] == [330,'22'] and change['after'] == [330,'1F'], 'Invalid reciprocal owner adaptation')
                else:
                    check(row[index][0] in (142,143) and row[index][1] == 0.0, 'Invalid typed scale adaptation')
                    check(change['after'] == [row[index][0],1.0], 'Undisclosed scale replacement')
                row[index] = change['after']
        check(records(typed) == expected_rows, 'Undisclosed typed carrier changes')
        expected = packet_graph(expected_rows, item['hours'])
        for binary_input in (False, True):
            for binary_output in (False, True):
                name = f"sunstudy-stored-{item['name'][:-4]}-input-{binary_input}-output-{binary_output}.dxf"
                data = (artifacts / name).read_bytes()
                check(data.startswith(b'AutoCAD Binary DXF') == binary_output, 'Wrong SUNSTUDY output transport')
                parsed = records(data)
                verify_graph(parsed, expected)
                outputs += 1
                example = parsed
                if item['hours']:
                    hour_example, hour_expected = parsed, expected
    check(outputs == 24, 'Expected all six carrier packets across both input and output transports')
    controls = []
    for code in (5, 330, 90, 1, 2, 70, 3, 290, 4, 291, 91, 292, 93, 94, 95, 73, 340, 341, 342, 74, 75, 76, 77, 40, 293, 294, 343):
        damaged = copy.deepcopy(example)
        study = next(row for row in damaged if row[0] == [0, 'SUNSTUDY'])
        tag = next(tag for tag in study if tag[0] == code)
        tag[1] = tag[1] + '_changed' if isinstance(tag[1], str) else tag[1] + 1
        try:
            verify_graph(damaged, expected)
        except (ValueError, StopIteration):
            controls.append(code)
        else:
            raise ValueError('Corrupted stored field passed: ' + str(code))
    for index in range(4):
        damaged = copy.deepcopy(hour_example)
        study = next(row for row in damaged if row[0] == [0, 'SUNSTUDY'])
        flags = [tag for tag in study if tag[0] == 290][1:]
        flags[index][1] = 1 - flags[index][1]
        try:
            verify_graph(damaged, hour_expected)
        except ValueError:
            controls.append(f'ordered-hour-{index}')
        else:
            raise ValueError('Corrupted ordered hour flag passed')
    opaque = 0
    for shape in ('version', 'dates', 'private', 'subclass', 'extra', 'arbitrary', 'older'):
        for binary in (False, True):
            data = (artifacts / f'sunstudy-stored-opaque-{shape}-{binary}.dxf').read_bytes()
            check(data.startswith(b'AutoCAD Binary DXF') == binary, 'Wrong opaque transport')
            study = next(row for row in records(data) if row[0] == [0, 'SUNSTUDY'])
            if shape == 'version': check(single(study, 90) == 1, 'Unknown version lost')
            if shape == 'dates': check([v for c,v in study if c == 90] == [0,2460291,3600], 'Unknown date packet lost')
            if shape == 'private': check([1001,'not an APPID'] in study, 'Private APPID-looking data lost')
            if shape == 'subclass': check([100,'PrivateSunStudy'] in study, 'Private subclass lost')
            if shape == 'extra': check([300,'future field'] in study, 'Extra field lost')
            if shape == 'arbitrary': check([320,'7FFFFFF0'] in study and [329,'7FFFFFF1'] in study, 'Arbitrary handles lost')
            opaque += 1
    return {'typedOutputs': outputs, 'opaqueOutputs': opaque, 'corruptionControls': len(controls), 'corruptedFieldsRejected': controls,
            'nativeAutoCadValidation': False, 'dateListSemantics': False, 'solarEvaluation': False,
            'sourceEvidence': 'Six pinned IxMilia 0.8.4 carriers; disclosed empty DIMSTYLE pointers, three orphan STYLE 1071 fields, PLOTSETTINGS scales 142/143 and reciprocal PLOTSETTINGS owner adjusted'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.artifacts), indent=2))
