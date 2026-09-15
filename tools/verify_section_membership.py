#!/usr/bin/env python3
"""Validate explicit SECTION_MANAGER public membership edits and native edit boundaries."""
import argparse
import copy
import gzip
import hashlib
import json
from pathlib import Path
import tempfile
from verify_fourth_mixed_modules import load, metadata, dictionary_edges
from verify_section_manager import manager, packet, check, YEARS, SPELLINGS

def validate(records, classes, spelling, mode, native=None):
    handle, row = manager(records)
    check(row[0] == (0,spelling), 'Manager spelling changed')
    body = packet(row)
    count = 0 if mode == 'empty' else 2 if mode == 'native' else 3
    flag = 0 if mode == 'edited' else 1
    check(body[:3] == [(100,'AcDbSectionManager'),(70,flag),(90,count)], 'Independent update flag/count changed')
    check(len(body) == count+3 and all(c == 330 for c,v in body[3:]), 'Public pointer-list grammar changed')
    owner, reactors, extension = metadata(row)
    check(owner in records and records[owner][0] == (0,'DICTIONARY'), 'Actual manager owner missing')
    check(('ACAD_SECTION_MANAGER',350,handle) in dictionary_edges(records[owner]), 'Original root anchor changed')
    check(metadata(records[owner])[0] in (None,'0'), 'Manager anchor is not the root')
    targets = [v for c,v in body[3:]]
    for target in targets:
        check(target in records and records[target][0] == (0,'SECTIONOBJECT'), 'Membership target is not an actual section')
    if mode == 'native':
        source = native[handle]
        prefix = source[:next(i for i,t in enumerate(source) if t[0] == 100)]
        expected = prefix + [(100,'AcDbSectionManager'),(70,1),(90,2),(330,'228'),(330,'228')]
        check(row == expected and handle == '229', 'Native manager changed outside explicit public list/flag edit')
        check(reactors == ['C'] and targets == ['228','228'], 'Native reactor/target identity changed')
        check(metadata(records['22A'])[0] == '228', 'Native settings reciprocal owner changed')
    else:
        check(not reactors and extension is None, 'Membership edit invented common metadata')
        names = [next(v for c,v in packet(records[h]) if c == 1) for h in targets]
        expected = {'edited':['Manager second','Manager third','Manager second'],
                    'mapped':['Manager second','Manager first','Manager second'], 'empty':[]}[mode]
        check(names == expected, 'Ordered membership/names changed')
        if targets: check(targets[0] == targets[2] and targets[0] != targets[1], 'Repeated identity was collapsed/rebound')
        sections = [r for r in records.values() if r[0] == (0,'SECTIONOBJECT')]
        check(len(sections) == (0 if mode == 'empty' else 2), 'Released/retained section inventory differs')
    definitions = [r for r in classes if (1,spelling) in r]
    check(len(definitions) == 1, 'Manager CLASS inventory changed')
    for tag in [(2,'AcDbSectionManager'),(3,'ObjectDBX Classes'),(90,1024),(91,1),(280,0),(281,0)]:
        check(tag in definitions[0], 'Manager CLASS metadata changed')

def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('artifacts',type=Path); args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]; fixture = root/'tests/fixtures/section'
    manifest = json.loads((fixture/'manifest.json').read_text()); packed = (fixture/'LiveSection1.dxf.gz').read_bytes(); original = gzip.decompress(packed)
    check(hashlib.sha256(packed).hexdigest() == manifest['gzip_sha256'] and hashlib.sha256(original).hexdigest() == manifest['source_sha256'], 'Pinned native source identity changed')
    with tempfile.TemporaryDirectory() as temporary:
        path = Path(temporary)/'original.dxf'; path.write_bytes(original); _,native,_ = load(path,2018,False)
    cases = [(f'section-membership-{mode}-{version}-{spelling}-{binary}.dxf',year,binary,spelling,mode,None)
             for version,year in YEARS.items() for spelling in SPELLINGS for binary in (False,True) for mode in ('edited','empty','mapped')]
    cases += [(f'section-membership-native-{input_binary}-{binary}.dxf',2018,binary,'SECTION_MANAGER','native',native)
              for input_binary in (False,True) for binary in (False,True)]
    check({p.name for p in args.artifacts.glob('section-membership-*.dxf')} == {c[0] for c in cases}, 'All52edited manager outputs required')
    controls = 0
    for name,year,binary,spelling,mode,source in cases:
        doc,records,classes = load(args.artifacts/name,year,binary); validate(records,classes,spelling,mode,source)
        audit = doc.audit(); check(not audit.errors and not audit.fixes,'Edited manager requires audit repair: '+name)
        for defect in ('flag','count','root','class','target'):
            bad = copy.deepcopy(records); bad_classes = copy.deepcopy(classes); handle,row = manager(bad); start = row.index((100,'AcDbSectionManager'))
            if defect == 'flag': row[start+1] = (70,1-row[start+1][1])
            elif defect == 'count': row[start+2] = (90,row[start+2][1]+1)
            elif defect == 'root':
                owner = metadata(row)[0]; bad[owner] = [(c,'WRONG_ANCHOR' if c == 3 and v == 'ACAD_SECTION_MANAGER' else v) for c,v in bad[owner]]
            elif defect == 'class':
                declaration = next(r for r in bad_classes if (1,spelling) in r); declaration[declaration.index((90,1024))] = (90,0)
            elif row[start+2][1]: row[start+3] = (330,metadata(row)[0])
            else: row.append((330,metadata(row)[0]))
            try: validate(bad,bad_classes,spelling,mode,source)
            except (ValueError,KeyError,StopIteration): controls += 1
            else: raise ValueError('Mutated output escaped validator: '+name+'/'+defect)
    check(len(cases) == 52 and controls == 260,'Output/control totals changed')
    print(json.dumps({'outputs':52,'native_manager_boundary_comparisons':4,'actual_output_corruptions_rejected':controls,
        'audit_errors':0,'audit_repairs':0,'native_cad_execution':False,'automatic_membership':False,'section_evaluation':False},sort_keys=True))

if __name__ == '__main__': main()
