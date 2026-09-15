#!/usr/bin/env python3
"""Verify 20 emitted FIELD drawings against pinned native packets and authored graphs.

Twelve synthetic graph maps and all native/edit phases are mandatory. Native
packets are extracted from complete hash-pinned sources; carrier LINE hosts
replace source ATTDEF objects explicitly. No evaluator or full-source claim.
"""
import argparse
import copy
import gzip
import hashlib
import io
import json
from pathlib import Path
import struct
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from verify_fourth_mixed_modules import PROFILES, check, load, metadata, dictionary_edges, xdata

ROOT = Path(__file__).resolve().parent.parent
FIXTURES = ROOT / 'tests/fixtures/field-oracle'
HANDLES = ['14C','14D','14E','14F','15C','15D','15E','15F','150']


def exact(value):
    if isinstance(value, float): return struct.pack('>d', value)
    if isinstance(value, (tuple, list)): return tuple(exact(v) for v in value)
    return value


def payload(record):
    start = next(i for i,t in enumerate(record) if t[0] == 100)
    end = next((i for i in range(start,len(record)) if record[i][0] == 1001),len(record))
    return [(code,exact(value)) for code,value in record[start:end]]


def source_records(entry):
    raw = gzip.decompress((FIXTURES / entry['file']).read_bytes())
    check(hashlib.sha256(raw).hexdigest() == entry['sha256'], 'Pinned FIELD source SHA256 mismatch')
    check(hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest() == entry['gitBlobSha'], 'Pinned Git blob mismatch')
    tags = tag_compiler(ascii_tags_loader(io.StringIO(raw.decode('utf8' if entry['profile'] == 'AC1032' else 'cp1252'),newline=None)))
    records = {}; current = []
    def finish():
        handle = next((v for c,v in current if c == 5),None)
        if handle in HANDLES: records[handle] = list(current)
    for tag in tags:
        if tag.code == 0: finish(); current = []
        current.append((tag.code,tag.value))
    finish()
    check(set(records) == set(HANDLES), 'Incomplete native FIELD source graph')
    return records


def native(records, source, phase):
    fields = {h:r for h,r in records.items() if r[0] == (0,'FIELD')}
    check(set(fields) == {'14E','14F','15E','15F'}, 'Native FIELD identity inventory differs')
    for handle in HANDLES:
        check(payload(records[handle]) == payload(source[handle]), 'Native packet differs: '+handle)
    for prefix in ('14','15'):
        host, outer, owner, parent, child = [prefix+suffix for suffix in ('B','C','D','E','F')]
        check(records[host][0] == (0,'LINE'), 'Explicit synthetic host type differs')
        check(metadata(records[host])[2] == outer, 'Carrier host lost native extension')
        check(metadata(records[outer])[0] == host, 'Outer native owner differs')
        check(metadata(records[owner])[:2] == (outer,[outer]), 'Native owner dictionary metadata differs')
        check(dictionary_edges(records[outer]) == [('ACAD_FIELD',360,owner)], 'Native ACAD_FIELD dictionary edge differs')
        check(dictionary_edges(records[owner]) == [('TEXT',360,parent)], 'Native TEXT owner edge differs')
        check(metadata(records[parent])[:2] == (owner,[owner]), 'Native FIELD owner/reactor differs')
        check(metadata(records[child])[0] == parent, 'Native child common owner differs')
        if phase == 'native-edited':
            check(xdata(records[parent]) == [(1001,'FIELD_COMMON_EDIT'),(1000,'metadata only')], 'Edited parent common metadata differs')
            check(xdata(records[child]) == [(1001,'FIELD_COMMON_EDIT'),(1000,'metadata only')], 'Edited child common metadata differs')
        else: check(not xdata(records[parent]) and not xdata(records[child]), 'Unedited native FIELD gained XData')
    root, reactors, extension = metadata(records['150'])
    check(reactors == [root] and extension is None, 'Native FIELDLIST metadata mapping differs')
    check(('ACAD_FIELDLIST',350,'150') in dictionary_edges(records[root]), 'FIELDLIST root dictionary reference differs')


