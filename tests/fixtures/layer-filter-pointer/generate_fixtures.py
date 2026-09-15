#!/usr/bin/env python3
"""Low-level public-schema fixtures, with ezdxf scaffolding; no native semantics claim.

The installed ezdxf LayerFilter writes330 handles instead of Autodesk's8 names.
Placeholder records are therefore replaced as ordered tags, with no load/resave
following insertion. An independent audit checks only the common envelope.
"""
from pathlib import Path
import hashlib,json
import ezdxf
from ezdxf.entities import DXFClass
ROOT=Path(__file__).resolve().parent
YEARS=(2000,2004,2007,2010,2013,2018)
NAMES=['Alpha','CaseLayer','Alpha','caselayer','東京',r'Literal\U+0041','Unresolved']
def encoded(value):
 units=value.encode('utf-16-le')
 return ''.join(chr(c) if c<128 and c!=92 else f'\\U+{c:04X}' for c in (int.from_bytes(units[i:i+2],'little') for i in range(0,len(units),2)))
def records(text):
 lines=text.splitlines(); result=[]; current=[]
 for i in range(0,len(lines),2):
  tag=(int(lines[i]),lines[i+1])
  if tag[0]==0 and current:result.append(current);current=[]
  current.append(tag)
 if current:result.append(current)
 return result
def main():
 assert ezdxf.__version__=='1.4.4'
 manifest={'producer':'independent low-level Autodesk schema insertion into ezdxf 1.4.4 scaffolding','native_semantics_qualified':False,'layer_names':NAMES,'fixtures':[],
 'sources':['https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-3B44DCFD-FA96-482B-8468-37B3C5B5F289.htm','https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-6D6885E2-281C-410A-92FB-8F6A7F54C9DF.htm','https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-DBDBE57E-9045-46A5-9FB9-99CADEB81CF0.htm'],
 'limitations':['No historical/native legality or ASE/filter execution qualification.','ezdxf high-level LayerFilter emits330 instead of the published8 names; ordered tags are the oracle.','OBJECT_PTR class defaults use the Autodesk row; unspecified application name is empty. No DC015 semantics is inferred.']}
 for year in YEARS:
  doc=ezdxf.new(f'R{year}')
  for name in ['Alpha','CaseLayer','東京']:doc.layers.new(name)
  for name in ['QA_FILTER_POINTER','DC015']:doc.appids.new(name)
  line=doc.modelspace().add_line((1.25,-2.5,3.75),(4.5,5.25,-6.125))
  app=doc.rootdict.add_new_dict('QA_FILTER_POINTER',hard_owned=True);objects={}
  for name in ['NAMES','EMPTY_FILTER','POINTER','EMPTY_POINTER']:
   item=doc.objects.add_dxf_object_with_reactor('ACDBPLACEHOLDER',{'owner':app.dxf.handle});app.add(name,item);objects[name]=item
  app.add('NAMES_ALIAS',objects['NAMES'])
  objects['NAMES'].set_xdata('QA_FILTER_POINTER',[(1000,'filter metadata'),(1005,objects['POINTER'].dxf.handle)])
  objects['POINTER'].set_xdata('DC015',[(1000,'uninterpreted application data'),(1005,objects['NAMES'].dxf.handle)])
  objects['POINTER'].set_reactors([app.dxf.handle,line.dxf.handle])
  extension=objects['POINTER'].new_extension_dict().dictionary;xrecord=doc.objects.add_xrecord(owner=extension.dxf.handle)
  xrecord.extend([(1,'owned extension'),(330,line.dxf.handle)]);extension.add('PAYLOAD',xrecord)
  for name,cpp,application,flags in [('LAYER_FILTER','AcDbLayerFilter','ObjectDBX Classes',0),('OBJECT_PTR','CAseDLPNTableRecord','',1)]:
   doc.classes.register(DXFClass.new(dxfattribs={'name':name,'cpp_class_name':cpp,'app_name':application,'flags':flags,'is_an_entity':0,'was_a_proxy':0,'instance_count':2}))
  path=ROOT/f'independent-layer-filter-pointer-R{year}.dxf';doc.saveas(path);packets=records(path.read_text(encoding=doc.output_encoding))
  handles={item.dxf.handle:name for name,item in objects.items()}
  for packet in packets:
   handle=next((v for c,v in packet if c==5),None)
   if handle in handles:
    name=handles[handle];is_filter=name in ('NAMES','EMPTY_FILTER');packet[0]=(0,'LAYER_FILTER' if is_filter else 'OBJECT_PTR')
    if is_filter:
     offset=next((i for i,t in enumerate(packet) if t[0]==1001),len(packet));packet[offset:offset]=[(100,'AcDbFilter'),(100,'AcDbLayerFilter')]+([(8,encoded(v)) for v in NAMES] if name=='NAMES' else [])
   if packet[0]==(0,'CLASS') and ((1,'LAYER_FILTER') in packet or (1,'OBJECT_PTR') in packet):packet[:]=[(c,'2' if c==91 else v) for c,v in packet]
  path.write_text(''.join(f'{c:3}\n{v}\n' for packet in packets for c,v in packet),encoding=doc.output_encoding)
  audit=ezdxf.readfile(path).audit();assert not audit.errors and not audit.fixes,(year,audit.errors,audit.fixes)
  manifest['fixtures'].append({'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'version':doc.dxfversion,'objects':{n:i.dxf.handle for n,i in objects.items()},'dictionary':app.dxf.handle,'extension':extension.dxf.handle,'xrecord':xrecord.dxf.handle,'line':line.dxf.handle})
  print('PASS',path.name,'low-level public fields; ancillary audit unchanged')
 (ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False)+'\n')
if __name__=='__main__':main()
