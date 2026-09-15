#!/usr/bin/env python3
from pathlib import Path
import gzip,io,re,json,hashlib,collections
from ezdxf.lldxf.tagger import ascii_tags_loader,binary_tags_loader
import argparse
parser=argparse.ArgumentParser(description='Inventory unsupported standalone entity records in the supplied DXF corpus.')
parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[1])
parser.add_argument('--output', type=Path, required=True)
args=parser.parse_args()
repo=args.repository.resolve()
source=(repo/'netDxf/IO/DxfReader.cs').read_text(); dispatch=source[source.index('private DxfObject ReadEntity('):source.index('private Wipeout ReadWipeout(')]
constants=dict(re.findall(r'const string (\w+)\s*=\s*"([^"]+)"',(repo/'netDxf/DxfObjectCode.cs').read_text()))
known={constants[x] for x in re.findall(r'case DxfObjectCode\.(\w+):',dispatch)}|set(re.findall(r'case "([^"]+)":',dispatch))
files=list((repo/'tests/fixtures').rglob('*.dxf.gz'))+list((repo/'tests/fixtures').rglob('*.dxf'))+list((repo/'TestDxfDocument').glob('*.dxf'))
files=sorted(files)
found=[];errors=[];manifest=[];counts=collections.Counter()
for path in files:
 entry={'file':str(path.relative_to(repo)), 'storedSha256':hashlib.sha256(path.read_bytes()).hexdigest()}
 manifest.append(entry)
 try:
  data=gzip.decompress(path.read_bytes()) if path.suffix=='.gz' else path.read_bytes(); tags=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('latin1'), newline=None))
  entry['inflatedSha256']=hashlib.sha256(data).hexdigest()
  entry['transport']='binary' if data.startswith(b'AutoCAD Binary DXF') else 'ascii'
  rows=[];row=[]
  for tag in tags:
   if tag.code==0:
    if row:rows.append(row)
    row=[]
   value=tag.value
   if isinstance(value,bytes):value=value.hex()
   row.append([tag.code,value])
  if row:rows.append(row)
  section=None;version=None
  for row in rows:
   if row[0]==[0,'SECTION']:section=next((v for c,v in row if c==2),None)
   if row[0]==[0,'ENDSEC']:section=None
   if section=='HEADER':
    for i,t in enumerate(row[:-1]):
     if t==[9,'$ACADVER']:version=row[i+1][1]
   if section in ('ENTITIES','BLOCKS') and row[0][1] not in known|{'SECTION','ENDSEC','BLOCK','ENDBLK'}:
    counts[row[0][1]]+=1
    if row[0][1] not in {'VERTEX','ATTRIB','SEQEND'}:
     found.append({'file':str(path.relative_to(repo)),'fileSha256':hashlib.sha256(data).hexdigest(),'version':version,'section':section,'type':row[0][1],'handle':next((v for c,v in row if c==5),None),'subclasses':[v for c,v in row if c==100],'tagCount':len(row),'codes':dict(collections.Counter(str(c) for c,v in row)),'tags':row})
  entry['sourceVersion']=version
  entry['recordCount']=len(rows)
 except Exception as e:errors.append({'file':str(path.relative_to(repo)),'error':str(e)})
result={'purpose':'Source inventory only; no native unknown-entity qualification is claimed.', 'sourceFiles':manifest, 'scannedFiles':len(files),'knownDispatch':sorted(known),'typeCounts':dict(counts),'unknownStandaloneCandidates':found,'errors':errors}
args.output.write_text(json.dumps(result,indent=2))
print(json.dumps({'scannedFiles':len(files),'typeCounts':dict(counts),'standaloneCandidates':len(found),'errors':errors},indent=2))
