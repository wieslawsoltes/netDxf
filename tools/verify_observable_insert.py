#!/usr/bin/env python3
"""Verify actual HATCH Insert packets, preserved source occurrences and callback observations."""
from pathlib import Path
import argparse,copy,json,hashlib,io
import ezdxf
from verify_hatch_source_relations import wire_records,paths,first,owner,check,canonical,VERSIONS
ROOT=Path(__file__).resolve().parents[1]

def validate(data,observed,source,index):
    wire=wire_records(data);before=wire_records(source);hatch=wire['3A2'];original=paths(before['3A2']);actual=paths(hatch)
    check(len(actual)==len(original)+1,'Inserted path count');retained=actual[:index]+actual[index+1:]
    check(canonical(retained)==canonical(original),'Insertion changed original path geometry/source occurrences/order')
    check(canonical(paths(wire['3A3']))==canonical(paths(before['3A3'])),'Other HATCH source paths changed')
    new=actual[index];check(new['source_count']==1,'New path source count');target=new['sources'][0];check(target not in before,'New source identity reused')
    check(wire[target][0]==(0,'LWPOLYLINE') and owner(wire[target])==owner(hatch),'Inserted source type/owner')
    check(new['geometry'][0][1]&2 and new['geometry'][2]==(73,1),'Inserted closed polyline representation')
    for name in {s for p in original for s in p['sources']}:
        check(name in wire and owner(wire[name])==owner(before[name]),'Original source owner/identity changed')
        for code in (10,40,41,42,70,90):check(canonical([v for c,v in wire[name] if c==code and (code not in (40,41,42) or v!=0)])==canonical([v for c,v in before[name] if c==code and (code not in (40,41,42) or v!=0)]),'Original source geometry changed')
    check(observed['index']==index and observed['exception'] is None,'Insert exception/index')
    check(observed['additions']==1 and observed['removals']==0,'Insert emitted removal callbacks')
    check(observed['paths']==[p['sources'] for p in actual],'Observed path source sequence differs from actual output')
    for item in observed['sources']:check(item['expected_reactors']==item['actual_reactors'],'Existing source reactor occurrence count/order changed')

def corrupt_wire(data,kind):
    wire=wire_records(data);boundaries=paths(wire['3A2']);retained=boundaries[0]['sources'][0];inserted=boundaries[1]['sources'][0]
    lines=data.decode().splitlines();tags=[(int(lines[i]),lines[i+1].strip()) for i in range(0,len(lines),2)]
    def packet(handle):
        start=next(i for i,t in enumerate(tags) if t==(5,handle));begin=start
        while tags[begin][0]!=0:begin-=1
        end=next((i for i in range(start+1,len(tags)) if tags[i][0]==0),len(tags));return begin,end
    if kind=='retained-source':
        begin,end=packet('3A2');start=next(i for i in range(begin,end) if tags[i]==(100,'AcDbHatch'));at=next(i for i in range(start,end) if tags[i][0]==330)
        alternatives=[h for h,t in wire.items() if t[0]==(0,'LWPOLYLINE') and h not in (tags[at][1],inserted)];check(alternatives,'Corruption target inventory');tags[at]=(330,alternatives[0])
    elif kind=='source-geometry':
        begin,end=packet(retained);at=next(i for i in range(begin,end) if tags[i][0]==10);tags[at]=(10,repr(float(tags[at][1])+.25))
    else:
        begin,end=packet(inserted);depth=0;at=None
        for i in range(begin,end):
            if tags[i][0]==102:depth+=1 if tags[i][1].startswith('{') else -1
            elif tags[i][0]==330 and depth==0:at=i;break
        check(at is not None,'Inserted source common owner');alternate=next(h for h,t in wire.items() if t[0]==(0,'BLOCK_RECORD') and h!=tags[at][1]);tags[at]=(330,alternate)
    return ''.join(f'{c}\n{v}\n' for c,v in tags).encode()

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('artifacts',type=Path);args=p.parse_args();fixture=ROOT/'tests/fixtures/hatch-source-relations';manifest=json.loads((fixture/'manifest.json').read_text());pins={x['file']:x['sha256'] for x in manifest['fixtures']};outputs=0;sample=None
    for year,profile in VERSIONS.items():
        for binary in (False,True):
            sourcefile=fixture/f'producer-R{year}-{"binary" if binary else "ascii"}.dxf';source=sourcefile.read_bytes();check(hashlib.sha256(source).hexdigest()==pins[sourcefile.name],'Actual producer source hash')
            for index in (0,1,2):
                stem=f'observable-insert-AutoCad{year}-{binary}-{index}';file=args.artifacts/(stem+'.dxf');data=file.read_bytes();observed=json.loads((args.artifacts/(stem+'.json')).read_text());validate(data,observed,source,index)
                check(data.startswith(b'AutoCAD Binary DXF')==binary,'Output transport');doc=ezdxf.readfile(file);check(doc.dxfversion==profile,'Output profile');audit=doc.audit();check(not audit.errors and not audit.fixes,'Independent audit changed actual output');outputs+=1
                if year==2018 and not binary and index==1:sample=(data,observed,source,index)
    corrupt=copy.deepcopy(sample[1]);corrupt['sources'][0]['actual_reactors']=[]
    try:validate(sample[0],corrupt,sample[2],sample[3])
    except ValueError:pass
    else:raise ValueError('Accepted corrupted actual reactor observation')
    wire_controls=0
    for kind in ('retained-source','source-geometry','inserted-owner'):
        changed=corrupt_wire(sample[0],kind)
        wire_records(changed);ezdxf.read(io.StringIO(changed.decode())) # Actual modified wire remains parseable.
        try:validate(changed,sample[1],sample[2],sample[3])
        except ValueError:wire_controls+=1
        else:raise ValueError('Accepted actual DXF corruption '+kind)
    check(outputs==36 and wire_controls==3,'Mandatory Insert outputs/controls');print(json.dumps({'outputs':outputs,'actual_callback_and_reactor_observations':outputs,'corruption_controls':4,'actual_wire_corruption_controls':wire_controls,'parseable_corrupted_drawings':wire_controls,'observation_corruption_controls':1,'audit_errors':0,'audit_repairs':0,'native_execution':False,'source':'Pinned independent producer fixtures plus explicitly authored inserted rectangle'}))
if __name__=='__main__':main()
