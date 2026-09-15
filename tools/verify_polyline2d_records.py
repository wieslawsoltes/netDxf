#!/usr/bin/env python3
"""Independent legacy 2D retained packet, width, geometry and identity qualification."""
from pathlib import Path
import argparse, copy, gzip, hashlib, json
import ezdxf
from ezdxf.lldxf.types import DXFTag
import verify_polygonmesh_records as common
import verify_polyface_records as polyface
ROOT=Path(__file__).resolve().parents[1]
check,equal,wire=common.check,common.equal,common.wire
FOLDER=ROOT/'tests/fixtures/polyline2d-records'

def fields(packet,subclass):
    result={};active=False;depth=0
    for i,tag in enumerate(packet):
        if tag.code==102:
            depth+=1 if tag.value.startswith('{') else -1;continue
        if depth:continue
        if tag.code==100:
            if active:break
            active=tag.value==subclass;continue
        if tag.code==1001:break
        if active:result[tag.code]=(i,tag.value)
    return result

def value(packet,subclass,code,default=None):return fields(packet,subclass).get(code,(-1,default))[1]
def patch(packet,subclass,changes):
    source=fields(packet,subclass);out=list(packet)
    for code,(i,_) in sorted(source.items(),key=lambda item:item[1][0],reverse=True):
        if code not in changes:continue
        if changes[code] is None:out.pop(i)
        else:out[i]=DXFTag(code,changes[code])
    active=False;at=len(out);depth=0
    for i,tag in enumerate(out):
        if tag.code==102:
            depth+=1 if tag.value.startswith('{') else -1;continue
        if depth:continue
        if tag==DXFTag(100,subclass):active=True;continue
        if active and tag.code in (100,1001):at=i;break
    additions=[DXFTag(code,val) for code,val in changes.items() if code not in source and val is not None]
    out[at:at]=additions;return out

def header_tail(packet):
    return packet[next(i for i,t in enumerate(packet) if t==DXFTag(100,'AcDb2dPolyline')):]

def source_inputs():
    producer={};manifest=json.loads((FOLDER/'manifest.json').read_text())
    check(manifest['producer']=='ezdxf 1.4.4' and len(manifest['fixtures'])==12,'producer inventory')
    for item in manifest['fixtures']:
        data=(FOLDER/item['file']).read_bytes();check(hashlib.sha256(data).hexdigest()==item['sha256'],'producer hash')
        common.native.audit(ezdxf.readfile(FOLDER/item['file']));producer[item['year'],item['binary']]=(item,wire(data))
    native=json.loads((FOLDER/'native-manifest.json').read_text());source=native['source'];original=gzip.decompress((FOLDER/source['gzip_file']).read_bytes())
    check(hashlib.sha256(original).hexdigest()==source['source_sha256'],'native source SHA256')
    check(hashlib.sha1(b'blob '+str(len(original)).encode()+b'\0'+original).hexdigest()==source['git_blob'],'native Git blob')
    data=(FOLDER/native['file']).read_bytes();check(hashlib.sha256(data).hexdigest()==native['sha256'],'native carrier SHA256')
    before,after=wire(original),wire(data)
    check(len(native['chains'])==2,'two genuine R2000 chains')
    for chain in native['chains']:
        for h in chain['vertices']+[chain['seqend']]:equal(before[h],after[h],'exact native extracted child')
        parent=[DXFTag(330,native['carrier_owner']) if t==DXFTag(330,chain['native_owner']) else t for t in before[chain['parent']]]
        equal(parent,after[chain['parent']],'only declared parent carrier owner changed')
    return producer,(native,after)

def native_check(after,item,before):
    check(list(common.children(after))==list(common.children(before)),'native identity/order inventory')
    for h,p in common.children(before).items():equal(p,after[h],'complete native child packet')
    for chain in item['chains']:
        h=chain['parent'];p=before[h];at=next(i for i,t in enumerate(p) if t==DXFTag(100,'AcDbEntity'))+1
        end=next(i for i,t in enumerate(p) if t==DXFTag(100,'AcDb2dPolyline'))
        expected=p[:at]+[DXFTag(67,0),DXFTag(8,'0'),DXFTag(62,0),DXFTag(6,'ByBlock'),DXFTag(370,-1),DXFTag(48,1.),DXFTag(60,0)]+p[end:]
        equal(expected,after[h],'native complete header allows only four common defaults and existing62-before6 order')
        check(value(after[h],'AcDb2dPolyline',40)==chain['default_start_width'],'native inherited width')

