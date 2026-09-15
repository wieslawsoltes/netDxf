#!/usr/bin/env python3
"""Verify published stored envelopes using independent low-level ezdxf tag parsing.

The source fixtures use independently schema-authored tags over ezdxf scaffolding.
They do not qualify native filter execution, ASE semantics, or historical legality.
ezdxf's high-level LayerFilter omits group8 names; it is not a payload oracle.
"""
from pathlib import Path
import argparse,hashlib,json
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from verify_typed_container_inputs import wire_records
ROOT=Path(__file__).resolve().parents[1]
VERSIONS={2000:'AC1015',2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}
NAMES=['Alpha','CaseLayer','Alpha','caselayer','東京',r'Literal\U+0041','Unresolved']
KEYS={'NAMES','EMPTY_FILTER','POINTER','EMPTY_POINTER','NAMES_ALIAS'}
def check(condition,message):
 if not condition:raise ValueError(message)
def payload(tags):
 start=next((i for i,t in enumerate(tags) if t.code in(100,1001)),len(tags))
 end=next((i for i in range(start,len(tags)) if tags[i].code==1001),len(tags))
 return [(t.code,decode_dxf_unicode(t.value) if t.code==8 else t.value) for t in tags[start:end]]
def reactors_wire(tags):
 start=next((i for i,t in enumerate(tags) if t.code==102 and t.value=='{ACAD_REACTORS'),None)
 if start is None:return []
 end=next(i for i in range(start+1,len(tags)) if tags[i].code==102)
 return [(t.code,t.value) for t in tags[start+1:end]]
def dictionary_edges(tags):
 result=[];name=None
 for t in tags:
  if t.code==3:name=decode_dxf_unicode(t.value)
  elif t.code in(350,360):
   check(name is not None,'Dictionary handle lacks a name');result.append((name,t.code,t.value));name=None
 return result
