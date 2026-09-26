#!/usr/bin/env python3
"""Independent exact-rational oracle for real AVERAGE results and saved TABLE values."""
import copy
from fractions import Fraction
import json
from pathlib import Path
import struct
import sys
import ezdxf
from verify_portable_double_parsing import require, reject, unique_object
from verify_editable_table_styles import records
# Retain the existing qualified exception for generated empty LAYER dictionaries.
# It validates the exact empty shapes; no TABLE/FIELD identity is normalized.
from verify_numeric_consumers import normalize_numeric_metadata as normalize_save_metadata
from verify_field_results import changed_field, compare as compare_fields, FIELDS
from verify_legacy_utilities import PROFILES
from verify_table_calculation import scalar_slots, compare
from verify_raw_line_geometry import audit_signature


def value(bits):
    return struct.unpack('>d', struct.pack('>Q', bits))[0]


def encoded(number):
    return struct.pack('>d', 0.0 if number == 0 else number).hex()


def specifications():
    rows = {}
    def add(name, inputs):
        numbers = tuple(inputs)
        exact = sum((Fraction.from_float(value(b)) for b in numbers), Fraction()) / len(numbers)
        rows[name] = ([f'{b:016x}' for b in numbers], encoded(float(exact)))
    for exponent in range(2047):
        lower = exponent << 52 | ((exponent * 0x1f123bb5) & ~1)
        for sign in range(2):
            mask = sign << 63
            a = lower | mask
            prefix = f'exponent/{exponent}/{sign}/'
            add(prefix + 'repeat', (a, a))
            add(prefix + 'even', (a, (lower + 1) | mask))
            add(prefix + 'odd', ((lower + 1) | mask, (lower + 2) | mask))
            add(prefix + 'cancel', (a, 0x3ff0000000000000 | mask, a ^ (1 << 63)))
    m, n = 0x7fefffffffffffff, 1 << 63
    edges = ((m,m), (m|n,m|n), (m,m,m|n), (0,1), (1,2),
             (0xfffffffffffff,0x10000000000000), (m,1,m|n), (m,3,m|n),
             (0,n), (3,3,0), (n|1,n|2), (m,))
    for index, inputs in enumerate(edges):
        add(f'edge/{index}', inputs)
    require(len(rows) == 16388, 'Independent input specification count')
    return rows


def check_row(row, expected):
    require(type(row) is dict and set(row) == {'id','inputs','results'}, 'Average observation schema')
    require(type(row['id']) is str and row['id'] in expected, 'Unknown average observation')
    inputs, output = expected[row['id']]
    require(row['inputs'] == inputs, 'Average input bits changed')
    require(type(row['results']) is list and row['results'] == [output] * 4,
            'Incorrect range/duplicate/scalar/reversed result: ' + row['id'])


def check_document(document, expected):
    require(type(document) is dict and set(document) == {'schema','observations'}
            and type(document['schema']) is int and document['schema'] == 1, 'Average report schema')
    rows = document['observations']
    require(type(rows) is list and len(rows) == len(expected), 'Average observation inventory')
    for row in rows:
        check_row(row, expected)
    require(len({row['id'] for row in rows}) == len(rows), 'Duplicate/missing average observation')


def verify_drawings(directory):
    names = {f'table-average-{binary}-{stage}.dxf' for binary in (False,True)
             for stage in ('source','output','resave')}
    def inventory(actual):
        require(actual == names, 'Average drawing inventory')
    inventory({p.name for p in directory.glob('table-average-*.dxf')})
    controls = reject(lambda: inventory(names - {next(iter(names))}))
    controls += reject(lambda: inventory(names | {'extra.dxf'}))
    for binary in (False,True):
        before = normalize_save_metadata(records(directory/f'table-average-{binary}-source.dxf'))
        wanted = copy.deepcopy(before)
        contents = [h for h,tags in before.items() if tags[0] == [0,'TABLECONTENT']]
        require(len(contents) == 1, 'Average backing-content inventory')
        h = contents[0]
        slots = scalar_slots(before[h])
        for address,(number,display) in slots.items():
            require(before[h][number] == [140,0.0], 'Average synthetic input is not zero')
            if address in ((0,0),(1,0),(2,0)):
                wanted[h][number] = [140,1e308]
                wanted[h][display] = [302,'1E+308']
        for stage in ('output','resave'):
            path = directory/f'table-average-{binary}-{stage}.dxf'
            require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == (binary != (stage == 'resave')), 'Average output transport')
            actual = normalize_save_metadata(records(path))
            compare(wanted,actual)
            for index,(code,v) in enumerate(actual[h]):
                bad = dict(actual);bad[h] = list(actual[h])
                if isinstance(v, float):
                    bits = struct.unpack('>Q',struct.pack('>d',v))[0] ^ 1
                    different = value(bits)
                else:
                    different = v+'_bad' if isinstance(v,str) else v+[999] if isinstance(v,list) else v+1
                bad[h][index] = [code,different]
                controls += reject(lambda: compare(wanted,bad))
            for handle in actual:
                bad = dict(actual); del bad[handle]
                controls += reject(lambda: compare(wanted,bad))
            require(not any(any(v.values()) for v in audit_signature(ezdxf.readfile(path))), 'Average drawing required audit repairs')
    return controls


