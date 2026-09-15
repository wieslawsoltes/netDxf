#!/usr/bin/env python3
"""Independently check the three target-dependent omission examples emitted by conformance."""
import argparse
import copy
from pathlib import Path
from verify_sunstudy_producer import check, records, single

PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021', 2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}


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
    print(f'Target-version omission gate: {outputs} ASCII/binary outputs; {corruptions} physical-packet corruptions rejected.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('artifacts', nargs='?', type=Path, default=Path('artifacts/conformance'))
    verify(parser.parse_args().artifacts)
