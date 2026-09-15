from pathlib import Path
import io,json,hashlib,copy
from ezdxf.lldxf.tagger import ascii_tags_loader,tag_compiler
from ezdxf.lldxf.tagwriter import TagWriter,BinaryTagWriter
from ezdxf.lldxf.types import dxftag
root=Path(__file__).resolve().parent
entries=[]
for year in (2000,2004,2007,2010,2013,2018):
 source=(root.parent/'fixtures'/f'polyface-{year}-quad-ascii.dxf').read_text()
 rows=[];row=[]
 for tag in tag_compiler(ascii_tags_loader(io.StringIO(source,newline=None))):
  if tag.code==0:
   if row:rows.append(row)
   row=[]
  value=tag.value
  if not isinstance(value,(str,int,float,bytes)):value=tuple(value)
  row.append((tag.code,value))
 if row:rows.append(row)
 scenarios=['private-before','private-after','private-nested','unknown-subclass','public-resumes','xdata-boundary','xdata-control']
 for name in scenarios:
  changed=copy.deepcopy(rows);face=next(r for r in changed if r[0]==(0,'VERTEX') and (70,128) in r)
  block=[(102,'{PRIVATE'),(70,192),(71,32767),(102,'}')]
  if name=='private-before':
   i=face.index((100,'AcDbFaceRecord'));face[i:i]=block
  elif name=='private-after':face.extend(block)
  elif name=='private-nested':face.extend([(102,'{OUTER'),(71,32767),(102,'{INNER'),(72,-32768),(102,'}'),(70,192),(102,'}')])
  elif name=='unknown-subclass':face.extend([(100,'PrivateFaceData'),(70,192),(71,32767)])
  elif name=='public-resumes':
   face[:]=[t for t in face if t[0] not in (71,72,73,74)]
   face.extend([(71,1),(100,'PrivateFaceData'),(71,32767),(100,'AcDbFaceRecord'),(72,-2),(73,3),(74,-4)])
  elif name=='xdata-boundary':face.extend([(1001,'ACAD'),(1000,'opaque'),(71,32767),(70,192)])
  else:face.extend([(1001,'ACAD'),(1000,'opaque'),(1071,32767)])
  for binary in (False,True):
   output=io.BytesIO() if binary else io.StringIO(newline='\n');writer=BinaryTagWriter(output) if binary else TagWriter(output)
   if binary:writer.write_signature()
   for row in changed:
    for c,v in row:writer.write_tag(dxftag(c,v))
   data=output.getvalue();data=data if binary else data.encode('utf8')
   filename=f'polyface-boundary-{year}-{name}-'+('binary' if binary else 'ascii')+'.dxf'
   (root/'fixtures'/filename).write_bytes(data)
   entries.append(dict(name=filename,year=year,binary=binary,scenario=name,reject=False,expected=[1,-2,3,-4],sha256=hashlib.sha256(data).hexdigest()))
(root/'manifest.json').write_text(json.dumps(dict(scope='Synthetic private/public boundary probes; private payload preservation remains outside the assertion',cases=entries),indent=2)+'\n')
print('Generated',len(entries),'boundary cases')