def verify(path,year,binary,fixture=None,source=None):
 check(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Actual transport differs')
 doc=ezdxf.readfile(path);check(doc.dxfversion==VERSIONS[year],'Actual version differs');wire=wire_records(path)
 app=doc.rootdict['QA_FILTER_POINTER'];check(set(app.keys())==KEYS,'Dictionary entry inventory differs')
 check(app.dxf.hard_owned==1,'Dictionary hard ownership differs')
 check(app['NAMES_ALIAS'] is app['NAMES'],'Alias no longer identifies same object')
 edges=dictionary_edges(wire[app.dxf.handle]);check(len(edges)==5,'Dictionary physical edge count differs')
 if not fixture:check(edges==[(n,350 if n=='NAMES_ALIAS' else 360,app[n].dxf.handle) for n in ('NAMES','EMPTY_FILTER','POINTER','EMPTY_POINTER','NAMES_ALIAS')],'Authored dictionary edge strengths/order differ')
 for name in ('NAMES','EMPTY_FILTER','POINTER','EMPTY_POINTER'):
  item=app[name];kind='LAYER_FILTER' if name in('NAMES','EMPTY_FILTER') else 'OBJECT_PTR'
  check(item.dxftype()==kind,name+': wrong object type')
  check(item.dxf.owner==app.dxf.handle,name+': wrong owner')
  expected=[(100,'AcDbFilter'),(100,'AcDbLayerFilter')]+([(8,n) for n in NAMES] if name=='NAMES' else []) if kind=='LAYER_FILTER' else []
  check(payload(wire[item.dxf.handle])==expected,name+': ordered public payload differs')
  check(len([r for r in wire.values() if r[0].value==kind])==2,kind+': physical count differs')
  reactors=[app.dxf.handle]
  if name=='POINTER':reactors.append(list(doc.modelspace().query('LINE'))[0].dxf.handle)
  check(set(item.get_reactors())==set(reactors) and len(reactors_wire(wire[item.dxf.handle]))==len(reactors),name+': common reactors differ or contain duplicate tags')
  if name.startswith('EMPTY'):check(item.xdata is None or not item.xdata.data,name+': acquired application data')
 pointer=app['POINTER'];names=app['NAMES'];line=list(doc.modelspace().query('LINE'))
 check(len(doc.modelspace())==1 and len(line)==1,'Following geometry inventory differs');line=line[0]
 check(tuple(line.dxf.start)==(1.25,-2.5,3.75) and tuple(line.dxf.end)==(4.5,5.25,-6.125),'Following LINE geometry differs')
 check(list(names.get_xdata('QA_FILTER_POINTER'))==[(1000,'filter metadata'),(1005,pointer.dxf.handle)],'Filter XData differs')
 check(list(pointer.get_xdata('DC015'))==[(1000,'uninterpreted application data'),(1005,names.dxf.handle)],'Pointer XData differs')
 extension=pointer.get_extension_dict().dictionary;record=extension['PAYLOAD']
 check(extension.dxf.owner==pointer.dxf.handle and record.dxf.owner==extension.dxf.handle,'Extension ownership differs')
 check(list(record.tags)==[(1,'owned extension'),(330,line.dxf.handle)],'Owned XRECORD payload/reference differs')
 for name,cpp,application,flags in [('LAYER_FILTER','AcDbLayerFilter','ObjectDBX Classes',0),('OBJECT_PTR','CAseDLPNTableRecord','',1)]:
  definition=doc.classes.get(name).dxf
  check((definition.cpp_class_name,definition.app_name,definition.flags,definition.was_a_proxy,definition.is_an_entity)==(cpp,application,flags,0,0),name+': CLASS metadata differs')
  check(definition.instance_count==2 if year>=2004 else not definition.hasattr('instance_count'),name+': CLASS count/profile differs')
 if fixture:
  check(app.dxf.handle==fixture['dictionary'],'Source dictionary identity differs')
  for name,handle in fixture['objects'].items():check(app[name].dxf.handle==handle,name+': source identity differs')
  check((extension.dxf.handle,record.dxf.handle,line.dxf.handle)==(fixture['extension'],fixture['xrecord'],fixture['line']),'Source child identity differs')
  original=ezdxf.readfile(source);before=original.rootdict['QA_FILTER_POINTER'];oldwire=wire_records(source)
  check(edges==dictionary_edges(oldwire[before.dxf.handle]),'Source dictionary edge strengths/order changed')
  for name in ('NAMES','EMPTY_FILTER','POINTER','EMPTY_POINTER'):
   old=before[name];new=app[name]
   check(payload(oldwire[old.dxf.handle])==payload(wire[new.dxf.handle]),name+': source ordered body changed')
   check(reactors_wire(oldwire[old.dxf.handle])==reactors_wire(wire[new.dxf.handle]),name+': source reactor order changed')
 audit=doc.audit();check(not audit.errors and not audit.fixes,'Ancillary common-envelope audit changes: '+str(audit.errors+audit.fixes))
def main():
 parser=argparse.ArgumentParser();parser.add_argument('artifacts',type=Path);args=parser.parse_args()
 expected={f'{prefix}AutoCad{year}-{binary}.dxf' for prefix in('layer-filter-pointer-','independent-layer-filter-pointer-') for year in VERSIONS for binary in(False,True)}
 actual={p.name for pattern in('layer-filter-pointer-*.dxf','independent-layer-filter-pointer-*.dxf') for p in args.artifacts.glob(pattern)}
 check(actual==expected,'Missing/extra artifacts: '+str(actual^expected))
 fixture_root=ROOT/'tests/fixtures/layer-filter-pointer';manifest=json.loads((fixture_root/'manifest.json').read_text())
 check(manifest['producer']=='independent low-level Autodesk schema insertion into ezdxf 1.4.4 scaffolding' and manifest['native_semantics_qualified'] is False,'Fixture provenance differs')
 check(manifest['layer_names']==NAMES,'Fixture expected names differ')
 fixtures=manifest['fixtures'];check(len(fixtures)==6 and {(f['file'],f['version']) for f in fixtures}=={(f'independent-layer-filter-pointer-R{year}.dxf',version) for year,version in VERSIONS.items()},'Missing/extra fixture profiles')
 for f in fixtures:check(hashlib.sha256((fixture_root/f['file']).read_bytes()).hexdigest()==f['sha256'],'Fixture hash differs')
 for year in VERSIONS:
  fixture=next(f for f in fixtures if f['version']==VERSIONS[year]);source=fixture_root/fixture['file']
  verify(source,year,False,fixture,source)
  for binary in(False,True):
   for prefix in('layer-filter-pointer-','independent-layer-filter-pointer-'):
    path=args.artifacts/f'{prefix}AutoCad{year}-{binary}.dxf'
    verify(path,year,binary,fixture if prefix.startswith('independent') else None,source)
    print('PASS',path.name)
 print('24 stored-envelope outputs: 48 layer filters, 48 object pointers; public tags and common graphs retained.')
if __name__=='__main__':main()