def mean_field(tags, profile, number, display):
    # These explicit numeric displays are ASCII. Preserve the existing source/
    # cache oracle and independently supply its exact 250-character FIELD chunks.
    # Do not weaken the original helper's single-chunk fixture contract.
    require(display.isascii(), 'Unexpected non-ASCII mean display')
    result = changed_field(tags, profile, number, '', display)
    require(result[-2:] == [[301,''],[98,0]], 'Empty display oracle framing')
    return result[:-2] + [[301 if offset == 0 else 9, display[offset:offset+250]]
                         for offset in range(0,len(display),250)] + [[98,len(display)]]


def verify_fields(directory):
    names = {f'average-field-{v}-{b}-{i}-{stage}.dxf' for v in PROFILES
             for b in (False,True) for i in range(2) for stage in ('source','output','resave')}
    def inventory(actual):
        require(actual == names, 'Mean FIELD drawing inventory')
    inventory({p.name for p in directory.glob('average-field-*.dxf')})
    controls = reject(lambda: inventory(names - {next(iter(names))}))
    controls += reject(lambda: inventory(names | {'extra.dxf'}))
    for version, profile in PROFILES.items():
        for binary in (False,True):
            for scenario in range(2):
                expression = 'AVERAGE(1e308,1e308)' if scenario == 0 else 'AVERAGE(1e308,1,-1e308)'
                operands = (1e308,1e308) if scenario == 0 else (1e308,1.0,-1e308)
                mean = float(sum(map(Fraction.from_float, operands), Fraction()) / len(operands))
                display = format(mean,'.8f')
                prefix = f'average-field-{version}-{binary}-{scenario}'
                source = directory/(prefix+'-source.dxf')
                require(source.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Mean FIELD source transport')
                before = normalize_save_metadata(records(source))
                require({h for h,tags in before.items() if tags[0] == [0,'FIELD']} == set(FIELDS), 'Mean FIELD source inventory')
                tags = before['14F'];start = tags.index([100,'AcDbField'])
                code = '\\AcExpr ('+expression+') \\f "%lu2%pr8"'
                require(tags[start+1:start+3] == [[1,'AcExpr'],[2,code]], 'Mean FIELD source code')
                wanted = dict(before)
                wanted['14F'] = mean_field(before['14F'], profile, mean, display)
                wanted['14E'] = mean_field(before['14E'], profile, display, display)
                for stage in ('output','resave'):
                    path = directory/(prefix+'-'+stage+'.dxf')
                    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == (binary != (stage == 'resave')), 'Mean FIELD output transport')
                    actual = normalize_save_metadata(records(path));compare_fields(wanted,actual)
                    for handle in ('14E','14F'):
                        for index,(code,v) in enumerate(actual[handle]):
                            bad = dict(actual);bad[handle] = list(actual[handle])
                            different = value(struct.unpack('>Q',struct.pack('>d',v))[0]^1) if isinstance(v,float) else v+'_bad' if isinstance(v,str) else v+1
                            bad[handle][index] = [code,different]
                            controls += reject(lambda: compare_fields(wanted,bad))
                    doc = ezdxf.readfile(path)
                    require(doc.dxfversion == profile and not any(any(v.values()) for v in audit_signature(doc)), 'Mean FIELD profile or audit')
    return controls


def main(directory):
    path = directory/'table-average.json'
    require(path.stat().st_size <= 12_000_000, 'Average report too large')
    document = json.loads(path.read_text(encoding='utf-8-sig'),object_pairs_hook=unique_object)
    expected = specifications();check_document(document,expected)
    controls = 0
    for row in document['observations']:
        for column in range(4):
            bad = dict(row);bad['results'] = list(row['results'])
            bad['results'][column] = f'{int(row["results"][column],16)^1:016x}'
            controls += reject(lambda: check_row(bad,expected))
        bad = dict(row);bad['inputs'] = list(row['inputs']);bad['inputs'][0] = 'invalid'
        controls += reject(lambda: check_row(bad,expected))
    for mode in ('missing','extra','duplicate'):
        bad = copy.deepcopy(document)
        if mode == 'missing': bad['observations'].pop()
        elif mode == 'extra': bad['observations'].append(dict(bad['observations'][0]))
        else: bad['observations'][-1] = dict(bad['observations'][0])
        controls += reject(lambda: check_document(bad,expected))
    controls += reject(lambda: json.loads('{"schema":1,"schema":1}',object_pairs_hook=unique_object))
    controls += verify_drawings(directory)
    controls += verify_fields(directory)
    print(f'PASS: {len(expected)} exact-rational AVERAGE inputs through four paths; six TABLE and 72 FIELD drawings; '
          f'{controls} corruptions/inventory changes rejected. No native AutoCAD rounding-equivalence claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_table_average.py ARTIFACTS')
    main(Path(sys.argv[1]))