def producer_check(after,item,before):
    h=item['handles'];check(list(common.children(after))==list(common.children(before)),'producer child identity/order inventory')
    for key,p in common.children(before).items():equal(p,after[key],'complete producer child packet')
    for name in ('polyline','plain_polyline'):equal(header_tail(before[h[name]]),header_tail(after[h[name]]),'complete producer subclass packet')
    for name in ('vertex_xrecord','seqend_xrecord'):equal(before[h[name]],after[h[name]],'owned child metadata packet')
    check(polyface.chain(after,h['polyline'])==h['vertices']+[h['seqend']],'actual wire child order')
    for key in h['vertices']:check(common.native.extractor.owner(after[key])==h['block_record'],'actual containing block owner')
    check(common.native.extractor.owner(after[h['seqend']])==h['polyline'],'SEQEND parent identity')

def edited_check(after,item,before,mode):
    h=item['handles'];ids=h['vertices'];order=ids[::-1] if mode=='reversed' else ids
    check(polyface.chain(after,h['polyline'])==order+[h['seqend']],'edited physical point identity order')
    for n,key in enumerate(ids):
        original=before[key];changes={}
        if mode=='reversed':
            source=before[ids[(n-1)%4]]
            changes[40]=value(source,'AcDb2dVertex',41);changes[41]=value(source,'AcDb2dVertex',40)
            bulge=-value(source,'AcDb2dVertex',42,0.)
            changes[42]=bulge if 42 in fields(original,'AcDb2dVertex') or bulge!=0 else None
        elif mode=='transformed':
            x,y,z=value(original,'AcDb2dVertex',10);changes[10]=(2*x+5,2*y+7,0.)
            for code in (40,41):
                v=value(original,'AcDb2dVertex',code);changes[code]=None if v is None else v*2
        elif mode=='edited':
            if n==0:changes[40]=None
            if n==1:changes[41]=0.
            if n==2:changes[10]=(81.,82.,0.)
            if n==3:changes[42]=0.125;changes[91]=99
        equal(patch(original,'AcDb2dVertex',changes),after[key],'typed edits preserve unrelated child packet')
    equal(before[h['seqend']],after[h['seqend']],'SEQEND packet remains stable')
    changes={}
    if mode=='reversed':changes={40:1.25,41:0.75}
    if mode=='transformed':changes={10:(0.,0.,17.),39:1.,40:1.5,41:2.5}
    if mode=='edited':changes={70:0,10:(0.,0.,7.),39:2.,210:(0.,1.,0.)}
    equal(header_tail(patch(before[h['polyline']],'AcDb2dPolyline',changes)),header_tail(after[h['polyline']]),'typed header changes preserve remaining subclass packet')

def clone_check(after):
    parents=[p for p in after.values() if p[0].value=='POLYLINE' and DXFTag(100,'AcDb2dPolyline') in p];check(len(parents)==1,'clone legacy parent inventory')
    parent=parents[0];identity=common.identity(parent);ids=polyface.chain(after,identity);check(len(ids)==5,'clone four point records and terminator')
    for i,h in enumerate(ids[:-1]):
        check(value(after[h],'AcDb2dVertex',10)==[(101.,102.,0.),(104.,107.,0.),(108.,112.,0.),(113.,119.,0.)][i],'clone point geometry')
    check(value(parent,'AcDb2dPolyline',40)==2. and value(parent,'AcDb2dPolyline',41)==3.,'clone inherited defaults')

