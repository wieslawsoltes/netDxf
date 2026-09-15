#!/usr/bin/env python3
"""Verify native MLEADER envelope presence, private styles and extracted source packets."""
from pathlib import Path
import argparse, copy, gzip, hashlib, io, json, tempfile
import ezdxf
from ezdxf.entities.mleader import acdb_mleader_style
from verify_mleader_inputs import records, exact, json_value, context_semantics, decode_once, nested_context, check, VERSIONS
ROOT=Path(__file__).resolve().parents[1]
PRESENCE=('present','entity-absent','style-absent','both-absent')
COLORS=('absent','explicit-default','nondefault','clear')
OPAQUE=('unknown-before','unknown-after','private-subclass','private-class')

def body(record,name):
    return record[record.index([100,name])+1:]

def normalized(tags):
    return [[code,decode_once(value) if isinstance(value,str) else value] for code,value in tags]

def same_packet(before,after,label):
    check(exact(normalized(before))==exact(normalized(after)),label+': exact ordered packet differs')

def style_fields(before,after,absent):
    old=normalized(body(before,'AcDbMLeaderStyle'));new=normalized(body(after,'AcDbMLeaderStyle'))
    old_values={c:v for c,v in old if c<1000 and not(absent and c==179)}
    new_values={c:v for c,v in new if c<1000}
    check(len(new_values)==sum(c<1000 for c,v in new),'Duplicate style scalar')
    check((179 not in new_values)==absent,'Style envelope physical presence changed')
    check(all(c in new_values and exact(v)==exact(new_values[c]) for c,v in old_values.items()),'Stored style field changed')
    defaults={attribute.code:attribute.default for attribute in acdb_mleader_style.attribs.values()}
    check(all(c in defaults and exact(v)==exact(defaults[c]) for c,v in new_values.items() if c not in old_values),'Nondefault style field invented')
    check(exact([t for t in old if t[0]>=1000])==exact([t for t in new if t[0]>=1000]),'Style XData changed')

def audit(doc):
    result=doc.audit();check(not result.errors and not result.fixes,f'Independent audit: {len(result.errors)} errors/{len(result.fixes)} repairs')

