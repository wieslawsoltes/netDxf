#!/usr/bin/env python3
"""Qualify LIGHTLIST storage with pinned synthetic IxMilia inputs and raw tag checks.

Neither producer version values nor zero ancillary ezdxf audits certify native
AutoCAD semantics. ezdxf has no LIGHTLIST model; IxMilia drops stored entry names.
The ordered100/90/90/(5,1)* packet is therefore checked independently at tag level.
"""
from pathlib import Path
import argparse,hashlib,json
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from verify_typed_container_inputs import wire_records
ROOT=Path(__file__).resolve().parents[1]
VERSIONS={2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}
NAMES=['Stored alias 青','',r'Literal\U+0041 🧪']
STORED_VERSIONS={-2147483648,-7,0,42,2147483647}
def check(value,message):
 if not value:raise ValueError(message)
def packet(tags):
 start=next(i for i,t in enumerate(tags) if t.code==100)
 end=next((i for i in range(start,len(tags)) if tags[i].code==1001),len(tags))
 body=tags[start:end]
 check(len(body)>=3 and [(x.code,x.value) for x in body[:1]]==[(100,'AcDbLightList')],'LIGHTLIST subclass differs')
 check([x.code for x in body[1:3]]==[90,90],'Version/count grammar differs')
 count=body[2].value;check(count>=0 and len(body)==3+2*count,'Count/pair boundary differs')
 pairs=[]
 for i in range(3,len(body),2):
  check((body[i].code,body[i+1].code)==(5,1),'LIGHT/name pair order differs')
  pairs.append((body[i].value,decode_dxf_unicode(body[i+1].value)))
 return body[1].value,pairs

def value(tags,code):return next(t.value for t in tags if t.code==code)
def common(doc,wire,handle,owner,expected_version,pairs,extension):
 item=doc.entitydb[handle];tags=wire[handle];check(item.dxftype()=='LIGHTLIST','Type differs')
 check(value(tags,5)==handle and item.dxf.owner==owner,'Identity or owner differs')
 check(packet(tags)==(expected_version,pairs),'Exact signed version/order/independent names differ')
 check(item.get_reactors()==[owner],'Owner reactor packet differs')
 check(list(item.get_xdata('QA_LIGHTLIST'))==[(1000,'list metadata'),(1005,pairs[0][0] if pairs else '1D')],'Terminal XData differs')
 for light,name in pairs:check(light in wire and wire[light][0].value=='LIGHT','Payload5 does not reference an actual LIGHT')
 check(item.has_extension_dict==bool(extension),'Extension presence differs')
 if extension:
  ext=item.get_extension_dict().dictionary;record=ext['PAYLOAD'];check(ext.dxf.owner==handle and record.dxf.owner==ext.dxf.handle,'Extension ownership differs')
  check(set(ext.keys())=={'PAYLOAD'} and list(record.tags)==[(1,'extension payload'),(90,1701),(310,bytes([0,1,255,0]))],'Extension payload differs')
  if isinstance(extension,str):check(ext.dxf.handle==extension,'Source extension identity differs')

