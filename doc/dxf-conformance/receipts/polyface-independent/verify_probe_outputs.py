import argparse,copy,io,json
from pathlib import Path
from ezdxf.lldxf.tagger import ascii_tags_loader,binary_tags_loader,tag_compiler

def records(data):
 loader=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf8'),newline=None))
 result=[];row=[]
 for tag in tag_compiler(loader):
  if tag.code==0:
   if row:result.append(row)
   row=[]
  value=tag.value
  if not isinstance(value,(str,int,float,bytes)):value=tuple(value)
  row.append((tag.code,value))
 if row:result.append(row)
 return result

def check(condition,message):
 if not condition:raise ValueError(message)
def first(row,code):return next(v for c,v in row if c==code)
def validate(rows,expected):
 parents=[r for r in rows if r[0]==(0,'POLYLINE')];check(len(parents)==1,'Missing/extra POLYLINE')
 check(first(parents[0],70)&64,'Lost polyface flag')
 vertices=[r for r in rows if r[0]==(0,'VERTEX') and first(r,70)&192==192]
 faces=[r for r in rows if r[0]==(0,'VERTEX') and first(r,70)&192==128]
 check(len(vertices)==4 and len(faces)==1,'Changed physical child topology')
 check([first(r,10) for r in vertices]==[(0,0,0),(3,0,0),(3,4,0),(0,4,0)],'Changed ordered coordinates')
 check([t for t in faces[0] if 71<=t[0]<=74]==[(71+i,value) for i,value in enumerate(expected)],'Changed signed indices/canonical active prefix')
 check(sum(r[0]==(0,'SEQEND') for r in rows)==1,'Missing/extra SEQEND')
 lines=[r for r in rows if r[0]==(0,'LINE')];check(len(lines)==1,'Missing/extra following LINE')
 check(first(lines[0],10)==(71,72,73) and first(lines[0],11)==(81,82,83),'Changed following LINE')
 return parents[0],vertices,faces[0],lines[0]

def main():
 parser=argparse.ArgumentParser();parser.add_argument('corpus',type=Path);parser.add_argument('outputs',type=Path);args=parser.parse_args()
 entries=[e for e in json.loads((args.corpus/'manifest.json').read_text())['cases'] if not e['reject']]
 count=0
 for entry in entries:
  data=(args.outputs/entry['name']).read_bytes();check(data.startswith(b'AutoCAD Binary DXF')==entry['binary'],'Transport differs')
  rows=records(data);validate(rows,entry['expected']);count+=1
 controls=[]
 for name in ['index','coordinate','missing-vertex','following-line','polyline-kind','missing-seqend']:
  damaged=copy.deepcopy(rows);parent,vertices,face,line=validate(damaged,entry['expected'])
  if name=='index':face[face.index(next(t for t in face if t[0]==71))]=(71,2)
  elif name=='coordinate':vertices[0][vertices[0].index(next(t for t in vertices[0] if t[0]==10))]=(10,(999,0,0))
  elif name=='missing-vertex':damaged.remove(vertices[0])
  elif name=='following-line':line[line.index(next(t for t in line if t[0]==11))]=(11,(0,0,0))
  elif name=='polyline-kind':parent[parent.index(next(t for t in parent if t[0]==70))]=(70,16)
  else:damaged.remove(next(r for r in damaged if r[0]==(0,'SEQEND')))
  try:validate(damaged,entry['expected'])
  except ValueError:controls.append(name)
  else:raise ValueError('Undetected output corruption: '+name)
 print(json.dumps({'checkedOutputs':count,'negativeControls':controls,'nativeApplicationValidation':False},indent=2))
if __name__=='__main__':main()
