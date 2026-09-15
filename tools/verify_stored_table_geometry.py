#!/usr/bin/env python3
"""Verify stored TABLEGEOMETRY packets, native ownership and explicit projections."""
import argparse
import copy
import json
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    sys.path.insert(0, str(args.repository / 'tools'))
    from verify_fourth_mixed_modules import load, metadata, dictionary_edges
    from verify_stored_table_content import FILES, source, payload, check, verify_extraction, plain_records, verify_carrier_graph

    def geometry_records(records):
        return {h: r for h, r in records.items() if r[0] == (0, 'TABLEGEOMETRY')}

    def verify_native(records, classes, original):
        expected = geometry_records(original)
        check(set(geometry_records(records)) == set(expected), 'Native TABLEGEOMETRY identity inventory changed')
        for handle, before in expected.items():
            check(payload(records[handle]) == payload(before), 'Exact native geometry payload changed')
            owner = metadata(before)[0]
            check(metadata(records[handle])[0] == owner, 'Native geometry owner changed')
            check(owner in records and records[owner][0] == (0, 'XRECORD'), 'Native wrapper identity changed')
            check(payload(records[owner]) == payload(original[owner]), 'Native wrapper payload changed')
        definitions = [r for r in classes if (1, 'TABLEGEOMETRY') in r]
        check(len(definitions) == 1, 'Missing TABLEGEOMETRY class')
        check(all(t in definitions[0] for t in [(2, 'AcDbTableGeometry'), (3, 'ObjectDBX Classes'), (90, 1152), (91, len(expected)), (280, 0), (281, 0)]), 'Geometry CLASS metadata changed')

    def synthetic():
        return [(100,'AcDbTableGeometry'),(90,1),(91,1),(92,1),(93,7),(40,10.5),(41,20.25),(330,'0'),(94,1),
                (10,(1.0,2.0,3.0)),(11,(4.0,5.0,6.0)),(43,7.0),(44,8.0),(45,9.0),(46,10.0),(95,11)]

    def verify_schema(records, classes):
        rows = geometry_records(records)
        check(len(rows) == 1, 'Synthetic geometry inventory changed')
        handle, row = next(iter(rows.items()))
        start = row.index((100,'AcDbTableGeometry'))
        check(row[start:] == synthetic(), 'Stored scalar or vector projection changed')
        owner = metadata(row)[0]
        check(owner in records and ('GEOMETRY',360,handle) in dictionary_edges(records[owner]), 'Synthetic owner identity changed')
        definitions = [r for r in classes if (1,'TABLEGEOMETRY') in r]
        check(len(definitions) == 1 and (90,1152) in definitions[0] and (91,1) in definitions[0], 'Synthetic CLASS changed')

    def verify_opaque(records, variant):
        rows = geometry_records(records)
        check(len(rows) == 1, 'Opaque geometry inventory changed')
        handle, row = next(iter(rows.items()))
        body = synthetic(); prefix = []
        if variant == 1: body[0]=(100,'PrivateTableGeometry')
        if variant == 2: body.append((100,'PrivateGeometryExtension'))
        if variant == 3: body.append((300,'private field'))
        if variant == 4: body[1:1]=[(102,'{PRIVATE'),(1000,'private value'),(102,'}')]
        if variant == 5: body += [(100,'PrivateGeometryExtension'),(1000,'private value')]
        if variant == 6: prefix=[(102,'{PRIVATE'),(1000,'private header'),(102,'}')]
        if variant == 7: body.append((320,'EEEEEEEE'))
        start=next(i for i,t in enumerate(row) if t[0]==100)
        check([t for t in row[:start] if t[0] not in (0,5,330)] == prefix and row[start:] == body, 'Opaque geometry packet changed')
        depth=0; owner=None
        for code,value in row[:start]:
            if code==102: depth += 1 if value.startswith('{') else -1
            elif code==330 and depth==0: owner=value
        check(owner in records and ('GEOMETRY',360,handle) in dictionary_edges(records[owner]), 'Opaque owning dictionary changed')

    expected = {f'table-geometry-native-{f}-{b}.dxf' for f in FILES for b in (False,True)}
    expected |= {f'table-geometry-schema-AutoCad{year}-{b}.dxf' for year in (2004,2007,2010,2013,2018) for b in (False,True)}
    expected |= {f'table-geometry-opaque-{v}-{b}.dxf' for v in range(8) for b in (False,True)}
    check({p.name for p in args.directory.glob('table-geometry-*.dxf')} == expected, 'All 36 geometry outputs are mandatory')
    manifest=json.loads((args.repository/'tests/fixtures/table-content/manifest.json').read_text())
    selected=sum(verify_extraction(args.repository, row) for row in manifest['files'])
    check(selected==316,'Source carrier inventory changed')
    controls=0; packets=0; selected_appearances=0
    for file in FILES:
        original, _, year=source(args.repository,file)
        entry=next(r for r in manifest['files'] if r['file']==file)
        selected_rows=entry.get('records',[])
        carrier=plain_records((args.repository/'tests/fixtures/table-content'/entry['fixture']).read_bytes(),entry['profile']) if selected_rows else {}
        for binary in (False,True):
            name=f'table-geometry-native-{file}-{binary}.dxf'
            doc,records,classes=load(args.directory/name,year,binary)
            audit=doc.audit();check(not audit.errors and not audit.fixes,'Native geometry output audit failed')
            verify_native(records,classes,original);verify_carrier_graph(records,carrier,selected_rows)
            selected_appearances+=len(selected_rows);packets+=len(geometry_records(original))
            for fault in range(8):
                bad=copy.deepcopy(records);bc=copy.deepcopy(classes);handle=next(iter(geometry_records(bad)));row=bad[handle]
                code=(90,91,92,93,40,94)[fault] if fault<6 else None
                if code is not None:
                    i=next(i for i,t in enumerate(row) if t[0]==code);row[i]=(code,9876.5 if code==40 else 9876)
                elif fault==6: bad.pop(metadata(row)[0])
                else: next(r for r in bc if (1,'TABLEGEOMETRY') in r).append((91,9876)); bc=[]
                try:verify_native(bad,bc,original)
                except (ValueError,KeyError,StopIteration):controls+=1
                else:raise ValueError('Native geometry corruption accepted')
            print('PASS '+name)
    for year in (2004,2007,2010,2013,2018):
        for binary in (False,True):
            name=f'table-geometry-schema-AutoCad{year}-{binary}.dxf';doc,records,classes=load(args.directory/name,year,binary)
            audit=doc.audit();check(not audit.errors and not audit.fixes,'Synthetic geometry output audit failed')
            verify_schema(records,classes)
            for code in (90,93,40,10,11,43,46,95):
                bad=copy.deepcopy(records);row=next(iter(geometry_records(bad).values()));i=next(i for i,t in enumerate(row) if t[0]==code)
                row[i]=(code,(8.0,9.0,10.0) if code in (10,11) else 999)
                try:verify_schema(bad,classes)
                except (ValueError,KeyError,StopIteration):controls+=1
                else:raise ValueError('Synthetic geometry corruption accepted')
            print('PASS '+name)
    for variant in range(8):
        for binary in (False,True):
            name=f'table-geometry-opaque-{variant}-{binary}.dxf';doc,records,_=load(args.directory/name,2000 if variant==0 else 2018,binary)
            audit=doc.audit();check(not audit.errors and not audit.fixes,'Opaque geometry output audit failed')
            verify_opaque(records,variant)
            for code in (93,95):
                bad=copy.deepcopy(records);row=next(iter(geometry_records(bad).values()));i=next(i for i,t in enumerate(row) if t[0]==code);row[i]=(code,999)
                try:verify_opaque(bad,variant)
                except (ValueError,KeyError,StopIteration):controls+=1
                else:raise ValueError('Opaque geometry corruption accepted')
            print('PASS '+name)
    check(packets==16 and selected_appearances==632 and controls==192,'Geometry verification inventory changed')
    print('PASS 36 outputs, 16 native geometry packets, 632 carrier record appearances, 192 actual corruptions rejected; zero audit errors or repairs')

if __name__=='__main__':main()
