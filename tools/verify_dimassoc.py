#!/usr/bin/env python3
"""Require exact stored DIMASSOC packets and native ownership in all 46 outputs."""
from pathlib import Path
import argparse, copy, gzip, hashlib, importlib.util, io, json, tempfile
import ezdxf
from ezdxf.lldxf.tagger import binary_tags_loader, tag_compiler
from ezdxf.lldxf.types import DXFTag

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('dimassoc_extractor', ROOT/'tests/fixtures/dimassoc/extract_fixtures.py')
extractor = importlib.util.module_from_spec(spec); spec.loader.exec_module(extractor)
VERSIONS = {2000:'AC1015',2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}

def check(value, message):
    if not value: raise ValueError(message)

def packets(data):
    if not data.startswith(b'AutoCAD Binary DXF'): return extractor.packets(data)
    result, packet = [], []
    for tag in tag_compiler(binary_tags_loader(data)):
        if tag.code == 0 and packet: result.append(packet); packet=[]
        packet.append(tag)
    if packet: result.append(packet)
    return result

def wire(data):
    result={}
    for packet in packets(data):
        handle=extractor.identity(packet)
        if handle:
            check(handle not in result, 'Duplicate physical identity')
            result[handle]=packet
    return result

def one(packet, code): return extractor.one(packet,code)
def body(packet): return packet[packet.index(DXFTag(100,'AcDbDimAssoc')):]
def common(packet): return packet[:packet.index(DXFTag(100,'AcDbDimAssoc'))]
def reactor(packet, target):
    active=False
    for tag in packet:
        if tag.code==102: active=tag.value=='{ACAD_REACTORS'
        elif active and tag==DXFTag(330,target): return True
    return False

def source_inputs():
    folder=ROOT/'tests/fixtures/dimassoc'; sources=json.loads((folder/'source-manifest.json').read_text()); manifest=json.loads((folder/'manifest.json').read_text())
    check(len(sources['sources'])==len(manifest['fixtures'])==6,'Expected six pinned native originals and extractions')
    result={}; count=0
    for fixture in manifest['fixtures']:
        source=next(s for s in sources['sources'] if s['year']==fixture['year']); packed=(folder/'originals-gzip'/source['gzip_file']).read_bytes(); data=gzip.decompress(packed)
        check(hashlib.sha256(packed).hexdigest()==source['gzip_sha256'],'Pinned gzip hash changed')
        check(hashlib.sha256(data).hexdigest()==source['source_sha256'],'Pinned native source hash changed')
        check(hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest()==source['git_blob_sha'],'Native Git blob hash changed')
        before=wire(data); path=folder/fixture['file']; extracted=path.read_bytes(); after=wire(extracted)
        fixture['_class']=next(p for p in packets(data) if p[0]==DXFTag(0,'CLASS') and one(p,1)=='DIMASSOC')
        check(hashlib.sha256(extracted).hexdigest()==fixture['sha256'],'Extraction hash changed'); mapping=fixture['handle_map']
        check(len(fixture['application_packets'])==32,'Expected complete 32-packet source component')
        for handle in fixture['application_packets']:
            expected=[]
            for tag in before[handle]:
                pointer=tag.code in (5,105,1005) or 320<=tag.code<=369 or 390<=tag.code<=399 or tag.code in (480,481)
                expected.append(DXFTag(tag.code,mapping.get(tag.value,tag.value)) if pointer else tag)
            check(extractor.exact(after[mapping[handle]])==extractor.exact(expected),'Native packet changed beyond explicit handle map');count+=1
        audit(ezdxf.readfile(path));result[fixture['year']]=(fixture,after)
    check(count==192,'Native exact mapped packet count changed');return result

def audit(doc):
    result=doc.audit();check(not result.errors and not result.fixes,f'Independent audit has {len(result.errors)} errors/{len(result.fixes)} repairs')

def output_graph(path,year,binary,sources,variant=None,metadata=False):
    data=path.read_bytes();check(data.startswith(b'AutoCAD Binary DXF')==binary,'Output transport differs')
    after=wire(data);fixture,before=sources[year];mapping=fixture['handle_map'];target=mapping['452']
    check(sum(p[0]==DXFTag(0,'DIMASSOC') for p in after.values())==8,'Association inventory changed')
    for source in fixture['associations']:
        handle=mapping[source];packet=after[handle];expected=before[handle]
        if variant is not None and source=='452':
            expected=opaque_mutation(expected,variant)
        if metadata and source=='452':
            geometry=next(t.value for t in body(expected) if t.code==331)
            expected=expected+[DXFTag(1001,'RENAMED_ASSOC'),DXFTag(1004,bytes([0,128,255])),DXFTag(1005,geometry)]
        check(extractor.exact(packet)==extractor.exact(expected),'Exact DIMASSOC common/subclass/XData packet changed')
        owner=extractor.owner(packet);check(extractor.exact(after[owner])==extractor.exact(before[owner]),'Native owner dictionary changed')
        dim=one(body(before[handle]),330)
        check(after[dim][0].value in ('DIMENSION','ARC_DIMENSION'),'Subclass dimension target is not a DIMENSION identity')
        check(DXFTag(360,owner) in after[dim] and reactor(after[dim],handle),'Dimension extension attachment or reactor backlink changed')
        check(reactor(packet,owner),'Native association owner-reactor link changed')
        if source!='42F' and not (variant is not None and source=='452'):
            for tag in body(packet):
                if tag.code==331:
                    check(tag.value in after and after[tag.value][0]==before[tag.value][0],'Qualified source geometry identity/type changed')
                    check(reactor(after[tag.value],handle)==reactor(before[tag.value],handle),'Native source geometry backlink changed')
    doc=ezdxf.readfile(path);check(doc.dxfversion==VERSIONS[year],'Output profile differs')
    definition=doc.classes.get('DIMASSOC');check(definition.dxf.cpp_class_name=='AcDbDimAssoc' and definition.dxf.is_an_entity==0,'DIMASSOC CLASS identity changed')
    actual_class=next(p for p in packets(data) if p[0]==DXFTag(0,'CLASS') and one(p,1)=='DIMASSOC')
    check(extractor.exact([t for t in actual_class if t.code!=91])==extractor.exact([t for t in fixture['_class'] if t.code!=91]),'Native CLASS metadata changed')
    if year>=2004:check(definition.dxf.instance_count==8,'DIMASSOC CLASS count differs')
    audit(doc)

