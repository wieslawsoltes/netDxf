#!/usr/bin/env python3
"""IxMilia.Dxf 0.8.4 synthetic LIGHTLIST storage corpus with explicit raw augmentations.

Run `dotnet run --project producer -- OUTPUT` first, then `python generate.py OUTPUT`.
This is independent library-produced storage evidence, never native AutoCAD/version evidence.
"""
from pathlib import Path
import argparse, hashlib, io, json
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from ezdxf.lldxf.tagwriter import BinaryTagWriter
ROOT=Path(__file__).resolve().parent
VERSIONS={2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}
NAMES=['Stored alias 青', '', r'Literal\U+0041 🧪']
def records(path):
 data=path.read_bytes(); loader=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'),newline=None))
 result=[];current=[]
 for t in loader:
  if t.code==0 and current:result.append(current);current=[]
  current.append((t.code,cast_tag_value(t.code,t.value)))
 if current:result.append(current)
 return result

def output(path,rs,version,binary):
 if binary:
  with path.open('wb') as f:
   writer=BinaryTagWriter(f,dxfversion=version,encoding='utf8');writer.write_signature()
   for r in rs:
    for code,value in r:writer.write_tag2(code,value)
 else:
  with path.open('w',encoding='utf-8',newline='\n') as f:
   for r in rs:
    for code,value in r:
     if isinstance(value,bytes):value=value.hex().upper()
     f.write(f'{code:3}\n{value}\n')

def main():
 parser=argparse.ArgumentParser();parser.add_argument('producer_output',type=Path);args=parser.parse_args()
 manifest={'producer':'IxMilia.Dxf 0.8.4','secondary_transport_writer':'ezdxf 1.4.4 low-level tag writer','native_autocad':False,'qualification':'Published storage grammar only; all version values explicitly synthetic and uninterpreted.',
 'augmentations':['Remove IxMilia default empty optional handle fields330..369; they are not valid hexadecimal DXF handles.','Remove IxMilia default unscoped STYLE1071=0 (2010+) lacking required1001 application marker; no LIGHTLIST data is involved.','Add missing root dictionary owner330=0 (IxMilia emits no root owner).','Replace each populated LIGHTLIST group1 with independent names; IxMilia derives these names from LIGHT.Name and discards them when reading.','Add documented owner reactors, one extension dictionary/XRECORD, APPID and terminal XData, and update HANDSEED.','Use a custom QA_LIGHTLISTS dictionary; this does not qualify ACAD_LIGHT application placement.','Re-encode augmented low-level tags in the original transport; neither high-level producer saves them again.'], 'stored_names':NAMES,'fixtures':[]}
 for year,version in VERSIONS.items():
  for binary in (False,True):
   transport='binary' if binary else 'ascii';source=args.producer_output/f'ixmilia-lightlist-R{year}-{transport}.dxf';rs=records(source)
   rs=[[t for t in r if not (330 <= t[0] <= 369 and t[1]=='') and not (r[0]==(0,'STYLE') and t==(1071,0))] for r in rs]
   lists=[r for r in rs if r[0]==(0,'LIGHTLIST')];assert len(lists)==5
   source_bodies={next(v for c,v in r if c==5):list(r[r.index((100,'AcDbLightList')):]) for r in lists}
   root=next(r for r in rs if r[0]==(0,'DICTIONARY'));root_handle=next(v for c,v in root if c==5);root.insert(2,(330,'0'))
   app=next(r for r in rs if r[0]==(0,'TABLE') and (2,'APPID') in r);table_handle=next(v for c,v in app if c==5)
   for i,(c,v) in enumerate(app):
    if c==70:app[i]=(70,v+1)
   index=rs.index(app)+1
   while rs[index][0]!=(0,'ENDTAB'):index+=1
   rs.insert(index,[(0,'APPID'),(5,'502'),(330,table_handle),(100,'AcDbSymbolTableRecord'),(100,'AcDbRegAppTableRecord'),(2,'QA_LIGHTLIST'),(70,0)])
   entries=[]
   for i,r in enumerate(lists):
    identity=next(v for c,v in r if c==5);owner=next(v for c,v in r if c==330)
    r[2:2]=[(102,'{ACAD_REACTORS'),(330,owner),(102,'}')]
    if i==0:r[2:2]=[(102,'{ACAD_XDICTIONARY'),(360,'500'),(102,'}')]
    names=iter(NAMES)
    for j,(c,v) in enumerate(r):
     if c==1:r[j]=(1,next(names).replace('\\',r'\U+005C'))
    r.extend([(1001,'QA_LIGHTLIST'),(1000,'list metadata'),(1005,'1D')])
    start=r.index((100,'AcDbLightList'));body=r[start+1:];stored=body[0][1];count=body[1][1]
    assert source_bodies[identity][1:3]==[(90,stored),(90,count)]
    entries.append({'handle':identity,'owner':owner,'stored_version':stored,'entries':[{'light':body[2+j*2][1],'name':NAMES[j]} for j in range(count)],'extension':'500' if i==0 else None})
   objects=next(i for i,r in enumerate(rs) if r[:2]==[(0,'SECTION'),(2,'OBJECTS')]);end=next(i for i in range(objects+1,len(rs)) if rs[i][0]==(0,'ENDSEC'))
   rs[end:end]=[[(0,'DICTIONARY'),(5,'500'),(330,entries[0]['handle']),(100,'AcDbDictionary'),(280,1),(281,1),(3,'PAYLOAD'),(360,'501')],[(0,'XRECORD'),(5,'501'),(330,'500'),(100,'AcDbXrecord'),(280,1),(1,'extension payload'),(90,1701),(310,bytes([0,1,255,0]))]]
   for r in rs:
    for i,t in enumerate(r):
     if t==(9,'$HANDSEED'):r[i+1]=(5,'503')
   path=ROOT/f'independent-lightlist-R{year}-{transport}.dxf';output(path,rs,version,binary)
   doc=ezdxf.readfile(path);a=doc.audit();assert not a.errors and not a.fixes,(path,[(x.code,x.message) for x in a.errors+a.fixes])
   manifest['fixtures'].append({'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'original_producer_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'year':year,'version':version,'binary':binary,'dictionary':'22','lights':['1D','1E'],'line':'1F','lists':entries})
   print('PASS',path.name,'five LIGHTLIST packets; explicit versions; zero audit changes')
 (ROOT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
if __name__=='__main__':main()