def graph(records, mapping):
    check(set(mapping) == {'parent','child','graph','external','layer','style','app','line'}, 'Synthetic map inventory differs')
    check(len(set(mapping.values())) == 8, 'Synthetic map collapsed identities')
    kinds = {'parent':'FIELD','child':'FIELD','graph':'DICTIONARY','external':'XRECORD','layer':'LAYER','style':'STYLE','app':'APPID','line':'LINE'}
    for key, kind in kinds.items(): check(records[mapping[key]][0] == (0,kind), 'Synthetic mapped target type differs: '+key)
    expected = [(100,'AcDbField'),(1,'Synthetic'),(2,r'prefix \U+03'),(3,'A9 suffix'),(90,1),(360,mapping['child']),(97,7)]
    expected += [(331,mapping[k]) for k in ('layer','style','app','line','external','external')] + [(331,'0')]
    expected += [(91,63),(92,0),(94,43),(95,32),(96,335),(300,'stored error'),(93,0),(7,'ACFD_FIELD_VALUE'),(90,0),(91,0),(301,'####'),(98,4),(310,bytes([0,1,255]))]
    check(payload(records[mapping['parent']]) == expected, 'Synthetic FIELD payload differs')
    expected_child = [(100,'AcDbField'),(1,'Child'),(2,'1+1'),(90,0),(97,0),(93,0),(7,'ACFD_FIELD_VALUE'),(90,0)]
    check(payload(records[mapping['child']]) == expected_child, 'Synthetic child payload differs')
    check(metadata(records[mapping['parent']])[0] == mapping['graph'], 'Synthetic parent owner differs')
    check(metadata(records[mapping['child']])[0] == mapping['parent'], 'Synthetic child owner differs')
    check(dictionary_edges(records[mapping['graph']]) == [('FIELD',360,mapping['parent'])], 'Synthetic owner names differ')
    for key, name in [('layer','FIELD_LAYER_RENAMED'),('style','FIELD_STYLE_RENAMED'),('app','FIELD_REGISTRY_RENAMED')]:
        check((2,name) in records[mapping[key]], 'Renamed resource identity differs: '+key)


def class_check(classes, count, year):
    found = [r for r in classes if (1,'FIELD') in r]
    check(len(found) == 1 and (2,'AcDbField') in found[0] and (3,'ObjectDBX Classes') in found[0] and (90,1152) in found[0] and (281,0) in found[0], 'FIELD CLASS metadata differs')
    if year >= 2004: check((91,count) in found[0], 'FIELD CLASS instance count differs')


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory',type=Path); args = parser.parse_args()
    manifest = json.loads((FIXTURES/'manifest.json').read_text())
    source = {2000 if f['profile']=='AC1015' else 2018:source_records(f) for f in manifest['files']}
    expected = {f'stored-field-AutoCad{year}-{binary}-graph.dxf' for year in PROFILES for binary in (False,True)}
    expected |= {f'stored-field-AutoCad{year}-{binary}-{phase}.dxf' for year in (2000,2018) for binary in (False,True) for phase in ('native','native-edited')}
    check({p.name for p in args.directory.glob('stored-field-*.dxf')} == expected, 'All 20 FIELD drawings are mandatory')
    maps = {f'stored-field-AutoCad{year}-{binary}-map.json' for year in PROFILES for binary in (False,True)}
    check({p.name for p in args.directory.glob('stored-field-*-map.json')} == maps, 'All 12 FIELD maps are mandatory')
    controls = 0
    for name in sorted(expected):
        suffix = name.removeprefix('stored-field-AutoCad').removesuffix('.dxf'); year, binary, phase = suffix.split('-',2); year=int(year); binary=binary=='True'
        doc,records,classes = load(args.directory/name,year,binary)
        if phase == 'graph':
            mapping = json.loads((args.directory/f'stored-field-AutoCad{year}-{binary}-map.json').read_text()); verify=lambda rows:graph(rows,mapping); parent=mapping['parent']; child=mapping['child']
        else: verify=lambda rows:native(rows,source[year],phase); parent='14E'; child='14F'
        verify(records); class_check(classes,2 if phase=='graph' else 4,year)
        for code in (2,90,360,301):
            corrupted=copy.deepcopy(records); index=next(i for i,t in enumerate(corrupted[parent]) if t[0]==code)
            old=corrupted[parent][index][1]; corrupted[parent][index]=(code,old+1 if isinstance(old,int) else old+'BAD')
            try: verify(corrupted)
            except (ValueError,KeyError): controls+=1
            else: raise ValueError('FIELD corruption control passed: '+str(code))
        corrupted=copy.deepcopy(records); del corrupted[child]
        try: verify(corrupted)
        except (ValueError,KeyError): controls+=1
        else: raise ValueError('Missing child control passed')
        print('PASS '+name)
    print(f'PASS 20 FIELD drawings, 12 maps, {controls} independent packet/ownership corruption controls')

if __name__ == '__main__': main()