def presence(source,path,year,mode,binary):
    old,new=records(source),records(path);a,b=ezdxf.readfile(source),ezdxf.readfile(path)
    check(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Wrong output transport')
    check(b.dxfversion==VERSIONS[year],'Wrong output profile')
    ea=mode in ('entity-absent','both-absent');sa=mode in ('style-absent','both-absent')
    leaders=[(h,t) for h,t in old.items() if t[0]==[0,'MULTILEADER']]
    check(len(leaders)==len(list(b.modelspace().query('MULTILEADER')))==2,'Presence inventory changed')
    for h,t in leaders:
        expected=[tag for tag in body(t,'AcDbMLeader') if not(ea and tag[0]==270)]
        packet=body(new[h],'AcDbMLeader');same_packet(expected,packet,'Leader '+h)
        check((not any(c==270 for c,v in packet))==ea,'Entity envelope physical presence changed')
        nested_context(packet,year)
        check(exact(context_semantics(json_value(a.entitydb[h].context)))==exact(context_semantics(json_value(b.entitydb[h].context))),'Independent context semantics changed')
    for h,t in old.items():
        if t[0]==[0,'MLEADERSTYLE']:style_fields(t,new[h],sa)
    audit(b)
    return old,new

def colors(source,path,year,mode,binary):
    old,new=records(source),records(path);doc=ezdxf.readfile(path)
    check(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary and doc.dxfversion==VERSIONS[year],'Line-color transport/profile changed')
    expected_color=-1056964608 if mode=='explicit-default' else -1039064525 if mode=='nondefault' else None
    count=0
    for h,tags in old.items():
        if tags[0]!=[0,'MULTILEADER']:continue
        expected=[];inside=False
        for code,value in body(tags,'AcDbMLeader'):
            if [code,value]==[304,'LEADER_LINE{']:inside=True;count+=1
            if inside and code==92:
                if expected_color is not None:expected.append([92,expected_color])
            else:expected.append([code,value])
            if [code,value]==[305,'}']:inside=False
        same_packet(expected,body(new[h],'AcDbMLeader'),'Line-color packet '+h)
        check(all(line.color==(expected_color if expected_color is not None else -1056964608) for node in doc.entitydb[h].context.leaders for line in node.lines),'Independent effective line color changed')
    check(count==5,'Line-color inventory changed');audit(doc)

def expected_private(record,mode):
    tags=copy.deepcopy(record);text=next(i for i,t in enumerate(tags) if t[0]==342);tags[text]=[342,'FEEEE']
    xdata=next((i for i,t in enumerate(tags) if t[0]==1001),len(tags))
    index=next(i for i,t in enumerate(tags) if t[0]==179)+1 if mode=='unknown-before' else xdata
    extra=[[100,'PrivateMLeaderStyleData'],[342,'FDDDD'],[300,'opaque private data']] if mode=='private-subclass' else [[298,1]]
    tags[index:index]=extra
    if not any(c==1001 for c,v in tags):tags.extend([[1001,'QA_MLEADER'],[1000,'private style XData'],[1005,'FCCCC']])
    return tags

def opaque(source,path,year,mode,binary):
    old,new=records(source),records(path);doc=ezdxf.readfile(path)
    check(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary and doc.dxfversion==VERSIONS[year],'Private transport/profile changed')
    styles=[(h,t) for h,t in old.items() if t[0]==[0,'MLEADERSTYLE']]
    selected=styles if mode=='private-class' else styles[:1]
    for h,t in selected:same_packet(expected_private(t,mode),new[h],'Opaque style '+h)
    if mode=='private-class':
        cls=doc.classes.get('MLEADERSTYLE');check(cls.dxf.cpp_class_name=='PrivateMLeaderStyle' and cls.dxf.app_name=='PRIVATE_APP' and cls.dxf.instance_count==19,'Private CLASS changed')
        check(not any(t[0]==[0,'MULTILEADER'] for t in new.values()),'Private CLASS fixture gained leaders')
    else:
        check(len(list(doc.modelspace().query('MULTILEADER')))==2,'Following typed leaders were discarded')
        for h,t in styles[1:]:style_fields(t,new[h],False)
    # Deliberately unresolved private handles are an opaque preservation control.
    # ezdxf's semantic style auditor cannot qualify those private references.
    return old,new

def inflate(path,target):
    data=gzip.decompress(path.read_bytes());target.write_bytes(data);return data

def native_scaffold(source,path,binary,manifest,original):
    old,new=records(source),records(path);orig=records(original);doc=ezdxf.readfile(path)
    check(doc.dxfversion=='AC1021' and path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Native scaffold transport/profile changed')
    for h in manifest['exact_source_handles']:same_packet(orig[h],old[h],'Extracted original '+h)
    check(len(list(doc.modelspace().query('MULTILEADER')))==15,'Native source inventory changed')
    before=ezdxf.readfile(source)
    for h,kind in {'11':'STYLE','14':'LTYPE','107':'APPID','D7':'DICTIONARY','D8':'MLEADERSTYLE','E5':'MLEADERSTYLE','13CD':'DICTIONARY','13CE':'XRECORD'}.items():
        check(new[h][0]==[0,kind],h+': exported resource type changed')
    check(doc.rootdict['ACAD_MLEADERSTYLE'].dxf.handle=='D7','Native style dictionary identity changed')
    check(doc.rootdict['ACAD_MLEADERSTYLE']['Standard'].dxf.handle=='D8' and doc.rootdict['ACAD_MLEADERSTYLE']['Annotative'].dxf.handle=='E5','Native style dictionary membership changed')
    for h in manifest['entity_handles']:
        same_packet(body(old[h],'AcDbMLeader'),body(new[h],'AcDbMLeader'),'Native context '+h)
        leader=doc.entitydb[h]
        check(leader.dxf.style_handle=='D8' and leader.dxf.leader_linetype_handle=='14' and leader.dxf.text_style_handle=='11' and leader.context.mtext.style_handle=='11','Native resource references changed')
        check(not any(c==270 for c,v in body(new[h],'AcDbMLeader')),'Native missing 270 was materialized')
        check(doc.entitydb[h].dxf.owner=='1F','Original model-space owner changed')
        check(exact(context_semantics(json_value(before.entitydb[h].context)))==exact(context_semantics(json_value(doc.entitydb[h].context))),'Native context semantics changed')
    for h in manifest['style_handles']:style_fields(old[h],new[h],True)
    for h in ('13CD','13CE'):same_packet(old[h],new[h],'Native extension closure '+h)
    check(doc.entitydb['B27'].extension_dict.handle=='13CD','Native extension attachment changed')
    for kind,count in (('MULTILEADER',15),('MLEADERSTYLE',2)):
        check(doc.classes.get(kind).dxf.instance_count==count,kind+' count changed')
    audit(doc)

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path);args=parser.parse_args()
    expected={f'native-presence-AutoCad{y}-{m}-{t}.dxf' for y in VERSIONS for m in PRESENCE for t in ('text','binary')}
    expected|={f'native-color-AutoCad{y}-{m}-{t}.dxf' for y in VERSIONS for m in COLORS for t in ('text','binary')}
    expected|={f'native-opaque-AutoCad{y}-{m}-{t}.dxf' for y in VERSIONS for m in OPAQUE for t in ('text','binary')}
    expected|={f'native-original-R2007-{t}.dxf' for t in ('text','binary')}
    expected|={f'native-full-{n}-{t}.dxf' for n in ('acad_table_simple','acad_table_with_blk_ref') for t in ('text','binary')}
    check({p.name for p in args.directory.glob('native-*.dxf')}==expected,'Expected every one of 102 native/envelope/color outputs')
    producers=json.loads((ROOT/'tests/fixtures/mleader/manifest.json').read_text())
    for y in VERSIONS:
        source=ROOT/f'tests/fixtures/mleader/independent-mleader-R{y}.dxf'
        check(hashlib.sha256(source.read_bytes()).hexdigest()==next(f['sha256'] for f in producers['fixtures'] if f['year']==y),'Independent producer digest mismatch')
        for binary in (False,True):
            transport='binary' if binary else 'text'
            for mode in COLORS:
                path=args.directory/f'native-color-AutoCad{y}-{mode}-{transport}.dxf';colors(source,path,y,mode,binary)
            for mode in PRESENCE:
                path=args.directory/f'native-presence-AutoCad{y}-{mode}-{transport}.dxf';presence_old,presence_new=presence(source,path,y,mode,binary)
            for mode in OPAQUE:
                path=args.directory/f'native-opaque-AutoCad{y}-{mode}-{transport}.dxf';opaque_old,opaque_new=opaque(source,path,y,mode,binary)
    fixture_root=ROOT/'tests/fixtures/mleader-native';manifest=json.loads((fixture_root/'manifest.json').read_text())
    pinned=json.loads((ROOT/'tools/table_oracle/fixtures.json').read_text())
    with tempfile.TemporaryDirectory() as folder:
        folder=Path(folder);source=folder/'native.dxf';data=inflate(fixture_root/manifest['fixture'],source)
        check(hashlib.sha256(data).hexdigest()==manifest['decoded_sha256'],'Extracted fixture digest mismatch')
        original=folder/'original.dxf';data=inflate(ROOT/manifest['source_file'],original)
        check(hashlib.sha256(data).hexdigest()==manifest['source_sha256'],'Original fixture digest mismatch')
        for binary in (False,True):native_scaffold(source,args.directory/f"native-original-R2007-{'binary' if binary else 'text'}.dxf",binary,manifest,original)
        for name in ('acad_table_simple','acad_table_with_blk_ref'):
            source=folder/(name+'.dxf');data=inflate(ROOT/f'tests/fixtures/table-oracle/{name}.dxf.gz',source)
            check(hashlib.sha256(data).hexdigest()==next(f['sha256'] for f in pinned['files'] if f['file']==name+'.dxf'),'Pinned full source digest mismatch')
            old=records(source)
            for binary in (False,True):
                path=args.directory/f"native-full-{name}-{'binary' if binary else 'text'}.dxf";new=records(path);doc=ezdxf.readfile(path)
                check(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Full source transport changed')
                for h,t in old.items():
                    if t[0]==[0,'MLEADERSTYLE']:same_packet(t,new[h],'Original private style '+h)
                # This check concerns original MLEADERSTYLE preservation only.
                # ACAD_TABLE and other original source families are qualified separately.
    leader=next(h for h,t in presence_new.items() if t[0]==[0,'MULTILEADER'])
    before=body(presence_new[leader],'AcDbMLeader');after=[[270,2]]+copy.deepcopy(before)
    style=next(h for h,t in opaque_new.items() if t[0]==[0,'MLEADERSTYLE'])
    old_style=opaque_new[style];bad_style=copy.deepcopy(old_style)
    at=next(i for i,t in enumerate(bad_style) if t[0]==298);bad_style[at]=[298,0]
    # A protected literal escape on an actual context string must stay protected.
    protected=copy.deepcopy(before);literal_at=next(i for i,t in enumerate(protected) if t[0]==304 and t[1]!='LEADER_LINE{')
    protected[literal_at]=[304,r'literal \U+005CU+0041']
    unprotected=copy.deepcopy(protected);unprotected[literal_at]=[304,r'literal \U+0041']
    failures=0
    for before,after in ((before,after),(old_style,bad_style),(protected,unprotected)):
        try:same_packet(before,after,'corruption control')
        except ValueError:failures+=1
    check(failures==3,'Corruption controls failed to detect envelope insertion/private-field/literal-escape change')
    print(f'PASS ezdxf {ezdxf.__version__}: 102 outputs, 15 exact native leader bodies in both transports, 32 line-color presence/value cases, optional envelope presence, opaque styles, fixup controls and 3 rejected corruptions')
if __name__=='__main__':main()
