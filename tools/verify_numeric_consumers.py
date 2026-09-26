#!/usr/bin/env python3
"""Check real TABLE/FIELD numeric consumers against exact independent input oracles."""
import copy
import json
from pathlib import Path
import struct
import sys
from verify_portable_double_parsing import specifications, require, reject, unique_object
from verify_editable_table_styles import records, normalize_save_metadata
from verify_field_results import changed_field, compare, FIELDS
from verify_legacy_utilities import PROFILES
from verify_raw_line_geometry import audit_signature
import ezdxf

IDS = ('exponent/0/1/0', 'tail/4/-1/0', 'tail/7/1/0')
FIELDS_JSON = {'id', 'token', 'scalarBits', 'tableBits', 'factorBits'}


def expected_inputs():
    return {key:(token,bits) for key,(token,bits) in specifications().items()
            if bits is not None and len(token) <= 4050 and not any(c.isspace() for c in token)}


def check_row(row, expected):
    require(type(row) is dict and set(row) == FIELDS_JSON, 'Consumer row schema')
    require(type(row['id']) is str and row['id'] in expected, 'Consumer input identity')
    token, value = expected[row['id']]
    require(row['token'] == token, 'Consumer input changed')
    zero = int(value,16) & 0x7fffffffffffffff == 0
    require(row['scalarBits'] == row['tableBits'] == ('0000000000000000' if zero else value), 'Formula literal bits differ')
    require(row['factorBits'] == value, 'Conversion-factor literal bits differ')


def check_document(document, expected):
    require(type(document) is dict and set(document) == {'schema','observations'} and type(document['schema']) is int and document['schema'] == 1, 'Consumer report schema')
    rows = document['observations']
    require(type(rows) is list and len(rows) == len(expected) == 12375, 'Consumer inventory')
    for row in rows: check_row(row, expected)
    require(len({r['id'] for r in rows}) == len(rows), 'Duplicate/missing consumer identity')



def normalize_numeric_metadata(items):
    # Existing writer hydration recreates the empty LAYER extension and its empty
    # ACAD_LAYERSTATES child. Verify this exact generated shape before excluding
    # their identities. FIELD, host and every other record identity stays exact.
    # This numeric-consumer check does not qualify generated dictionary identity.
    items = normalize_save_metadata(items)
    for handle, row in list(items.items()):
        if row[:2] != [[0, 'TABLE'], [2, 'LAYER']]:
            continue
        marker = [102, '{ACAD_XDICTIONARY']
        if marker not in row:
            continue
        at = row.index(marker)
        require(row[at+1][0] == 360 and row[at+2] == [102, '}'], 'LAYER extension framing')
        child = row[at+1][1]
        expected = [[0, 'DICTIONARY'], [5, child], [330, handle], [100, 'AcDbDictionary'],
                    [280, 1], [281, 1], [3, 'ACAD_LAYERSTATES'], [360, '<generated-empty-layer-states>']]
        require(items.get(child) == expected, 'Generated LAYER extension is not the known empty shape')
        row[at+1] = [360, '<generated-layer-extension>']
        expected[1] = [5, '<generated-layer-extension>']
        items = {('<generated-layer-extension>' if key == child else key):
                 (expected if key == child else value) for key, value in items.items()}
    return items


def verify_drawings(directory, expected):
    wanted_names = {f'numeric-field-{v}-{b}-{i}-{stage}.dxf' for v in PROFILES
                    for b in (False,True) for i in range(3) for stage in ('source','output','resave')}
    require({p.name for p in directory.glob('numeric-field-*.dxf')} == wanted_names, 'FIELD drawing inventory')
    controls = 0
    for version, profile in PROFILES.items():
        for binary in (False,True):
            for i, key in enumerate(IDS):
                token, value_bits = expected[key]
                value = struct.unpack('>d', bytes.fromhex(value_bits))[0]
                display = format(value, '.8f')
                prefix = f'numeric-field-{version}-{binary}-{i}'
                before = normalize_numeric_metadata(records(directory / (prefix+'-source.dxf')))
                require({h for h,r in before.items() if r[0] == [0,'FIELD']} == set(FIELDS), 'Source FIELD inventory')
                tags = before['14F']; start = tags.index([100,'AcDbField'])
                code = '\\AcExpr ('+token+') \\f "%lu2%pr8"'
                chunks = [[2 if offset == 0 else 3,code[offset:offset+250]] for offset in range(0,len(code),250)]
                require(tags[start+1] == [1,'AcExpr'] and tags[start+2:start+2+len(chunks)] == chunks, 'Source FIELD numeric code/chunks')
                wanted = dict(before)
                wanted['14F'] = changed_field(before['14F'], profile, value, display, display)
                wanted['14E'] = changed_field(before['14E'], profile, display, display, display)
                for stage in ('output','resave'):
                    path = directory / (prefix+'-'+stage+'.dxf')
                    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == (binary if stage == 'output' else not binary), 'FIELD output transport')
                    actual = normalize_numeric_metadata(records(path)); compare(wanted,actual)
                    for handle in ('14E','14F'):
                        for index,(code,v) in enumerate(actual[handle]):
                            bad=dict(actual); bad[handle]=list(actual[handle]); bad[handle][index]=[code,v+'_bad' if isinstance(v,str) else v+1]
                            controls += reject(lambda: compare(wanted,bad))
                    require(not any(any(v.values()) for v in audit_signature(ezdxf.readfile(path))), 'Independent FIELD graph audit')
    return len(wanted_names), controls


def main(directory):
    expected=expected_inputs(); path=directory/'numeric-consumers.json'
    require(path.stat().st_size < 32_000_000, 'Consumer report size')
    document=json.loads(path.read_text(), object_pairs_hook=unique_object)
    check_document(document,expected); controls=0
    for row in document['observations']:
        for field in ('scalarBits','tableBits','factorBits'):
            bad=dict(row); bad[field]=f'{int(row[field],16)^1:016x}'
            controls += reject(lambda:check_row(bad,expected))
        bad=dict(row);bad['token']+='0';controls += reject(lambda:check_row(bad,expected))
    for mode in ('remove','duplicate','extra'):
        bad=copy.deepcopy(document)
        if mode=='remove': bad['observations'].pop()
        elif mode=='duplicate': bad['observations'][-1]=dict(bad['observations'][0])
        else: bad['observations'].append(dict(bad['observations'][0]))
        controls += reject(lambda:check_document(bad,expected))
    drawings, rejected=verify_drawings(directory,expected)
    print(f'PASS: {len(expected)} input-only scalar/table/factor observations; {drawings} FIELD drawings; '
          f'{controls+rejected} corruptions/inventory changes rejected. No native AutoCAD acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv)==2,'Usage: verify_numeric_consumers.py ARTIFACTS');main(Path(sys.argv[1]))
