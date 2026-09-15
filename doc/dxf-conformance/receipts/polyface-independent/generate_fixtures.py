from pathlib import Path
import io,json,hashlib
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader,tag_compiler
from ezdxf.lldxf.tagwriter import TagWriter,BinaryTagWriter
from ezdxf.lldxf.types import dxftag
root=Path(__file__).resolve().parent
cases=[
 ('quad',[(71,1),(72,-2),(73,3),(74,-4)],[1,-2,3,-4],False,''),
 ('triangle-zero',[(71,1),(72,-2),(73,3),(74,0)],[1,-2,3],False,''),
 ('line-face',[(71,1),(72,-2),(73,0),(74,0)],[1,-2],False,''),
 ('point-face',[(71,2),(72,0),(73,0),(74,0)],[2],False,''),
 ('out-of-range',[(71,1),(72,2),(73,5),(74,0)],[],True,''),
 ('signed-minimum',[(71,-32768),(72,2),(73,3),(74,0)],[],True,''),
 ('bad-first',[(71,32767),(72,0),(73,1),(74,2)],[],True,''),
 ('trailing-after-zero',[(71,1),(72,-2),(73,0),(74,32767)],[1,-2],False,''),
 ('missing-middle',[(71,1),(73,3),(74,4)],[1],False,''),
 ('reordered-slots',[(74,0),(72,-2),(71,1),(73,3)],[1,-2,3],False,''),
 ('duplicate-slot',[(71,1),(71,2),(72,3)],[],True,''),
 ('zero-first',[(71,0),(72,1),(73,2),(74,3)],[],True,''),
 ('advisory-counts',[(71,1),(72,-2),(73,3),(74,-4)],[1,-2,3,-4],False,'counts'),
 ('face-before-coordinates',[(71,1),(72,-2),(73,3),(74,-4)],[1,-2,3,-4],False,'first'),
 ('interleaved-face',[(71,1),(72,-2),(73,3),(74,-4)],[1,-2,3,-4],False,'middle'),
]
entries=[]
for year in (2000,2004,2007,2010,2013,2018):
 doc=ezdxf.new('R'+str(year));msp=doc.modelspace()
 poly=msp.add_polyface();poly.append_faces([[(0,0,0),(3,0,0),(3,4,0),(0,4,0)]])
 msp.add_line((71,72,73),(81,82,83))
 doc.header['$TDCREATE']=doc.header['$TDUPDATE']=2451545.0
 stream=io.StringIO();doc.write(stream)
 rows=[];row=[]
 for tag in tag_compiler(ascii_tags_loader(io.StringIO(stream.getvalue(),newline=None))):
  if tag.code==0:
   if row:rows.append(row)
   row=[]
  v=tag.value
  if not isinstance(v,(str,int,float,bytes)):v=tuple(v)
  row.append((tag.code,v))
 if row:rows.append(row)
 import copy
 for name,slots,expected,reject,variant in cases:
  changed=copy.deepcopy(rows)
  parent=next(r for r in changed if r[0]==(0,'POLYLINE'))
  face=next(r for r in changed if r[0]==(0,'VERTEX') and (70,128) in r)
  face[:]=[t for t in face if t[0] not in (71,72,73,74)]+slots
  vertices=[r for r in changed if r[0]==(0,'VERTEX') and (70,192) in r]
  if variant=='counts':
   parent[:]=[t for t in parent if t[0] not in (71,72)]+[(71,-30000),(72,32767)]
  elif variant in ('first','middle'):
   changed.remove(face);index=changed.index(vertices[0])+(1 if variant=='middle' else 0);changed.insert(index,face)
  for binary in (False,True):
   output=io.BytesIO() if binary else io.StringIO(newline='\n')
   writer=BinaryTagWriter(output,dxfversion=doc.dxfversion) if binary else TagWriter(output,dxfversion=doc.dxfversion)
   if binary:writer.write_signature()
   for row in changed:
    for c,v in row:writer.write_tag(dxftag(c,v))
   data=output.getvalue();data=data if binary else data.encode('utf8')
   filename=f'polyface-{year}-{name}-'+('binary' if binary else 'ascii')+'.dxf'
   (root/'fixtures'/filename).write_bytes(data)
   entries.append(dict(name=filename,year=year,binary=binary,scenario=name,reject=reject,expected=expected,sha256=hashlib.sha256(data).hexdigest()))
(root/'manifest.json').write_text(json.dumps(dict(producer='ezdxf '+ezdxf.__version__,scope='Independent authored packets and disclosed mutations; no native application evidence',cases=entries),indent=2)+'\n')
print('Generated',len(entries),'independent cases')
