#!/usr/bin/env python3
"""Require and independently verify all 24 DIMSTYLE parity outputs with ezdxf."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler

CODES = {'dimtsz': 142, 'dimtvp': 145, 'dimupt': 288, 'dimrnd': 45, 'dimalt': 170}
PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021', 2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}

def check(ok, message):
    if not ok: raise AssertionError(message)

def wire_tags(path):
    data = path.read_bytes()
    if data.startswith(b'AutoCAD Binary DXF'): return list(tag_compiler(binary_tags_loader(data)))
    # All non-ASCII source text is absent in this focused corpus.
    return list(tag_compiler(ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))))

def compare(path, manifest, fixture, binary, independent):
    check(path.is_file(), f'Missing required output: {path}')
    check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'transport')
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == PROFILES[fixture['year']], 'profile')
    audit = doc.audit()
    check(not audit.errors and not audit.fixes, f'Unexpected audit errors/repairs: {audit.errors}, {audit.fixes}')
    style = doc.dimstyles.get('PARITY')
    for name, value in manifest['style'].items(): check(style.dxf.get(name) == value, f'style {name}')
    header = manifest['header'] if independent else {'$DIMTSZ': 1.375, '$DIMTVP': -0.625, '$DIMUPT': 1}
    for name, value in header.items(): check(doc.header[name] == value, f'header {name}')
    dimensions = list(doc.modelspace().query('DIMENSION'))
    check(len(dimensions) == 2, 'dimension count')
    for index, (dim, expected) in enumerate(zip(dimensions, manifest['overrides'])):
        if independent: check(dim.dxf.handle == fixture['dimension_handles'][index], 'dimension identity')
        overrides = dim.get_acad_dstyle(style)
        for name, value in expected.items(): check(overrides[name] == value, f'override {index}/{name}')
        # Check the payload pairs, not merely ezdxf defaults or effective style values.
        data = list(dim.get_xdata('ACAD'))
        for name, value in expected.items():
            pairs = [data[i+1] for i,t in enumerate(data[:-1]) if t.code == 1070 and t.value == CODES[name]]
            check(len(pairs) == 1, f'unique DSTYLE value {name}')
            check(pairs[0].code == (1070 if name in ('dimupt', 'dimalt') else 1040) and pairs[0].value == value, f'DSTYLE wire type/value {name}')
    tags = wire_tags(path)
    for name, value in header.items():
        positions = [i for i,t in enumerate(tags) if t.code == 9 and t.value == name]
        check(len(positions) == 1, f'duplicate/absent header {name}')
        tag = tags[positions[0]+1]
        check(tag.code == (70 if name == '$DIMUPT' else 40) and tag.value == value, f'header wire value {name}')
    records = []; current = []
    for tag in tags:
        if tag.code == 0:
            if current: records.append(current)
            current = []
        current.append(tag)
    records.append(current)
    tables = [r for r in records if r[0].value == 'DIMSTYLE' and any(t.code == 2 and t.value == 'PARITY' for t in r)]
    check(len(tables) == 1, 'one named style')
    for name, value in manifest['style'].items():
        values = [t.value for t in tables[0] if t.code == CODES[name]]
        check(values == [value], f'DIMSTYLE stored wire field {name}')
    if independent:
        line = doc.entitydb[fixture['line_handle']]
        check(tuple(line.dxf.start) == (17.25, -4.5, 2.0) and tuple(line.dxf.end) == (18.5, 9.25, -3.0), 'following line')


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('artifacts', type=Path); args = parser.parse_args()
    source = Path(__file__).resolve().parents[1] / 'tests/fixtures/dimstyle-stored-settings'
    manifest = json.loads((source/'manifest.json').read_text())
    check(manifest['producer'] == 'ezdxf 1.4.4', 'qualified independent producer')
    check(len(manifest['fixtures']) == 6 and {f['year'] for f in manifest['fixtures']} == set(PROFILES), 'complete unique fixture profile set')
    expected_names = {name for year in PROFILES for transport in ('ascii', 'binary') for name in (
        f'independent-dimstyle-R{year}-{transport}.dxf', f'dimstyle-parity-AutoCad{year}-{transport}.dxf')}
    actual_names = {p.name for prefix in ('independent-dimstyle-', 'dimstyle-parity-') for p in args.artifacts.glob(prefix+'*.dxf')}
    check(actual_names == expected_names, f'Exact 24-output corpus required; missing={expected_names-actual_names}, unexpected={actual_names-expected_names}')
    count = 0
    for fixture in manifest['fixtures']:
        check(fixture['file'] == f"independent-dimstyle-R{fixture['year']}.dxf", 'source fixture profile name')
        original = source/fixture['file']
        check(hashlib.sha256(original.read_bytes()).hexdigest() == fixture['sha256'], 'source provenance')
        for binary in (False, True):
            transport = 'binary' if binary else 'ascii'; year = fixture['year']
            for independent in (False, True):
                name = f'independent-dimstyle-R{year}-{transport}.dxf' if independent else f'dimstyle-parity-AutoCad{year}-{transport}.dxf'
                path = args.artifacts/name
                compare(path, manifest, fixture, binary, independent)
                print(f'PASS {path.name}'); count += 1
    check(count == 24, 'complete version/transport/producer matrix')
    print(f'DIMSTYLE stored settings: {count} required files passed; no audit errors or repairs.')

if __name__ == '__main__': main()
