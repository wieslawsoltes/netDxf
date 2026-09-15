#!/usr/bin/env python3
"""Qualify SECTION/TABLESTYLE/SUN/FIELD coexistence from actual output packets.

Authored graphs and the added native-graph carriers are explicit scaffolds.
The original LiveSection1 packets are compared against their pinned source;
no FIELD evaluation, section generation, sun calculation or CAD execution occurs.
"""
from pathlib import Path
import argparse, copy, json, struct
import ezdxf
from verify_mleader_inputs import records, check, decode_once
from verify_section import body, one, owner, dictionary, extension, control, classes, structural_audit, native_source
from verify_section_settings import payload as settings_payload
from verify_sun import packet as sun_packet

ROOT = Path(__file__).resolve().parents[1]
YEARS = (2007, 2010, 2013, 2018)

def exact(value):
    if isinstance(value, float): return struct.pack('<d', value)
    if isinstance(value, list): return [exact(item) for item in value]
    if isinstance(value, dict): return {key:exact(item) for key,item in value.items()}
    return value

def decoded(tags):
    return [[code, decode_once(value) if isinstance(value,str) else value] for code,value in tags]

def field_payload(child, targets):
    return [[100,'AcDbField'],[1,'SixthSynthetic'],[2,r'prefix \U+03'],[3,r'A9 Literal\U+005CU+0041'],[90,1],[360,child],[97,7]] + [[331,h] for h in targets] + [
        [91,63],[92,0],[94,43],[95,32],[96,335],[300,'inert evaluator cache'],[93,0],[7,'ACFD_FIELD_VALUE'],[90,0],[91,0],[301,'####'],[98,4],[310,{'hex':'0011ff'}],[320,'F0F0F0']]

def style_payload(line, renamed):
    result=[[3,'Sixth style Ω'],[70,0],[71,0],[40,.125],[41,.25],[280,0],[281,1]]
    for ordinal in range(3): result += [[7,'SIXTH_STYLE_RENAMED_Ω' if renamed else 'sixth_style'],[140,2.5+ordinal],[170,5],[62,3],[63,257],[283,0],[90,512],[91,0],[1,r'Literal\U+0041'],[284,1]]
    return result+[[340,line],[102,'{SIXTH_PRIVATE'],[7,'SIXTH_PRIVATE_STYLE'],[310,{'hex':'0700ff'}],[102,'}']]

def public_style_names(tags):
    names=[];depth=0;active=False
    for code,value in tags:
        if code==102:
            depth += 1 if value.startswith('{') else -1
            check(depth>=0,'Unbalanced TABLESTYLE private context')
        elif depth==0 and code==100: active=value=='AcDbTableStyle'
        elif depth==0 and active and code==7: names.append(decode_once(value))
    check(depth==0,'Unclosed TABLESTYLE private context');return names

def section_body(settings):
    return [[90,4],[91,17],[1,r'Sixth section Ω Literal\U+0041'],[10,[.25,-.5,2.]],[40,7.125],[41,-3.5],[70,37],[62,9],
        [92,2],[11,[1.25,2.5,3.75]],[11,[-4.25,5.5,-6.75]],[93,0],[360,settings]]