def opaque_mutation(packet,variant):
    tags=list(packet);marker=tags.index(DXFTag(100,'AcDbDimAssoc'))
    def at(code): return next(i for i in range(marker+1,len(tags)) if tags[i].code==code)
    if variant==0: tags.insert(at(331),DXFTag(331,'EEEEFFFF'))
    elif variant==1: tags.insert(at(75),DXFTag(332,'EEEEFFFF'))
    elif variant==2: tags.insert(at(40),DXFTag(301,r'inert\unopened.dwg'))
    elif variant==3: tags.insert(at(40),DXFTag(302,r'inert\unopened2.dwg'))
    elif variant==4: tags[at(75)]=DXFTag(75,1)
    elif variant==5: tags[at(1)]=DXFTag(1,'PrivatePointRef');tags[at(72)]=DXFTag(72,32767)
    elif variant==6: tags[at(72)]=DXFTag(72,6)
    elif variant==7: tags.extend([DXFTag(100,'PrivateDimAssoc'),DXFTag(94,17),DXFTag(75,17)])
    elif variant==8: tags[marker:marker]=[DXFTag(102,'{PRIVATE'),DXFTag(1,'private'),DXFTag(102,'}')]
    else: tags[marker]=DXFTag(100,'PrivateAssocBase')
    tags[at(330)]=DXFTag(330,'FFFFFFFE')
    # Recognized common metadata is written before private retained header tags.
    if variant==8:
        private=tags[marker:marker+3];del tags[marker:marker+3]
        tags[marker:marker]=private
    return tags

def corruption_controls(folder,sources):
    path=folder/'dimassoc-native-AutoCad2018-False-False.dxf';fixture,_=sources[2018];mapping=fixture['handle_map'];target=mapping['452'];original=packets(path.read_bytes())
    with tempfile.TemporaryDirectory() as temporary:
        for fault in range(4):
            content=copy.deepcopy(original);packet=next(p for p in content if extractor.identity(p)==target)
            if fault==0:
                i=next(i for i,t in enumerate(packet) if t.code==40);packet[i]=DXFTag(40,99.125)
            elif fault==1:
                i=packet.index(DXFTag(100,'AcDbDimAssoc'))-1;check(packet[i].code==330,'Expected common owner');packet[i]=DXFTag(330,'0')
            elif fault==2:
                dim=one(body(packet),330);entity=next(p for p in content if extractor.identity(p)==dim);entity.remove(DXFTag(330,target))
            else:
                i=next(i for i,t in enumerate(packet) if t.code==331);packet[i]=DXFTag(331,'FFFFFFFF')
            corrupted=Path(temporary)/f'corrupt-{fault}.dxf';corrupted.write_bytes(extractor.write(content,VERSIONS[2018]));rejected=False
            try:output_graph(corrupted,2018,False,sources)
            except (ValueError,AssertionError):rejected=True
            check(rejected,'Actual-output corruption escaped independent gate')
        content=packets((folder/'dimassoc-opaque-7-False.dxf').read_bytes());packet=next(p for p in content if extractor.identity(p)==target)
        packet.remove(DXFTag(75,17));corrupted=Path(temporary)/'corrupt-private.dxf';corrupted.write_bytes(extractor.write(content,VERSIONS[2018]));rejected=False
        try:output_graph(corrupted,2018,False,sources,variant=7)
        except (ValueError,AssertionError):rejected=True
        check(rejected,'Private payload corruption escaped independent gate')

def main():
    parser=argparse.ArgumentParser();parser.add_argument('artifacts',type=Path);args=parser.parse_args()
    check(ezdxf.__version__=='1.4.4','Pinned oracle version differs');sources=source_inputs();expected=set()
    for year in VERSIONS:
        for source_binary in (False,True):
            for binary in (False,True):
                name=f'dimassoc-native-AutoCad{year}-{source_binary}-{binary}.dxf';expected.add(name);output_graph(args.artifacts/name,year,binary,sources)
    for binary in (False,True):
        for variant in range(10):
            name=f'dimassoc-opaque-{variant}-{binary}.dxf';expected.add(name);output_graph(args.artifacts/name,2018,binary,sources,variant)
        name=f'dimassoc-metadata-{binary}.dxf';expected.add(name);output_graph(args.artifacts/name,2018,binary,sources,metadata=True)
    check({p.name for p in args.artifacts.glob('dimassoc-*.dxf')}==expected,'DIMASSOC output inventory differs from required 46')
    corruption_controls(args.artifacts,sources)
    print('DIMASSOC: 46 outputs, 6 pinned native originals/extractions, 192 exact mapped source packets, exact ownership/backlinks/CLASS/opaque variants/XData, zero audits, 5 actual-output corruption controls passed.')

if __name__=='__main__':main()
