#!/usr/bin/env python3
"""Inspect declared-schema opaque entity source/output packets independently of typed loading."""
import argparse
import copy
import re
from pathlib import Path
from verify_sunstudy_producer import check, records

NAME = 'QUALIFIED_FUTURE_CURVE'
PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021', 2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}


def packet(rows):
    found = [row for row in rows if row[0] == [0, NAME]]
    check(len(found) == 1, 'Expected exactly one declared opaque record')
    return found[0]


def definition(rows):
    found = [row for row in rows if row[0] == [0, 'CLASS'] and [1, NAME] in row]
    check(len(found) == 1, 'Expected one qualified CLASS declaration')
    return found[0]


def verify_rows(source, output, year):
    check(packet(source) == packet(output), 'Complete ordered opaque tag packet changed')
    check(definition(source) == definition(output), 'Qualified CLASS fields changed')
    row = packet(output)
    check(row[1] == [5, 'F001'], 'Physical source identity changed')
    common = row.index([100, 'AcDbEntity'])
    body = row.index([100, 'AcDbQualifiedFutureCurve'])
    check(common < body, 'Common/private subclass ordering changed')
    check([5, 'BEEF'] in row[body:] and [320, 'DEAD'] in row[body:] and [329, 'BEEF'] in row[body:], 'Private identity/arbitrary values changed')
    check([62, 191] in row[body:] and [48, -8.0] in row[body:], 'Private common-like fields changed')
    check([102, '{UNINTERPRETED'] in row[:common] and [66, 1] in row[:common] and [101, 'Embedded Object'] in row[:common], 'Private header framing changed')
    check([102, '{NESTED'] in row[body:] and [100, 'AcDbVertex'] in row[body:], 'Nested private marker changed')
    owner = row[2][1]
    owners = [item for item in output if item[0] == [0, 'BLOCK_RECORD'] and [5, owner] in item]
    check(len(owners) == 1, 'Physical BLOCK_RECORD owner is missing or duplicated')
    target = next(value for code, value in row if code == 340)
    targets = [item for item in output if item[0] == [0, 'LINE'] and [5, target] in item]
    check(len(targets) == 1, 'Actual shared source LINE target is missing or duplicated')
    header = next(item for item in output if item[:2] == [[0, 'SECTION'], [2, 'HEADER']])
    version = header.index([9, '$ACADVER'])
    check(header[version + 1] == [1, PROFILES[year]], 'Source profile changed')


def verify(directory):
    outputs = 0
    for year in PROFILES:
        for source_binary in (False, True):
            source_bytes = (directory / f'opaque-entity-source-AutoCad{year}-{source_binary}.dxf').read_bytes()
            check(source_bytes.startswith(b'AutoCAD Binary DXF') == source_binary, 'Declared source transport differs')
            source = records(source_bytes)
            for output_binary in (False, True):
                data = (directory / f'opaque-entity-packet-AutoCad{year}-{source_binary}-{output_binary}.dxf').read_bytes()
                check(data.startswith(b'AutoCAD Binary DXF') == output_binary, 'Opaque output transport differs')
                verify_rows(source, records(data), year)
                outputs += 1
    source = records((directory / 'opaque-entity-source-AutoCad2018-False.dxf').read_bytes())
    original = records((directory / 'opaque-entity-packet-AutoCad2018-False-True.dxf').read_bytes())
    corruptions = 0
    for fault in ('identity', 'owner', 'private-identity', 'arbitrary', 'private-visibility', 'private-scale', 'private-coordinate', 'private-binary', 'private-name', 'private-group', 'common-layer', 'pointer', 'class-value', 'class-omission', 'row-order'):
        changed = copy.deepcopy(original)
        row = packet(changed)
        body = row.index([100, 'AcDbQualifiedFutureCurve'])
        if fault == 'identity': row[1] = [5, 'F002']
        elif fault == 'owner': row[2] = [330, '0']
        elif fault == 'private-identity': row[row.index([5, 'BEEF'])] = [5, 'F001']
        elif fault == 'arbitrary': row[row.index([320, 'DEAD'])] = [320, '0']
        elif fault == 'private-visibility': row[row.index([62, 191])] = [62, 1]
        elif fault == 'private-scale': row[row.index([48, -8.0])] = [48, 1.0]
        elif fault == 'private-coordinate': row[next(i for i in range(body, len(row)) if row[i][0] == 10)] = [10, 8.5]
        elif fault == 'private-binary': del row[next(i for i in range(body, len(row)) if row[i][0] == 310)]
        elif fault == 'private-name': row[next(i for i in range(body, len(row)) if row[i][0] == 300)] = [300, 'decoded private text']
        elif fault == 'private-group': del row[row.index([102, '{NESTED'])]
        elif fault == 'common-layer': row[row.index([8, '0'])] = [8, 'missing']
        elif fault == 'pointer': row[next(i for i in range(body, len(row)) if row[i][0] == 340)] = [340, '0']
        elif fault == 'class-value': definition(changed)[-1] = [281, 0]
        elif fault == 'class-omission': changed.remove(definition(changed))
        else: row[body + 1], row[body + 2] = row[body + 2], row[body + 1]
        try: verify_rows(source, changed, 2018)
        except ValueError: corruptions += 1
        else: raise ValueError(f'Corrupted opaque packet accepted: {fault}')
    literal_checks = 0
    def decoded(text):
        return re.sub(r"\\U\+([0-9A-Fa-f]{4})", lambda match: chr(int(match.group(1), 16)), text)
    def literal(rows):
        values = [decoded(value) for code, value in definition(rows) if code == 3]
        check(values == [r"literal \U+0041 Ω"], 'Literal CLASS escape changed its semantic text')
    for year in PROFILES:
        for source_binary in (False, True):
            source = records((directory / f'opaque-class-source-AutoCad{year}-{source_binary}.dxf').read_bytes())
            literal(source)
            for output_binary in (False, True):
                changed = records((directory / f'opaque-class-output-AutoCad{year}-{source_binary}-{output_binary}.dxf').read_bytes())
                literal(changed)
                literal_checks += 1
    changed = copy.deepcopy(changed)
    definition(changed)[next(i for i, tag in enumerate(definition(changed)) if tag[0] == 3)] = [3, r'literal \U+0041 Ω']
    try: literal(changed)
    except ValueError: corruptions += 1
    else: raise ValueError('Lossy CLASS escape was accepted')
    check(outputs == 24 and literal_checks == 24 and corruptions == 16, 'Opaque packet evidence inventory changed')
    print(f'Declared opaque entity gate: {outputs} full source/output packet checks; {literal_checks} CLASS literal checks; {corruptions} corruptions rejected. No native/proxy qualification claimed.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', nargs='?', type=Path, default=Path('artifacts/conformance'))
    verify(parser.parse_args().directory)
