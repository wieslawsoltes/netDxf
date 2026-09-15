#!/usr/bin/env python3
"""Verify explicit SECTION_MANAGER creation/erasure on raw packet records."""
import argparse
import copy
import gzip
import hashlib
import json
from pathlib import Path
import tempfile
from verify_fourth_mixed_modules import load, metadata
from verify_section_manager import check, manager, packet, YEARS


def dictionary_edges(row):
    tags = packet(row)
    check(tags.pop(0) == (100,'AcDbDictionary'), 'Dictionary subclass changed')
    if tags and tags[0][0] == 280:
        check(tags.pop(0)[1] in (0,1), 'Dictionary hard-owner flag changed')
    check(tags and tags[0][0] == 281 and tags.pop(0)[1] in range(6), 'Dictionary cloning flag changed')
    check(len(tags) % 2 == 0, 'Dictionary entry framing changed')
    result = []
    for index in range(0,len(tags),2):
        check(tags[index][0] == 3 and tags[index+1][0] in (350,360), 'Dictionary entry grammar changed')
        result.append((tags[index][1],*tags[index+1]))
    return result

def remove_anchor(row, handle):
    result = list(row)
    for index in range(len(result)-2, -1, -1):
        if result[index][0] == 3 and result[index+1] in ((350,handle),(360,handle)):
            del result[index:index+2]
    return result


def stable_records(records):
    # The legacy writer allocates this temporary empty dictionary on every Save.
    # Admit only its exact known empty packet and reciprocal LAYER-table ancestry.
    result = copy.deepcopy(records)
    parents = [(h,r) for h,r in result.items() if (3,'ACAD_LAYERSTATES') in r]
    check(len(parents) == 1, 'Generated layer-state carrier inventory changed')
    parent,row = parents[0]
    edges = dictionary_edges(row)
    check(len(edges) == 1 and edges[0][:2] == ('ACAD_LAYERSTATES',360), 'Generated layer-state entry changed')
    child = edges[0][2]; owner = metadata(row)[0]
    check(owner in result and result[owner][0] == (0,'TABLE') and (2,'LAYER') in result[owner] and [(c,v) for c,v in result[owner] if c == 360] == [(360,parent)] and [(102,'{ACAD_XDICTIONARY'),(360,parent),(102,'}')] == result[owner][3:6], 'Layer-state owner ancestry changed')
    check(result[child] == [(0,'DICTIONARY'),(5,child),(330,parent),(100,'AcDbDictionary'),(280,1),(281,1)], 'Temporary layer-state dictionary is no longer the exact empty writer packet')
    result[parent] = [(c,'@generated-layerstates' if (c,v) == (360,child) else v) for c,v in row]
    del result[child]
    return result

