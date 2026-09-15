#!/usr/bin/env python3
"""Check target-dependent omission examples and accepted Polyface profile exports."""
import argparse
import copy
from pathlib import Path
import ezdxf
from verify_sunstudy_producer import check, records, single

PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021', 2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}


def verify_polyface_rows(rows, year):
    header = next(row for row in rows if row[:2] == [[0, 'SECTION'], [2, 'HEADER']])
    version = header.index([9, '$ACADVER'])
    check(header[version + 1] == [1, PROFILES[year]], 'Polyface target profile differs')
    chain = [row for row in rows if row[0] in ([0, 'POLYLINE'], [0, 'VERTEX'], [0, 'SEQEND'])]
    check([row[0][1] for row in chain] == ['POLYLINE'] + ['VERTEX'] * 4 + ['SEQEND'], 'Polyface child inventory/order')
    values = lambda row, code: [value for tag, value in row if tag == code]
    identities = [values(row, 5) for row in chain]
    check(all(len(item) == 1 and int(item[0], 16) != 0 for item in identities), 'Polyface physical identities')
    check(len({int(item[0], 16) for item in identities}) == 6, 'Polyface duplicate child identities')
    parent = identities[0][0]
    check(values(chain[0], 70) == [64], 'Polyface parent mode')
    for row in chain[1:]:
        check(values(row, 330) == [parent], 'Polyface child source owner')
    for row, point in zip(chain[1:4], ([0., 0., 0.], [1., 0., 0.], [0., 1., 0.])):
        check(values(row, 70) == [192] and values(row, 10) == [point], 'Polyface coordinate value or role')
    face = chain[4]
    check(values(face, 70) == [128], 'Polyface face role')
    check([(code, value) for code, value in face if 71 <= code <= 74] == [(71, 1), (72, -2), (73, 3)], 'Polyface signed face topology')
    return header, chain


def verify_polyface(artifacts):
    expected = {f'compatibility-polyface-{retained}-AutoCad{year}-{binary}.dxf': year
                for year in PROFILES for binary in (False, True) for retained in (False, True)
                if not retained or year == 2010}
    check({p.name for p in artifacts.glob('compatibility-polyface-*.dxf')} == set(expected), 'Accepted Polyface profile output inventory')
    controls = 0
    for name, year in expected.items():
        path = artifacts / name
        data = path.read_bytes()
        check(data.startswith(b'AutoCAD Binary DXF') == name.endswith('-True.dxf'), 'Polyface output transport')
        original = records(data)
        verify_polyface_rows(original, year)
        audit = ezdxf.readfile(path).audit()
        check(not audit.errors and not audit.fixes, 'Polyface output audit')
        for fault in ('profile', 'coordinate', 'face-sign', 'owner', 'sequence-end'):
            rows = copy.deepcopy(original)
            header, chain = verify_polyface_rows(rows, year)
            if fault == 'profile':
                index = header.index([9, '$ACADVER'])
                header[index + 1] = [1, 'AC1018' if year == 2000 else 'AC1015']
            elif fault == 'coordinate':
                next(tag for tag in chain[1] if tag[0] == 10)[1][0] = .25
            elif fault == 'face-sign':
                next(tag for tag in chain[4] if tag[0] == 72)[1] = 2
            elif fault == 'owner':
                next(tag for tag in chain[2] if tag[0] == 330)[1] = '0'
            else:
                rows.remove(chain[-1])
            try:
                verify_polyface_rows(rows, year)
            except ValueError:
                controls += 1
            else:
                raise ValueError(f'Accepted Polyface packet corruption: {name}/{fault}')
    check(len(expected) == 14 and controls == 70, 'Polyface evidence inventory changed')
    return len(expected), controls


def verify_rows(rows, year):
    header = next(row for row in rows if row[:2] == [[0, 'SECTION'], [2, 'HEADER']])
    version = next(i for i, tag in enumerate(header) if tag == [9, '$ACADVER'])
    check(header[version + 1] == [1, PROFILES[year]], 'Target profile differs')
    hatch = next(row for row in rows if row[0] == [0, 'HATCH'])
    definition = next(row for row in rows if row[0] == [0, 'CLASS'] and [1, 'COMPATIBILITY_CLASS'] in row)
    retained = year > 2000
    saved_by = [i for i, tag in enumerate(header) if tag == [9, '$LASTSAVEDBY']]
    check(len(saved_by) == int(retained), 'LastSavedBy omission prediction differs from output')
    if retained:
        check(header[saved_by[0] + 1] == [1, 'Compatibility author'], 'LastSavedBy value changed')
    gradient = [value for code, value in hatch if code == 450]
    check(gradient == ([1] if retained else []), 'Gradient omission prediction differs from output')
    count = [value for code, value in definition if code == 91]
    check(count == ([0] if retained else []), 'Explicit-zero CLASS count omission differs from output')
    return header, hatch, definition


def verify(artifacts):
    outputs = 0
    for year in PROFILES:
        for binary in (False, True):
            name = f'compatibility-omissions-AutoCad{year}-{binary}.dxf'
            data = (artifacts / name).read_bytes()
            check(data.startswith(b'AutoCAD Binary DXF') == binary, 'Omission output transport differs')
            verify_rows(records(data), year)
            outputs += 1
    corruptions = 0
    for year in (2000, 2018):
        original = records((artifacts / f'compatibility-omissions-AutoCad{year}-False.dxf').read_bytes())
        for fault in ('version', 'saved-by-presence', 'gradient-presence', 'class-count-presence', 'class-count-value'):
            rows = copy.deepcopy(original)
            header, hatch, definition = verify_rows(rows, year)
            if fault == 'version':
                index = header.index([9, '$ACADVER']); header[index + 1] = [1, 'AC1015' if year == 2018 else 'AC1032']
            elif fault == 'saved-by-presence':
                if year == 2000: header.extend([[9, '$LASTSAVEDBY'], [1, 'Compatibility author']])
                else:
                    index = header.index([9, '$LASTSAVEDBY']); del header[index:index + 2]
            elif fault == 'gradient-presence':
                if year == 2000: hatch.append([450, 1])
                else: hatch[:] = [tag for tag in hatch if tag[0] != 450]
            elif fault == 'class-count-presence':
                if year == 2000: definition.append([91, 0])
                else: definition[:] = [tag for tag in definition if tag[0] != 91]
            else:
                definition[:] = [tag for tag in definition if tag[0] != 91]; definition.append([91, 73])
            try: verify_rows(rows, year)
            except ValueError: corruptions += 1
            else: raise ValueError(f'Mutation was accepted: {year}/{fault}')
    check(outputs == 12 and corruptions == 10, 'Omission evidence inventory changed')
    polyface_outputs, polyface_controls = verify_polyface(artifacts)
    print(f'Target-version gate: {outputs} omission and {polyface_outputs} Polyface ASCII/binary outputs; {corruptions + polyface_controls} physical-packet corruptions rejected.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('artifacts', nargs='?', type=Path, default=Path('artifacts/conformance'))
    verify(parser.parse_args().artifacts)
