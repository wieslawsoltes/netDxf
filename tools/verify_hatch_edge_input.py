#!/usr/bin/env python3
"""Check the exact boundary packet produced from single-pass HATCH edge inputs."""
import io
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader,binary_tags_loader
PROFILES=dict(zip(('AutoCad2000','AutoCad2004','AutoCad2007','AutoCad2010','AutoCad2013','AutoCad2018'),('AC1015','AC1018','AC1021','AC1024','AC1027','AC1032')))
def require(test,message):
    if not test:raise ValueError(message)
def boundary(data):
    load=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'),newline=None))
    packets=[];hatch=False;active=False;current=[]
    for tag in load:
        if tag.code==0:hatch=tag.value=='HATCH'
        if not hatch:continue
        if tag.code==91:active=True
        if active and tag.code==75:packets.append(current);current=[];active=False
        if active:current.append((tag.code,tag.value))
    require(len(packets)==1,'Expected one HATCH boundary packet')
    return [(code,float(value)) for code,value in packets[0]]
def expected(kind):
    points=[(0,0),(4,0),(4,3),(0,3)]
    if kind==1:
        tags=[(91,1),(92,7),(72,1),(73,1),(93,4)]
        for x,y in points:tags.extend(((10,x),(20,y),(42,0)))
    else:
        if kind==3:points=points[:3]
        tags=[(91,1),(92,5),(93,len(points))]
        for i,(x,y) in enumerate(points):
            a,b=points[(i+1)%len(points)];tags.extend(((72,1),(10,x),(20,y),(11,a),(21,b)))
    return tags+[(97,0)]
def check(actual,wanted):require(actual==wanted,'Boundary fields/order/classification differ')
def main(directory):
    directory=Path(directory);names={f'hatch-edge-input-{k}-{v}-{b}.dxf' for k in (1,2,3) for v in PROFILES for b in (False,True)}
    require({p.name for p in directory.glob('hatch-edge-input-*.dxf')}==names,'Fixture inventory differs');mutations=0
    for k in (1,2,3):
        for version,profile in PROFILES.items():
            for binary in (False,True):
                path=directory/f'hatch-edge-input-{k}-{version}-{binary}.dxf';data=path.read_bytes();require(data.startswith(b'AutoCAD Binary DXF')==binary,'Transport differs')
                tags=boundary(data);wanted=expected(k);check(tags,wanted)
                for i,(code,value) in enumerate(tags):
                    for op in ('change','drop','duplicate'):
                        changed=list(tags)
                        if op=='change':changed[i]=(code,value+1)
                        elif op=='drop':del changed[i]
                        else:changed.insert(i,changed[i])
                        try:check(changed,wanted)
                        except ValueError:mutations+=1
                        else:raise AssertionError('Boundary corruption escaped validator')
                doc=ezdxf.readfile(path);require(doc.dxfversion==profile,'Version differs');audit=doc.audit();require(not audit.errors and not audit.fixes,'Graph repairs required')
    print(f'PASS: {len(names)} single-pass HATCH drawings; {mutations} ordered-boundary corruptions rejected; zero graph errors/repairs')
if __name__=='__main__':main(sys.argv[1])