def validate(records, classes, mode, baseline=None, native=None):
    erased = mode in ('erased','native-erased')
    native_mode = mode.startswith('native-')
    roots = [(h,r) for h,r in records.items() if r[0] == (0,'DICTIONARY') and metadata(r)[0] in (None,'0')]
    check(len(roots) == 1, 'Named-object root inventory changed')
    root_handle, root = roots[0]
    entries = dictionary_edges(root)
    definitions = [r for r in classes if (1,'SECTION_MANAGER') in r]
    expected_class = [(0,'CLASS'),(1,'SECTION_MANAGER'),(2,'AcDbSectionManager'),(3,'ObjectDBX Classes'),(90,1024),(91,0 if erased else 1),(280,0),(281,0)]
    check(definitions == [expected_class], 'Canonical CLASS metadata/count changed')
    if erased:
        check(not any(r[0][1] in ('SECTION_MANAGER','SECTIONMANAGER') for r in records.values()), 'Erased manager was retained or resynthesized')
        check(not any(name.upper() == 'ACAD_SECTION_MANAGER' for name,_,_ in entries), 'Erased root anchor was retained')
    else:
        identity, row = manager(records)
        count = 0 if mode == 'empty' else 1 if native_mode else 3
        flag = 0 if mode in ('created','native-before') else 1
        owner, reactors, extension = metadata(row)
        check(owner == root_handle and reactors == [root_handle] and extension is None, 'Canonical common owner/reactor metadata changed')
        check(('ACAD_SECTION_MANAGER',350,identity) in entries, 'Canonical soft root anchor changed')
        body = packet(row)
        check(body[:3] == [(100,'AcDbSectionManager'),(70,flag),(90,count)] and len(body) == count+3 and all(c == 330 for c,v in body[3:]), 'Manager public list/flag grammar changed')
        prefix = [(0,'SECTION_MANAGER'),(5,identity),(102,'{ACAD_REACTORS'),(330,root_handle),(102,'}'),(330,root_handle)]
        check(row == prefix+body, 'Manager complete common envelope changed')
        targets = [v for c,v in body[3:]]
        if mode in ('created','recreated'):
            names = [next(v for c,v in packet(records[h]) if c == 1) for h in targets]
            check(names == (['Lifecycle first','Lifecycle second','Lifecycle first'] if mode == 'created' else ['Lifecycle second','Lifecycle first','Lifecycle second']), 'Ordered member identities/names changed')
            check(targets[0] == targets[2] and targets[0] != targets[1], 'Repeated actual member identity changed')
        if native_mode:
            check(identity == '229' and row == native['229'] and targets == ['228'], 'Full pinned native manager packet changed')
    sections = {h:r for h,r in records.items() if r[0] == (0,'SECTIONOBJECT')}
    check(len(sections) == (1 if native_mode else 0 if mode == 'empty' else 2), 'Surviving SECTION inventory changed')
    if not native_mode and mode != 'empty':
        check((280,0) in root and (281,2) in root, 'Existing root flags were rewritten')
        keep = [(strength,h) for name,strength,h in entries if name == 'LIFECYCLE_KEEP']
        check(len(keep) == 1 and keep[0][0] == 360 and packet(records[keep[0][1]]) == [(100,'AcDbXrecord'),(280,1),(1,'unrelated root entry')], 'Unrelated root entry/strength/payload changed')
    if baseline is not None:
        old_records, old_classes = baseline
        old_handle, old_manager = manager(old_records)
        old_root_handle = metadata(old_manager)[0]
        check(root_handle == old_root_handle, 'Existing root identity changed')
        old_sections = {h:r for h,r in old_records.items() if r[0] == (0,'SECTIONOBJECT')}
        check(sections == old_sections, 'Surviving SECTION identities/full records changed')
        if erased:
            expected_records = copy.deepcopy(old_records); del expected_records[old_handle]
            expected_records[root_handle] = remove_anchor(expected_records[root_handle],old_handle)
            check(stable_records(records) == stable_records(expected_records), 'Erasure changed records beyond manager/root anchor and the verified temporary layer-state identity')
            expected_classes = [[(c,0 if c == 91 else v) for c,v in r] if (1,'SECTION_MANAGER') in r else r for r in old_classes]
            check(classes == expected_classes, 'Erasure changed CLASS metadata beyond present manager count')
        elif mode == 'recreated':
            identity, row = manager(records)
            check(identity != old_handle and old_handle not in records, 'Recreation reused an erased manager identity')
            check(remove_anchor(root,identity) == remove_anchor(old_records[root_handle],old_handle), 'Recreation changed unrelated root packet')
            check({h:r for h,r in stable_records(records).items() if h not in (identity,root_handle)} == {h:r for h,r in stable_records(old_records).items() if h not in (old_handle,root_handle)}, 'Recreation changed surviving physical records')
    if native_mode:
        check(root_handle == 'C' and metadata(records['22A'])[0] == '228', 'Native section/settings root or ownership changed')


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('artifacts',type=Path); args = parser.parse_args()
    fixture = Path(__file__).resolve().parents[1]/'tests/fixtures/section'; manifest = json.loads((fixture/'manifest.json').read_text())
    packed = (fixture/'LiveSection1.dxf.gz').read_bytes(); original = gzip.decompress(packed)
    check(hashlib.sha256(packed).hexdigest() == manifest['gzip_sha256'] and hashlib.sha256(original).hexdigest() == manifest['source_sha256'], 'Pinned native source hashes changed')
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp)/'native.dxf'; path.write_bytes(original); _,native,_ = load(path,2018,False)
    cases = [(f'manager-lifecycle-{mode}-{version}-{binary}.dxf',year,binary,mode,f'manager-lifecycle-created-{version}-{binary}.dxf' if mode in ('erased','recreated') else None)
             for version,year in YEARS.items() for binary in (False,True) for mode in ('created','erased','recreated','empty')]
    cases += [(f'manager-lifecycle-native-{mode}-{input_binary}-{binary}.dxf',2018,binary,'native-'+mode,f'manager-lifecycle-native-before-{input_binary}-{binary}.dxf' if mode == 'erased' else None)
              for input_binary in (False,True) for binary in (False,True) for mode in ('before','erased')]
    check({p.name for p in args.artifacts.glob('manager-lifecycle-*.dxf')} == {case[0] for case in cases}, 'All 40 lifecycle outputs are mandatory')
    loaded = {name:load(args.artifacts/name,year,binary) for name,year,binary,_,_ in cases}
    controls = 0
    for name,year,binary,mode,baseline_name in cases:
        doc,records,classes = loaded[name]; baseline = loaded[baseline_name][1:] if baseline_name else None
        validate(records,classes,mode,baseline,native); audit = doc.audit(); check(not audit.errors and not audit.fixes,'Lifecycle output requires audit repair: '+name)
        defects = ('class','section','root-flags','manager-resynthesis') if 'erased' in mode else ('class','count','root','reactor','flag','section')
        if baseline is not None: defects += ('layerstate-payload','layerstate-owner','layerstate-entry')
        for defect in defects:
            bad = copy.deepcopy(records); bad_classes = copy.deepcopy(classes)
            root_handle = next(h for h,r in bad.items() if r[0] == (0,'DICTIONARY') and metadata(r)[0] in (None,'0'))
            if defect.startswith('layerstate-'):
                parent = next(r for r in bad.values() if (3,'ACAD_LAYERSTATES') in r); child = next(h for n,c,h in dictionary_edges(parent) if n == 'ACAD_LAYERSTATES')
                if defect == 'layerstate-payload': bad[child].append((1,'unexpected payload'))
                elif defect == 'layerstate-owner': bad[child] = [(c,root_handle if c == 330 else v) for c,v in bad[child]]
                else: parent.extend([(3,'UNEXPECTED_ENTRY'),(360,root_handle)])
            elif defect == 'class':
                declaration = next(r for r in bad_classes if (1,'SECTION_MANAGER') in r); idx = next(i for i,t in enumerate(declaration) if t[0] == 91); declaration[idx] = (91,37)
            elif defect == 'section':
                target = next((h for h,r in bad.items() if r[0] == (0,'SECTIONOBJECT')),None)
                if target is None: bad['ABCDEF'] = [(0,'SECTIONOBJECT'),(5,'ABCDEF')]
                else: del bad[target]
            elif defect == 'root-flags': bad[root_handle].append((281,5))
            elif defect == 'manager-resynthesis':
                old_handle,row = manager(baseline[0]); row = [(c,'ABCDEF' if c == 5 else v) for c,v in row]; bad['ABCDEF'] = row; bad[root_handle] += [(3,'ACAD_SECTION_MANAGER'),(350,'ABCDEF')]
            else:
                identity,row = manager(bad); start = row.index((100,'AcDbSectionManager'))
                if defect == 'count': row[start+2] = (90,row[start+2][1]+1)
                elif defect == 'flag': row[start+1] = (70,1-row[start+1][1])
                elif defect == 'root': bad[root_handle] = [(360,v) if (c,v) == (350,identity) else (c,v) for c,v in bad[root_handle]]
                elif defect == 'reactor': row[row.index((330,root_handle))] = (330,identity)
            try: validate(bad,bad_classes,mode,baseline,native)
            except (ValueError,KeyError,StopIteration): controls += 1
            else: raise ValueError('Actual output corruption escaped: '+name+'/'+defect)
    check(controls == 276,'Control inventory changed')
    print(json.dumps({'outputs':len(cases),'actual_output_corruptions_rejected':controls,'native_before_after_record_comparisons':4,'audit_errors':0,'audit_repairs':0,'native_cad_execution':False,'automatic_section_generation':False},sort_keys=True))

if __name__ == '__main__': main()