def graph(path, year, binary, phase, native=False, override=None, audit=True):
    wire=records(path) if override is None else override
    doc=ezdxf.readfile(path);root=doc.rootdict.dxf.handle;top=dictionary(wire,root)
    check('SIXTH_MIXED' in top,'Mixed root dictionary missing');graph_id=top['SIXTH_MIXED'];entries=dictionary(wire,graph_id)
    check(set(entries)=={'STYLE','FIELD'},'Mixed dictionary inventory differs')
    style,field=entries['STYLE'],entries['FIELD'];check(owner(wire[style])==owner(wire[field])==graph_id,'Mixed dictionary ownership differs')
    check(wire[style][0]==[0,'TABLESTYLE'] and wire[field][0]==[0,'FIELD'],'Mixed object kinds differ')
    fp=wire[field][wire[field].index([100,'AcDbField']):];child=one(fp,360)
    refs=[v for c,v in fp if c==331];check(len(refs)==7,'FIELD reference inventory differs')
    section,settings,sun,referenced_style,line,repeat,null=refs
    check(referenced_style==style and line==repeat and null=='0','FIELD repeated/null/style identities differ')
    check(all(h in wire for h in refs if h!='0'),'FIELD physical target missing')
    check(fp==field_payload(child,refs),'FIELD complete immutable code/cache/dependency packet differs')
    check(wire[child][0]==[0,'FIELD'] and owner(wire[child])==field,'FIELD child kind or reciprocal ownership differs')
    check(body(wire[child],'AcDbField')==[[1,'SixthChild'],[2,'1+1'],[90,0],[97,0],[93,0],[7,'ACFD_FIELD_VALUE'],[90,0]],'FIELD child body differs')
    check(sum(tags[0]==[0,'FIELD'] for tags in wire.values())==2,'Unexpected FIELD object inventory')
    check(wire[line][0]==[0,'LINE'] and one(wire[line],10)==[11.,12.,13.] and one(wire[line],11)==[14.,15.,16.],'Shared external LINE changed')
    renamed=phase in ('renamed','cloned','erased')
    check(exact(decoded(body(wire[style],'AcDbTableStyle')))==exact(style_payload(line,renamed)),'Complete synthetic TABLESTYLE body differs')
    names=public_style_names(wire[style]);check(len(names)==3,'TABLESTYLE public row inventory differs')
    resource=doc.styles.get(names[0]);check(all(doc.styles.get(name) is resource for name in names),'TABLESTYLE resource aliases do not resolve identically')
    check(resource.dxf.handle in wire and wire[resource.dxf.handle][0]==[0,'STYLE'],'TABLESTYLE target is not a physical STYLE')
    check(doc.styles.get('SIXTH_PRIVATE_STYLE') is not resource,'Private STYLE binding conflated')
    check(wire[section][0]==[0,'SECTIONOBJECT'] and one(body(wire[section],'AcDbSection'),360)==settings and owner(wire[settings])==section,'SECTION reciprocal settings differs')
    check(wire[settings][0][1] in ('SECTIONSETTINGS','SECTION_SETTINGS'),'Settings kind differs')
    check(wire[sun][0]==[0,'SUN'],'FIELD SUN target kind differs');host=owner(wire[sun]);check(one(wire[host],361)==sun,'SUN reciprocal ownership differs')
    check(wire[host][0]==[0,'VPORT' if year==2007 else 'VIEW'] and one(wire[host],2)=='SIXTH_SUN_OWNER','SUN host profile/family/name differs')
    expected_sun={90:1,290:1,63:5,421:0x345678,40:1.375,291:1,91:2455826,92:54000000,292:0,70:2,71:512,280:19}
    check(sun_packet([tuple(tag) for tag in wire[sun]])==expected_sun,'SUN scalar packet differs')
    originals={section,settings,sun,field,child,style,graph_id};clone_ids=set()
    if not native:
        check(exact(decoded(body(wire[section],'AcDbSection')))==exact(section_body(settings)),'SECTION exact stored body differs')
        section_ids=[h for h,t in wire.items() if [100,'AcDbSection'] in t];sun_ids=[h for h,t in wire.items() if t[0]==[0,'SUN']]
        count=2 if phase=='cloned' else 1
        check(len(section_ids)==len(sun_ids)==count,'SECTION/SUN clone inventory differs')
        for current in section_ids:
            current_settings=one(body(wire[current],'AcDbSection'),360)
            check(exact(decoded(body(wire[current],'AcDbSection')))==exact(section_body(current_settings)),'Cloned SECTION scalar body differs')
            check(owner(wire[current_settings])==current,'Cloned settings common owner differs')
            kind,types=settings_payload(wire[current_settings]);check(kind==4 and len(types)==1,'Settings outer grammar differs');bundle=types[0]
            expected_sources=[current,line,current_settings] if phase=='context' else [current,line,line,'0',current_settings,style,sun,field]
            check(bundle['type']==4 and bundle['options']==17 and bundle['sources']==expected_sources,'SECTION source order, self map, null or external identities differ')
            check(bundle['destination']==owner(wire[current]) and bundle['file']==r'inert\U+0041.dwg','SECTION destination mapping or inert file differs')
            check(bundle['geometry']==[{90:4,91:8,92:33,62:9,8:'*_BackgroundLines',6:'ByLayer',40:1.125,1:'ByColor',370:-1,70:23,71:41,72:0,2:'',41:0.,42:1.,43:1.}],'SECTION appearance fields differ')
            if phase!='context': check(control(wire[current_settings],'{ACAD_REACTORS')==[[330,current]],'SECTION owner reactor map differs')
            if current!=section: clone_ids|={current,current_settings}
        for current in sun_ids:
            current_host=owner(wire[current]);check(one(wire[current_host],361)==current,'Copied SUN reciprocal slot differs')
            check(sun_packet([tuple(tag) for tag in wire[current]])==expected_sun,'Copied SUN scalar values differ')
            if phase=='context': continue
            ext=extension(wire[current]);slots=dictionary(wire,ext);check(set(slots)=={'LINKS','ALIAS'} and slots['LINKS']==slots['ALIAS'],'SUN extension alias differs')
            note=slots['LINKS'];check(owner(wire[ext])==current and owner(wire[note])==ext,'SUN extension common ownership differs')
            packet=decoded(body(wire[note],'AcDbXrecord'));check(packet==[[280,1],[1,r'links Ω Literal\U+0041'],[330,current_host],[331,current],[340,settings],[350,style],[340,field],[310,{'hex':'0300ff'}]],'SUN owned/raw reference mapping differs')
            if current!=sun: clone_ids|={current,ext,note}
            else: originals|={ext,note}
        if phase=='erased':
            empty=[t for t in wire.values() if t[0][1] in ('VIEW','VPORT') and [2,'SIXTH_SUN_COPY'] in t]
            check(len(empty)==1 and not any(c==361 for c,v in empty[0]),'Erased SUN slot presence returned')
    else:
        check(section=='228' and settings=='22A','Native original source identities relocated')
    definitions={name:doc.classes.get(name).dxf for name in ('FIELD','SUN','TABLESTYLE')}
    for name,cpp in (('FIELD','AcDbField'),('SUN','AcDbSun'),('TABLESTYLE','AcDbTableStyle')):
        definition=definitions[name];check(definition.cpp_class_name==cpp and not definition.is_an_entity and definition.instance_count==sum(t[0]==[0,name] for t in wire.values()),'CLASS identity or physical instance count differs: '+name)
    classes(path,wire)
    if audit: check(not structural_audit(path,year,binary),'Native SECTIONOBJECT unexpectedly required an audit adapter')
    return {'section':section,'settings':settings,'sun':sun,'style':style,'field':field,'child':child,'line':line,'host':host,'originals':originals,'clones':clone_ids},wire

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path);args=parser.parse_args()
    phases=('original','renamed','cloned','erased','context')
    expected={f'sixth-mixed-{phase}-AutoCad{year}-{binary}.dxf' for phase in phases for year in YEARS for binary in (False,True)}
    expected|={f'sixth-mixed-native-AutoCad2018-{binary}.dxf' for binary in (False,True)}
    check({p.name for p in args.directory.glob('sixth-mixed-*.dxf')}==expected,'Mandatory 42 mixed outputs missing or unexpected')
    controls=0
    for year in YEARS:
        for binary in (False,True):
            results={phase:graph(args.directory/f'sixth-mixed-{phase}-AutoCad{year}-{binary}.dxf',year,binary,phase) for phase in phases}
            original,ow=results['original'];cloned,cw=results['cloned'];erased,ew=results['erased']
            check(original['originals']==cloned['originals']==erased['originals'],'Clone/erase changed original identity graph')
            check(len(cloned['clones'])==5 and not cloned['clones']&original['originals'],'Clones reused original owned identities')
            check(not cloned['clones']&set(ew),'Erased clone identities remain physically present')
            for target in ('field','child'):check(ow[original[target]]==ew[erased[target]],'Immutable FIELD packet changed after other family clone/erase')
    source=native_source(ROOT);native_packets=0
    for binary in (False,True):
        ids,wire=graph(args.directory/f'sixth-mixed-native-AutoCad2018-{binary}.dxf',2018,binary,'native',native=True)
        source_map=next(h for h,t in source.items() if t[0]==[0,'CELLSTYLEMAP'])
        for handle,marker in (('228','AcDbSection'),('22A','AcDbSectionSettings'),('87','AcDbTableStyle'),(source_map,'AcDbCellStyleMap')):
            check(exact(body(wire[handle],marker))==exact(body(source[handle],marker)),'Native original body changed: '+handle);native_packets+=1
        check(owner(wire['22A'])=='228','Native settings owner changed')
        check(owner(wire['228'])==owner(source['228']),'Native SECTION common owner changed')
        proxy=lambda tags:[tag for tag in tags[:tags.index([100,'AcDbSection'])] if tag[0] in (160,310)]
        check(proxy(wire['228'])==proxy(source['228']),'Native proxy count or exact byte chunks changed')
        native_extension=extension(source['87'])
        check(extension(wire['87'])==native_extension and owner(wire[native_extension])=='87','Native TABLESTYLE extension identity/owner changed')
        check(dictionary(wire,native_extension)==dictionary(source,native_extension) and owner(wire[source_map])==native_extension,'Native opaque CELLSTYLEMAP ownership or slot changed')
    path=args.directory/'sixth-mixed-cloned-AutoCad2018-False.dxf';ids,wire=graph(path,2018,False,'cloned')
    for defect in ('field-child','field-external','sun-owner','style-private','section-source','missing-owned-clone'):
        corrupt=copy.deepcopy(wire)
        if defect=='field-child':i=corrupt[ids['child']].index([330,ids['field']]);corrupt[ids['child']][i]=[330,ids['style']]
        elif defect=='field-external':i=corrupt[ids['field']].index([331,ids['sun']]);corrupt[ids['field']][i]=[331,ids['line']]
        elif defect=='sun-owner':i=corrupt[ids['sun']].index([330,ids['host']]);corrupt[ids['sun']][i]=[330,ids['section']]
        elif defect=='style-private':i=corrupt[ids['style']].index([7,'SIXTH_PRIVATE_STYLE']);corrupt[ids['style']][i]=[7,'SIXTH_STYLE_RENAMED_Ω']
        elif defect=='section-source':i=corrupt[ids['settings']].index([330,ids['field']]);corrupt[ids['settings']][i]=[330,ids['child']]
        else:corrupt.pop(next(iter(ids['clones'])))
        try:graph(path,2018,False,'cloned',override=corrupt,audit=False)
        except (ValueError,KeyError,StopIteration):controls+=1
        else:raise ValueError('Actual mixed output corruption escaped detection: '+defect)
    check(controls==6,'Expected six detected cross-family output corruptions')
    print(json.dumps({'outputs':42,'authored_profile_transport_graphs':8,'native_complete_outputs':2,'exact_native_bodies':native_packets,
        'negative_controls':controls,'audit_errors':0,'audit_repairs':0,'native_cad_execution':False,'evaluation':False},sort_keys=True))

if __name__=='__main__':main()