def precheck(path,year,binary,count,classes=True):
 check(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Filename/actual transport differs')
 doc=ezdxf.readfile(path);wire=wire_records(path);check(doc.dxfversion==VERSIONS[year],'Filename/actual profile differs')
 handles=[h for h,r in wire.items() if r[0].value=='LIGHTLIST'];check(len(handles)==count,'Physical LIGHTLIST inventory differs')
 if classes:
  definition=doc.classes.get('LIGHTLIST').dxf
  check((definition.cpp_class_name,definition.app_name,definition.flags,definition.is_an_entity,definition.instance_count)==('AcDbLightList','SCENEOE',1025,0,count),'LIGHTLIST class metadata/count differs')
 lights=[(h,r) for h,r in wire.items() if r[0].value=='LIGHT'];check(len(lights)==2,'Actual LIGHT inventory differs')
 by_name={value(r,1):(h,r) for h,r in lights}
 names=('Actual light A','Actual light B') if count==5 else ('Actual A','Actual B')
 check(set(by_name)==set(names),'Independent actual LIGHT names differ')
 for name,position,target in zip(names,[(1.25,-2.5,4),(-8,9,2)],[(5,7,-1),(2,4,6)]):
  r=by_name[name][1];check(tuple(value(r,10))==position and tuple(value(r,11))==target,'Referenced LIGHT vectors differ')
 lines=list(doc.modelspace().query('LINE'));check(len(doc.modelspace())==3 and len(lines)==1,'Following entity inventory differs')
 check(tuple(lines[0].dxf.start)==(21,22,23) and tuple(lines[0].dxf.end)==(31,32,33),'Following LINE vectors differ')
 audit=doc.audit();check(not audit.errors and not audit.fixes,'Ancillary common graph audit changed: '+str(audit.errors+audit.fixes))
 return doc,wire,by_name

def independent(path,fixture,classes=True):
 doc,wire,_=precheck(path,fixture['year'],fixture.get('output_binary',fixture['binary']),5,classes)
 app=doc.rootdict['QA_LIGHTLISTS'];check(app.dxf.handle==fixture['dictionary'],'Source dictionary identity differs')
 check(set(app.keys())=={f"VERSION_{i}" for i in STORED_VERSIONS},'Source dictionary names differ')
 for entry in fixture['lists']:
  handle=entry['handle'];check(app[f"VERSION_{entry['stored_version']}"] is doc.entitydb[handle],'Dictionary entry target differs')
  common(doc,wire,handle,entry['owner'],entry['stored_version'],[(e['light'],e['name']) for e in entry['entries']],entry['extension'])
 check(set(fixture['lights'])=={h for h,r in wire.items() if r[0].value=='LIGHT'},'Source LIGHT identities differ')
 check(doc.entitydb[fixture['line']].dxftype()=='LINE','Source following LINE identity differs')
 return doc,wire

def authored(path,year,binary,source=None):
 doc,wire,lights=precheck(path,year,binary,2);app=doc.rootdict['QA_LIGHTLISTS'];check(set(app.keys())=={'MAIN','ALIAS','EMPTY'},'Authored dictionary inventory differs')
 main=app['MAIN'];check(main is app['ALIAS'],'Alias no longer shares identity')
 handles=[lights['Actual A'][0],lights['Actual B'][0],lights['Actual A'][0]]
 common(doc,wire,main.dxf.handle,app.dxf.handle,42,list(zip(handles,NAMES)),True)
 empty=app['EMPTY'];check(packet(wire[empty.dxf.handle])==(-2147483648,[]) and empty.dxf.owner==app.dxf.handle,'Empty list version/owner differs')
 edges=[(t.code,t.value) for t in wire[app.dxf.handle] if t.code in(3,350,360)]
 check(edges==[(3,'MAIN'),(360,main.dxf.handle),(3,'ALIAS'),(350,main.dxf.handle),(3,'EMPTY'),(360,empty.dxf.handle)],'Alias edge hard/soft flags differ')
 if source:
  old=ezdxf.readfile(source);oldwire=wire_records(source);oldapp=old.rootdict['QA_LIGHTLISTS'];oldmain=oldapp['MAIN']
  check(main.dxf.handle!=oldmain.dxf.handle and app.dxf.handle!=oldapp.dxf.handle,'Mapped clone did not allocate distinct object identities')
  oldpairs=packet(oldwire[oldmain.dxf.handle])[1];check(all(a!=b for (a,_),(b,_) in zip(oldpairs,packet(wire[main.dxf.handle])[1])),'Mapped clone retained source LIGHT handles')
  check(main.get_extension_dict().dictionary.dxf.handle!=oldmain.get_extension_dict().dictionary.dxf.handle,'Mapped clone retained source extension identity')

def main():
 parser=argparse.ArgumentParser();parser.add_argument('artifacts',type=Path);args=parser.parse_args()
 root=ROOT/'tests/fixtures/lightlist';manifest=json.loads((root/'manifest.json').read_text(encoding='utf-8'))
 check(manifest['producer']=='IxMilia.Dxf 0.8.4' and manifest['secondary_transport_writer']=='ezdxf 1.4.4 low-level tag writer' and manifest['native_autocad'] is False,'Synthetic producer qualification changed')
 fixtures=manifest['fixtures'];expected_inputs={(f'independent-lightlist-R{year}-{transport}.dxf',year,version,transport=='binary') for year,version in VERSIONS.items() for transport in('ascii','binary')}
 check(len(fixtures)==8 and {(f['file'],f['year'],f['version'],f['binary']) for f in fixtures}==expected_inputs,'Exact eight source profiles differ')
 expected={f'lightlist-{prefix}AutoCad{year}-{binary}.dxf' for prefix in('','clone-') for year in VERSIONS for binary in(False,True)}
 expected|={Path(f['file']).stem+f'-roundtrip-{transport}.dxf' for f in fixtures for transport in('ascii','binary')}
 actual={p.name for pattern in('lightlist-*.dxf','independent-lightlist-*.dxf') for p in args.artifacts.glob(pattern)}
 check(actual==expected,'Missing/extra32 output inventory: '+str(actual^expected))
 for f in fixtures:
  source=root/f['file'];check(hashlib.sha256(source.read_bytes()).hexdigest()==f['sha256'],'Raw source hash differs')
  check({x['stored_version'] for x in f['lists']}==STORED_VERSIONS,'Explicit source version coverage differs')
  independent(source,f,False)
  for binary in(False,True):
   path=args.artifacts/(source.stem+f'-roundtrip-{"binary" if binary else "ascii"}.dxf');independent(path,dict(f,output_binary=binary));print('PASS',path.name)
 for year in VERSIONS:
  for binary in(False,True):
   source=args.artifacts/f'lightlist-AutoCad{year}-{binary}.dxf';copy=args.artifacts/f'lightlist-clone-AutoCad{year}-{binary}.dxf'
   authored(source,year,binary);authored(copy,year,binary,source);print('PASS',source.name);print('PASS',copy.name)
 print('PASS32 LIGHTLIST storage outputs:112 objects; explicit signed versions, ordered real LIGHT references, independent names, common metadata and remapped graph clones. No native semantic-version qualification.')
if __name__=='__main__':main()
