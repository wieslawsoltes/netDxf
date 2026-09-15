#!/usr/bin/env python3
"""Require stored SECTION packet, ownership, clone and native-source fidelity checks."""
from pathlib import Path
import argparse, copy, gzip, hashlib, io, json, tempfile
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler
from ezdxf.lldxf.types import DXFTag
from verify_mleader_inputs import records, exact, decode_once, json_value, check

VERSIONS = {2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}
NAMES = ('SECTION','SECTIONOBJECT')
SETTINGS = ('SECTIONSETTINGS','SECTION_SETTINGS')
TEXT = r'東京 Literal\U+0041'

def normal(tags): return [[c,decode_once(v) if isinstance(v,str) else v] for c,v in tags]
def body(tags,marker): return tags[tags.index([100,marker])+1:]
def one(tags,code):
    values=[v for c,v in tags if c==code];check(len(values)==1,f'Expected one group {code}, got {len(values)}');return values[0]
def common(tags): return tags[:next(i for i,t in enumerate(tags) if t[0]==100)]
def owner(tags):
    inside=False;values=[]
    for c,v in common(tags):
        if c==102: inside=v!='}'
        elif c==330 and not inside: values.append(v)
    check(len(values)==1,'Common owner count');return values[0]
def control(tags,name):
    start=tags.index([102,name]);end=tags.index([102,'}'],start+1);return tags[start+1:end]
def extension(tags): return one(control(tags,'{ACAD_XDICTIONARY'),360)
def dictionary(wire,handle):
    tags=normal(body(wire[handle],'AcDbDictionary')); result={};pending=None
    for c,v in tags:
        if c==3: check(pending is None and v not in result,'Dictionary name collision');pending=v
        elif c in (350,360): check(pending is not None,'Dictionary reference lacks name');result[pending]=v;pending=None
    check(pending is None,'Unfinished dictionary entry');return result