def main():
    parser=argparse.ArgumentParser();parser.add_argument('artifacts',type=Path);args=parser.parse_args()
    producer,native=source_inputs();count=0;controls=0
    def read(name,year,binary):
        nonlocal count
        path=args.artifacts/name;check(path.exists(),'missing output '+name);count+=1;return common.output(path,year,binary)[0]
    for binary in (False,True):native_check(read(f'legacy2d-records-native-{binary}.dxf',2000,binary),*native)
    for (year,input_binary),(item,before) in producer.items():
        version='AutoCad'+str(year)
        for binary in (False,True):
            after=read(f'legacy2d-records-producer-{version}-{input_binary}-{binary}.dxf',year,binary);producer_check(after,item,before)
            for mutation in ('identity','width','owner'):
                broken=copy.deepcopy(after);key=item['handles']['vertices'][0]
                if mutation=='identity':broken[key]=[DXFTag(5,'FFFFFF') if t.code==5 else t for t in broken[key]]
                elif mutation=='width':broken[key]=patch(broken[key],'AcDb2dVertex',{40:0.})
                else:broken[key]=[DXFTag(330,'FFFFFF') if t.code==330 else t for t in broken[key]]
                rejected=False
                try:producer_check(broken,item,before)
                except (ValueError,AssertionError):rejected=True
                check(rejected,'corruption control escaped '+mutation);controls+=1
        for mode in ('reversed','transformed','edited'):edited_check(read(f'legacy2d-records-{mode}-{version}-{input_binary}.dxf',year,input_binary),item,before,mode)
        clone_check(read(f'legacy2d-records-clone-{version}-{input_binary}.dxf',year,not input_binary))
    for binary in (False,True):
        item,before=producer[2018,binary];h=item['handles'];expected=copy.deepcopy(before)
        expected[h['vertices'][0]]=patch(expected[h['vertices'][0]],'AcDb2dVertex',{40:0.})
        expected[h['vertices'][2]]=patch(expected[h['vertices'][2]],'AcDb2dVertex',{41:0.})
        after=read(f'legacy2d-records-explicit-zero-{binary}.dxf',2018,binary)
        for key,p in common.children(expected).items():
            # Source raw zero was deliberately inserted before group41; compare qualified presence separately from unedited packet portions.
            if key in (h['vertices'][0],h['vertices'][2]):
                equal([t for t in p if t.code not in (40,41)],[t for t in after[key] if t.code not in (40,41)],'explicit-zero unrelated packet')
                check(fields(p,'AcDb2dVertex').get(40,(-1,None))[1]==fields(after[key],'AcDb2dVertex').get(40,(-1,None))[1],'explicit-zero start presence')
                check(fields(p,'AcDb2dVertex').get(41,(-1,None))[1]==fields(after[key],'AcDb2dVertex').get(41,(-1,None))[1],'explicit-zero end presence')
            else:equal(p,after[key],'explicit-zero unaffected packet')
        after=read(f'legacy2d-records-zero-reversed-scaled-{binary}.dxf',2018,binary)
        check(value(after[h['vertices'][3]],'AcDb2dVertex',40)==0.,'reversed scaled explicit zero start')
        check(value(after[h['vertices'][1]],'AcDb2dVertex',41)==0.,'reversed scaled explicit zero end')
        check(value(after[h['polyline']],'AcDb2dPolyline',40)==3.75,'reversed scaled default start')
        for number in (0,1):
            for transformed in (False,True):
                suffix='degenerate-transformed' if transformed else 'degenerate'
                after=read(f'legacy2d-records-{suffix}-{binary}-{number}.dxf',2018,binary)
                end=h['plain_seqend'];parent=common.native.extractor.owner(after[end]);ids=polyface.chain(after,parent)
                check(ids==h['plain_vertices'][:number]+[end],'degenerate actual physical child sequence')
                check(value(after[parent],'AcDb2dPolyline',40)==(4. if transformed else 2.),'degenerate inherited width')
                check(value(after[parent],'AcDb2dPolyline',10)==(0.,0.,11. if transformed else 0.),'degenerate parent plane')
                if number:
                    check(value(after[h['plain_vertices'][0]],'AcDb2dVertex',10)==((207.,211.,0.) if transformed else (101.,102.,0.)),'degenerate singleton geometry')
                for key in (parent,end):check(any(t==DXFTag(1000,'degenerate metadata') for t in after[key]),'degenerate parent/terminator metadata')
        erased=read(f'legacy2d-records-owned-erased-{binary}.dxf',2018,binary)
        for key in (h['vertices'][2],h['seqend']):check(not any(t==DXFTag(102,'{ACAD_XDICTIONARY') for t in erased[key]),'erased child extension packet survived')
        for variant in range(4):
            after=read(f'legacy2d-records-private-{binary}-{variant}.dxf',2018,binary)
            parent=after[h['plain_polyline']];check(value(parent,'AcDb2dPolyline',40)==2.,'private header width lookalike')
            check(value(after[h['plain_vertices'][0]],'AcDb2dVertex',10)==(101.,102.,0.),'private vertex position lookalike')
            packet=after[h['plain_vertices'][0] if variant<2 else h['plain_polyline']]
            check(any(t==DXFTag(5,'FFFFFF') for t in packet),'private physical packet missing')
    inventory=list(args.artifacts.glob('legacy2d-records-*.dxf'));check(len(inventory)==count,'unexpected legacy output inventory')
    print(f'PASS: {count} legacy 2D outputs, {controls} corruption controls, zero independent audit errors or repairs.')
if __name__=='__main__':main()
