#!/usr/bin/env python3
"""Require exact native R2004 composite ownership packets in all eight outputs.

The four maps explicitly identify the rebound external owner, opaque table
caches, DATATABLE and its 41 native XRECORD children. No display/evaluation claim.
"""
import argparse
import copy
import gzip
import hashlib
import io
import json
from pathlib import Path
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from verify_fourth_mixed_modules import check, load, metadata, dictionary_edges, xdata
from verify_stored_fields import payload

ROOT = Path(__file__).resolve().parents[1]
SOURCE = 'sample_AC1018_ascii.dxf'


def source_records():
    entry = next(f for f in json.loads((ROOT/'tools/table_oracle/fixtures.json').read_text())['files'] if f['file'] == SOURCE)
    raw = gzip.decompress((ROOT/'tests/fixtures/table-oracle'/f'{SOURCE}.gz').read_bytes())
    check(hashlib.sha256(raw).hexdigest() == entry['sha256'], 'Native composite source SHA256 differs')
    records, current = {}, []
    def finish():
        identity = next((v for c,v in current if c == 5),None)
        if identity is not None: records[identity] = list(current)
    for tag in tag_compiler(ascii_tags_loader(io.StringIO(raw.decode('cp1252'), newline=None))):
        if tag.code == 0: finish(); current = []
        current.append((tag.code,tag.value))
    finish()
    return records


def inspect(records, source, mapping, wrapper, phase):
    check(set(mapping) == {'source','wrapper','sourceOwner','carrierOwner','content','geometry','dataTable','rowObjects'}, 'Composite map keys differ')
    check(mapping['source'] == SOURCE and mapping['wrapper'] == wrapper, 'Composite source/map identity differs')
    source_wrapper = source[wrapper]
    direct = [v for c,v in source_wrapper[next(i for i,t in enumerate(source_wrapper) if t[0] == 100):] if c in (360,361)]
    check(direct == [mapping[k] for k in ('content','geometry','dataTable')], 'Composite direct owner slots differ')
    rows = [v for c,v in source[direct[2]] if c == 360]
    check(rows == mapping['rowObjects'] and len(rows) == (21 if wrapper == '13DF' else 20), 'Composite DATATABLE row map differs')
    check(mapping['sourceOwner'] == metadata(source_wrapper)[0], 'Composite external source owner differs')
    root = mapping['carrierOwner']
    check(records[root][0] == (0,'DICTIONARY'), 'Composite carrier owner is not a dictionary')
    check(('NATIVE_COMPOSITE',360,wrapper) in dictionary_edges(records[root]), 'Composite carrier owner entry differs')
    check(metadata(records[wrapper])[:2] == (root,[root]), 'Composite common owner/reactor mapping differs')
    expected = [wrapper] + direct + rows
    check(len(set(expected)) == len(expected), 'Composite source identities collapsed')
    for handle in expected:
        check(handle in records, 'Native composite descendant missing: '+handle)
        check(records[handle][0] == source[handle][0], 'Native composite object type differs: '+handle)
        check(payload(records[handle]) == payload(source[handle]), 'Native composite subclass packet differs: '+handle)
        if handle != wrapper:
            check(metadata(records[handle]) == metadata(source[handle]), 'Native composite descendant ownership/metadata differs: '+handle)
            check(not xdata(records[handle]), 'Native composite descendant gained XData')
    check(records[direct[0]][0] == (0,'TABLECONTENT') and records[direct[1]][0] == (0,'TABLEGEOMETRY') and records[direct[2]][0] == (0,'DATATABLE'), 'Composite direct child classes differ')
    check(all(records[h][0] == (0,'XRECORD') for h in rows), 'Native DATATABLE child type differs')
    check(all(metadata(records[h])[0] == wrapper for h in direct), 'Composite direct ownership is not reciprocal')
    check(all(metadata(records[h])[0] == direct[2] for h in rows), 'DATATABLE row ownership is not reciprocal')
    expected_xdata = [(1001,'COMPOSITE_TEST'),(1000,'common metadata only')] if phase == 'edited' else []
    check(xdata(records[wrapper]) == expected_xdata, 'Composite common metadata edit differs')


def change(rows, code, value, last=False):
    slots = [i for i,t in enumerate(rows) if t[0] == code]
    check(slots, 'Corruption control lacks its target code')
    index = slots[-1] if last else slots[0]
    rows[index] = (code,value)


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory',type=Path); args=parser.parse_args()
    expected = {f'composite-table-{wrapper}-{binary}-{phase}.dxf' for wrapper in ('13DF','13F2') for binary in (False,True) for phase in ('native','edited')}
    maps = {f'composite-table-{wrapper}-{binary}-map.json' for wrapper in ('13DF','13F2') for binary in (False,True)}
    check({p.name for p in args.directory.glob('composite-table-*.dxf')} == expected, 'All eight composite TABLE drawings are mandatory')
    check({p.name for p in args.directory.glob('composite-table-*-map.json')} == maps, 'All four composite TABLE maps are mandatory')
    source=source_records(); controls=0
    for name in sorted(expected):
        wrapper,binary,phase = name.removeprefix('composite-table-').removesuffix('.dxf').split('-'); binary=binary=='True'
        mapping=json.loads((args.directory/f'composite-table-{wrapper}-{binary}-map.json').read_text())
        _,records,_ = load(args.directory/name,2004,binary)
        inspect(records,source,mapping,wrapper,phase)
        for variant in range(10):
            bad=copy.deepcopy(records); bad_map=copy.deepcopy(mapping); table=mapping['dataTable']; row=mapping['rowObjects'][0]
            if variant == 0: change(bad[wrapper],102,'PRIVATE_TABLE',True)
            if variant == 1: change(bad[wrapper],360,row)
            if variant == 2: change(bad[table],330,mapping['carrierOwner'])
            if variant == 3: change(bad[row],330,wrapper)
            if variant == 4: bad.pop(row)
            if variant == 5: change(bad[table],91,999)
            if variant == 6: change(bad[table],360,mapping['geometry'])
            if variant == 7: change(bad[wrapper],91,999)
            if variant == 8: bad_map['rowObjects'].reverse()
            if variant == 9: bad[wrapper].append((1000,'unexpected common data')) if phase == 'edited' else bad[wrapper].extend([(1001,'COMPOSITE_TEST'),(1000,'unexpected')])
            try: inspect(bad,source,bad_map,wrapper,phase)
            except (ValueError,KeyError,StopIteration): controls+=1
            else: raise ValueError(f'Composite corruption control {variant} was accepted for {name}')
        print('PASS '+name)
    check(controls == 80,'Composite corruption inventory differs')
    print('PASS eight native composite TABLE drawings, four explicit owner maps, 41 source XRECORD descendants, 80 corruption controls')


if __name__ == '__main__': main()