def raw_tags(path):
    data=path.read_bytes()
    return list(binary_tags_loader(data)) if data.startswith(b'AutoCAD Binary DXF') else list(ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'),newline=None)))
def class_packets(path):
    found={};packet=[]
    for tag in tag_compiler(iter(raw_tags(path))):
        if tag.code==0:
            if packet and packet[0]==[0,'CLASS']: found[one(packet,1)]=packet
            packet=[]
        packet.append([tag.code,json_value(tag.value)])
    return found

def structural_audit(path,year,binary):
    # Raw packets are checked by the caller before this adapter. ezdxf's file
    # splitter mistakes documented entity SECTION for a structural SECTION.
    # Only that entity type and its corresponding CLASS name are adapted here.
    data=path.read_bytes();check(data.startswith(b'AutoCAD Binary DXF')==binary,'Wrong transport')
    tags=raw_tags(path);kind=None;changes=0
    for i,tag in enumerate(tags):
        if tag.code==0:
            kind=tag.value
            if tag.value=='SECTION' and i+1<len(tags) and tags[i+1].code!=2:
                tags[i]=DXFTag(0,'SECTIONOBJECT');changes+=1
        elif kind=='CLASS' and tag.code==1 and tag.value=='SECTION': tags[i]=DXFTag(1,'SECTIONOBJECT')
    doc=ezdxf.document.Drawing.load(iter(tags)) if changes else ezdxf.readfile(path)
    check(doc.dxfversion==VERSIONS[year],'Wrong DXF profile');audit=doc.audit()
    check(not audit.errors and not audit.fixes,f'Independent audit found {len(audit.errors)} errors and {len(audit.fixes)} repairs')
    return bool(changes)
def classes(path,wire):
    definitions=class_packets(path)
    for name,cpp,entity in [(n,'AcDbSection',1) for n in NAMES]+[(n,'AcDbSectionSettings',0) for n in SETTINGS]:
        count=sum(t[0]==[0,name] and (not entity or [100,'AcDbSection'] in t) for t in wire.values())
        if count or name in definitions:
            check(name in definitions,'Required CLASS missing');packet=definitions[name]
            check(one(packet,2)==cpp and one(packet,281)==entity,'CLASS identity mismatch');check(one(packet,91)==count,'CLASS instance count mismatch')

def expected_body(settings='0',xdata=False):
    result=[[90,4],[91,17],[1,TEXT],[10,[1.,2.,3.]],[40,5.25],[41,-15.5],[70,70],[62,9],[63,256],[411,'Book$Color'],[92,2],[11,[1.,2.,3.]],[11,[-4.,5.,6.]],[93,1],[12,[9.,8.,7.]],[360,settings]]
    if xdata: result += [[1001,'SECTION_APP'],[1005,settings]]
    return result

def basic(path,year,binary,name,wire_override=None):
    wire=records(path) if wire_override is None else wire_override
    sections=[t for t in wire.values() if [100,'AcDbSection'] in t];check(len(sections)==1,'Basic section count')
    packet=sections[0];check(packet[0]==[0,name],'Stored entity spelling changed')
    check(exact(normal(body(packet,'AcDbSection')))==exact(expected_body()),'Basic stored section packet mismatch')
    classes(path,wire);return structural_audit(path,year,binary)

def graph(path,year,binary,count,cross=False,wire_override=None):
    wire=records(path) if wire_override is None else wire_override
    sections={h:t for h,t in wire.items() if [100,'AcDbSection'] in t};check(len(sections)==count,'Owned section count')
    lines={h:t for h,t in wire.items() if t[0]==[0,'LINE']};check(len(lines)==1,'External line count');line=next(iter(lines))
    check(one(lines[line],10)==[0.,0.,0.] and one(lines[line],11)==([0.,1.,0.] if cross else [1.,0.,0.]),'External LINE geometry changed')
    identities={}
    for h,t in sections.items():
        check(t[0]==[0,'SECTION'],'Documented spelling changed');packet=normal(body(t,'AcDbSection'));settings=one(packet,360)
        check(exact(packet)==exact(expected_body(settings,True)),'Owned section stored packet mismatch')
        st=wire[settings];check(st[0]==[0,'SECTIONSETTINGS'] and owner(st)==h,'Settings type or reciprocal owner mismatch')
        p=normal(body(st,'AcDbSectionSettings'));check(p[:4]==[[90,4],[91,1],[1,'SectionTypeSettings'],[90,4]],'Settings outer grammar')
        check(p[4:6]==[[91,17],[92,5]] and p[6:11]==[[330,h],[330,line],[330,line],[330,'0'],[330,settings]],'Settings exact source sequence/flags/count')
        check(p[11:15]==[[331,owner(t)],[1,'never-open-this.dwg'],[93,1],[2,'SectionGeometrySettings']],'Settings destination/count/marker')
        geometry=p[15:p.index([3,'SectionGeometrySettingsEnd'])];check(geometry[:3]==[[90,4],[91,8],[92,0]],'Independent geometry integers changed')
        check(one(geometry,62)==9 and one(geometry,8)=='*_BackgroundLines','Stored geometry color/name changed')
        check(p[-4:]==[[3,'SectionGeometrySettingsEnd'],[3,'SectionTypeSettingsEnd'],[1001,'SECTION_APP'],[1005,h]],'Settings end markers or XData mapping')
        check(control(st,'{ACAD_REACTORS')==[[330,h]],'Settings persistent owner reactor')
        root=control(t,'{ACAD_REACTORS')[0][1];check(wire[root][0]==[0,'DICTIONARY'] and control(t,'{ACAD_REACTORS')==[[330,root],[330,line]],'Entity persistent reactors')
        ext=extension(t);check(owner(wire[ext])==h,'Entity extension owner');entries=dictionary(wire,ext);check(set(entries)=={'NOTE','ALIAS'} and entries['NOTE']==entries['ALIAS'],'Extension aliases')
        note=entries['NOTE'];check(owner(wire[note])==ext,'Note owner');np=normal(body(wire[note],'AcDbXrecord'));np=[t for t in np if t[0]!=280]
        check(np==[[1,TEXT],[330,settings],[310,{'hex':'0300ff'}]],'Exact note binary/text/reference packet')
        se=extension(st);check(owner(wire[se])==settings,'Settings extension owner');empty=dictionary(wire,se);check(set(empty)=={'EMPTY'},'Settings extension inventory');empty_id=empty['EMPTY'];check(owner(wire[empty_id])==se,'Empty record owner')
        identities[h]={h,settings,ext,note,se,empty_id}
    check(sum(t[0]==[0,'SECTIONSETTINGS'] for t in wire.values())==count,'Extra or missing owned settings')
    classes(path,wire);adapted=structural_audit(path,year,binary);return identities,wire,adapted

def native_source(root):
    folder=root/'tests/fixtures/section';manifest=json.loads((folder/'manifest.json').read_text());packed=(folder/'LiveSection1.dxf.gz').read_bytes()
    check(hashlib.sha256(packed).hexdigest()==manifest['gzip_sha256'],'Native gzip hash changed');data=gzip.decompress(packed)
    check(hashlib.sha256(data).hexdigest()==manifest['source_sha256'] and len(data)==manifest['source_bytes'],'Native original source hash/size changed')
    check(manifest['transformations']==[],'Native source must remain unchanged')
    with tempfile.TemporaryDirectory() as temporary:
        path=Path(temporary)/'source.dxf';path.write_bytes(data);source=records(path);audit=ezdxf.readfile(path).audit();check(not audit.errors and not audit.fixes,'Original native source audit')
    check(source['228'][0]==[0,'SECTIONOBJECT'] and source['22A'][0]==[0,'SECTION_SETTINGS'],'Pinned native type inventory');return source

def native(path,binary,source,count=1,wire_override=None):
    wire=records(path) if wire_override is None else wire_override
    sections={h:t for h,t in wire.items() if [100,'AcDbSection'] in t};check(len(sections)==count,'Native section count')
    check('228' in sections,'Original native identity lost');check(exact(normal(body(wire['228'],'AcDbSection')))==exact(normal(body(source['228'],'AcDbSection'))),'Original native section body changed')
    check(exact(normal(body(wire['22A'],'AcDbSectionSettings')))==exact(normal(body(source['22A'],'AcDbSectionSettings'))),'Original native settings body changed')
    for h,t in sections.items():
        check(t[0]==[0,'SECTIONOBJECT'],'Native spelling changed');settings=one(body(t,'AcDbSection'),360);check(wire[settings][0]==[0,'SECTION_SETTINGS'] and owner(wire[settings])==h,'Native reciprocal settings ownership')
        expected=[[c,settings if c==360 else v] for c,v in body(source['228'],'AcDbSection')]
        check(exact(normal(body(t,'AcDbSection')))==exact(normal(expected)),'Native cloned section body changed')
        check(exact(normal(body(wire[settings],'AcDbSectionSettings')))==exact(normal(body(source['22A'],'AcDbSectionSettings'))),'Native cloned settings body changed')
        proxy=lambda tags:[v for c,v in tags[:tags.index([100,'AcDbSection'])] if c in (160,310)]
        check(proxy(t)==proxy(source['228']),'Native proxy bytes or byte-count changed')
    classes(path,wire);check(not structural_audit(path,2018,binary),'Native audit unexpectedly required an adapter')
    return {h for h in sections}|{one(body(t,'AcDbSection'),360) for t in sections.values()},wire

def expected_producer_body():
    return [[90,7],[91,17],[1,'Producer section'],[10,[0.25,-0.5,2.0]],[40,17.125],[41,-2.75],[70,37],[63,5],[411,'ProducerColor'],[92,3],
            [11,[1.125,2.25,3.5]],[11,[-4.75,5.125,-6.25]],[11,[7.5,-8.75,9.125]],[93,2],[12,[10.25,-11.5,12.75]],[12,[-13.125,14.25,-15.5]]]

def producer_sources(root):
    folder=root/'tests/fixtures/section-producer'
    original_manifest=json.loads((folder/'source-manifest.json').read_text());manifest=json.loads((folder/'manifest.json').read_text())
    pins={'producer':'IxMilia.Dxf 0.8.4','schema_source_commit':'3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567','package_sha256':'3b08f5b604c958ea6c29f107f751d75abdf3328713ec586bc4fdbf0302e24c81'}
    for field,value in pins.items():
        check(original_manifest[field]==value and manifest[field]==value,'Producer pin mismatch: '+field)
    check(manifest['extractor']=='ezdxf 1.4.4' and manifest['original_transformations']==[] and not manifest['original_whole_file_import_qualified'],'Producer evidence scope changed')
    names={f'ixmilia-section-R{year}-{transport}.dxf' for year in VERSIONS for transport in ('ascii','binary')}
    check(len(original_manifest['files'])==8 and {f['file'] for f in original_manifest['files']}==names,'Expected eight original producer entries')
    check(len(manifest['fixtures'])==8 and {f['file'] for f in manifest['fixtures']}==names,'Expected eight extracted producer entries')
    check({p.name for p in (folder/'originals-gzip').glob('*.gz')}=={n+'.gz' for n in names},'Original producer gzip inventory differs')
    check({p.name for p in (folder/'extracted').glob('*.dxf')}=={n.replace('ixmilia-','extracted-') for n in names},'Extracted producer inventory differs')
    originals={f['file']:f for f in original_manifest['files']}; result={};adapted_sources=0
    with tempfile.TemporaryDirectory() as temporary:
        for fixture in manifest['fixtures']:
            name=fixture['file'];year=int(name.split('-R')[1][:4]);binary='-binary.' in name;original=originals[name]
            check(fixture['profile']==VERSIONS[year] and fixture['binary']==binary,'Producer profile metadata changed')
            check(fixture['gzip_file']==name+'.gz' and fixture['extracted_file']==name.replace('ixmilia-','extracted-'),'Producer manifest path changed')
            packed=(folder/'originals-gzip'/fixture['gzip_file']).read_bytes();check(hashlib.sha256(packed).hexdigest()==fixture['gzip_sha256'],'Original producer gzip hash mismatch')
            data=gzip.decompress(packed);check(hashlib.sha256(data).hexdigest()==fixture['source_sha256']==original['sha256'] and len(data)==fixture['source_bytes']==original['bytes'],'Original producer hash/size mismatch')
            check(data.startswith(b'AutoCAD Binary DXF')==binary,'Original producer transport mismatch')
            path=Path(temporary)/name;path.write_bytes(data);tags=raw_tags(path)
            index=next(i for i,t in enumerate(tags) if t==DXFTag(9,'$ACADVER'));check(tags[index+1].value==VERSIONS[year],'Original producer version mismatch')
            source=records(path);entities={h:t for h,t in source.items() if [100,'AcDbSection'] in t};check(set(entities)=={'1D'},'Original entity identity/inventory differs')
            packet=entities['1D'];check(packet[0]==[0,'SECTION'] and not any(c==330 for c,v in common(packet)),'Original entity spelling/owner changed')
            check(exact(packet)==exact(original['entity_tags']),'Original manifest complete entity packet mismatch')
            check(exact(body(packet,'AcDbSection'))==exact(expected_producer_body()),'Independent producer stored values changed')
            check(fixture['handle_map']=={'1D':'F1000'} and fixture['inserted_common_owner']['code']==330,'Extraction map contract changed')
            model_owner=fixture['inserted_common_owner']['value'];expected=[[5,'F1000'] if c==5 else [c,v] for c,v in packet]
            expected.insert(expected.index([100,'AcDbEntity']),[330,model_owner])
            extracted=folder/'extracted'/fixture['extracted_file'];check(hashlib.sha256(extracted.read_bytes()).hexdigest()==fixture['extracted_sha256'],'Extracted producer hash mismatch')
            wire=records(extracted);entities={h:t for h,t in wire.items() if [100,'AcDbSection'] in t};check(set(entities)=={'F1000'},'Extracted entity identity/inventory differs')
            check(exact(wire['F1000'])==exact(expected),'Extraction changed a field outside identity/inserted owner map')
            check(model_owner in wire and wire[model_owner][0]==[0,'BLOCK_RECORD'] and one(wire[model_owner],2)=='*Model_Space','Extracted owner is not actual model space')
            check(fixture['exact_ordered_body'] and fixture['all_other_common_fields_unchanged'],'Missing extraction equality receipts')
            check(fixture['audit_type_name_changes']=={'entity_type':1,'class_name':1} and fixture['audit_errors']==0 and fixture['audit_repairs']==0,'Extraction audit receipt changed')
            classes(extracted,wire);check(structural_audit(extracted,year,binary),'Documented producer source should require audit adapter');adapted_sources+=1
            result[(year,binary)]=expected
    check(adapted_sources==8,'Expected eight explicitly adapted extracted-source audits')
    return result,adapted_sources

def producer(path,year,binary,source,wire_override=None):
    wire=records(path) if wire_override is None else wire_override
    sections={h:t for h,t in wire.items() if [100,'AcDbSection'] in t};check(set(sections)=={'F1000'},'Saved producer identity/inventory differs')
    packet=sections['F1000'];check(packet[0]==[0,'SECTION'],'Saved producer spelling changed')
    stored=body(packet,'AcDbSection');check(exact(stored)==exact(body(source,'AcDbSection'))==exact(expected_producer_body()),'Saved producer ordered section body changed')
    check(not any(c==360 for c,v in stored),'Saved producer materialized absent settings pointer')
    check(owner(packet)==owner(source) and wire[owner(packet)][0]==[0,'BLOCK_RECORD'] and one(wire[owner(packet)],2)=='*Model_Space','Saved producer owner changed')
    # The standard writer may emit its common default fields. Every physically
    # supplied source common scalar must retain its exact value and presence.
    source_common=source[source.index([100,'AcDbEntity'])+1:source.index([100,'AcDbSection'])]
    saved_common=packet[packet.index([100,'AcDbEntity'])+1:packet.index([100,'AcDbSection'])]
    for code,value in source_common:
        check(exact(one(saved_common,code))==exact(value),'Saved producer common group changed: '+str(code))
    check(not any(t[0][1] in SETTINGS for t in wire.values()),'Producer output invented a settings object')
    classes(path,wire);return structural_audit(path,year,binary)

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path);parser.add_argument('--repository',type=Path);args=parser.parse_args();root=args.repository or Path(__file__).resolve().parents[1]
    expected={f'section-{kind}-AutoCad{year}-{binary}.dxf' for kind in ('authored','native-name','owned','copy','erased','cross') for year in VERSIONS for binary in (False,True)}
    expected|={f'section-{kind}-AutoCad2018-{binary}.dxf' for kind in ('native','native-copy','native-erased') for binary in (False,True)}
    expected|={f'section-producer-AutoCad{year}-{source_binary}-{binary}.dxf' for year in VERSIONS for source_binary in (False,True) for binary in (False,True)}
    # Adjacent modules have their own mandatory inventories and verifiers.
    # Keep this gate exact for all 70 SECTION outputs when run on the full suite.
    check({p.name for p in args.directory.glob('section-*.dxf') if not p.name.startswith(('section-settings-','section-manager-'))}==expected,'Expected all 70 SECTION output fixtures')
    source=native_source(root);producer_inputs,source_adapted=producer_sources(root);adapted=0;native_audits=0
    for year in VERSIONS:
        for binary in (False,True):
            path=lambda kind:args.directory/f'section-{kind}-AutoCad{year}-{binary}.dxf'
            adapted+=basic(path('authored'),year,binary,'SECTION')
            check(not basic(path('native-name'),year,binary,'SECTIONOBJECT'),'Native-name authoring audit required adaptation');native_audits+=1
            original,ow,a=graph(path('owned'),year,binary,1);adapted+=a
            copied,cw,a=graph(path('copy'),year,binary,2);adapted+=a;original_id=next(iter(original));check(original_id in copied and copied[original_id]==original[original_id],'Clone altered original ownership graph')
            new_ids=set(copied)-set(original);check(len(new_ids)==1,'Clone did not create exactly one independent section');new_id=next(iter(new_ids));check(not copied[new_id]&original[original_id],'Clone reused an owned identity')
            erased,ew,a=graph(path('erased'),year,binary,1);adapted+=a;check(erased==original and not copied[new_id]&set(ew),'Erased owned identities survived')
            cross,_,a=graph(path('cross'),year,binary,1,True);adapted+=a
    for binary in (False,True):
        native(args.directory/f'section-native-AutoCad2018-{binary}.dxf',binary,source)
        copied,_=native(args.directory/f'section-native-copy-AutoCad2018-{binary}.dxf',binary,source,2)
        erased,wire=native(args.directory/f'section-native-erased-AutoCad2018-{binary}.dxf',binary,source)
        check(not (copied-erased)&set(wire),'Native clone erasure retained owned identities');native_audits+=3
    for year in VERSIONS:
        for source_binary in (False,True):
            for binary in (False,True):
                adapted+=producer(args.directory/f'section-producer-AutoCad{year}-{source_binary}-{binary}.dxf',year,binary,producer_inputs[(year,source_binary)])
    check(adapted==56 and native_audits==14,'Expected 56 adapted and 14 unmodified native-name output audits')
    controls=0;path=args.directory/'section-owned-AutoCad2018-False.dxf';ids,wire,_=graph(path,2018,False,1);h=next(iter(ids));settings=one(body(wire[h],'AcDbSection'),360)
    for defect in ('count','owner','literal'):
        corrupted=copy.deepcopy(wire)
        if defect=='count':i=corrupted[h].index([92,2]);corrupted[h][i]=[92,1]
        elif defect=='owner':i=corrupted[settings].index([330,h],corrupted[settings].index([102,'}'])+1);corrupted[settings][i]=[330,'0']
        else:i=next(i for i,t in enumerate(corrupted[h]) if t[0]==1);corrupted[h][i]=[1,'東京 LiteralA']
        try:graph(path,2018,False,1,wire_override=corrupted)
        except ValueError:controls+=1
        else:raise ValueError('A deliberate output-packet corruption escaped detection')
    check(controls==3,'Missing graph corruption controls')
    path=args.directory/'section-producer-AutoCad2018-False-False.dxf';original=records(path)
    for defect in ('invented-settings','common-color-name','back-count'):
        corrupted=copy.deepcopy(original);packet=corrupted['F1000']
        if defect=='invented-settings':packet.append([360,'0'])
        elif defect=='common-color-name':index=next(i for i,t in enumerate(packet) if t[0]==430);packet[index]=[430,'Invented']
        else:index=packet.index([93,2]);packet[index]=[93,1]
        try:producer(path,2018,False,producer_inputs[(2018,False)],wire_override=corrupted)
        except ValueError:controls+=1
        else:raise ValueError('A deliberate producer output corruption escaped detection')
    check(controls==6,'Missing graph or producer corruption controls')
    print(json.dumps({'verified_outputs':70,'pinned_producer_originals_and_exact_extractions':8,'adapted_producer_extracted_source_audits':source_adapted,'unmodified_native_original_audits':1,'unmodified_native_name_audits':native_audits,'adapted_documented_name_structural_audits':adapted,'native_original_transformations':0,'actual_output_packet_corruption_controls':controls},sort_keys=True))
if __name__=='__main__':main()
