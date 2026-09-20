#!/usr/bin/env python3
"""Complete raw reversal packet checks plus independently reversed arc/width samples."""
from pathlib import Path
import itertools
import math
import sys
import ezdxf
from verify_raw_lwpolyline_topology import load_tags
from verify_raw_line_geometry import PROFILES, key, require, reject, audit_signature
from verify_polyline_reverse_atomic import sample

VERSIONS={k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13')}
FIELDS=(10,20,40,41,42,91)

def selected(tags):
    starts=[i for i,t in enumerate(tags) if t==(0,'LWPOLYLINE')];require(len(starts)==1,'Polyline inventory')
    a=starts[0];b=next(i for i in range(a+1,len(tags)) if tags[i][0]==0);return a,b

def check(before,after,variant):
    a,b=selected(before);c,d=selected(after);require(a==c,'Record moved')
    require(list(map(key,before[:a]))==list(map(key,after[:c])),'Prefix changed')
    require(list(map(key,before[b:]))==list(map(key,after[d:])),'Suffix changed')
    source,result=before[a:b],after[c:d]
    starts=[i for i,(c,_) in enumerate(source) if c==10];require(len(starts)==3,'Input vertex count')
    vertices=[]
    for i,start in enumerate(starts):
        stop=starts[i+1] if i<2 else len(source)
        fields={c:v for c,v in source[start:stop] if c in FIELDS}
        expected={10:1.+i*3,20:2.+i*3,91:-7 if i==1 else 100+i}
        if variant!=1:expected.update({40:.25+i*.25,41:.5+i*.25,42:-.5 if i==1 else 0.})
        require(fields==expected,'Regenerated input differs');vertices.append(fields)
    packets=[]
    for j in range(3):
        p=vertices[2-j];edge=vertices[(1-j)%3];out=[(10,p[10]),(20,p[20])]
        if 91 in p:out.append((91,p[91]))
        if 41 in edge:out.append((40,edge[41]))
        if 40 in edge:out.append((41,edge[40]))
        if 42 in edge:out.append((42,-edge[42]))
        packets.append(out)
    expected=[];j=0
    for code,value in source:
        if code==10:expected.extend(packets[j]);j+=1
        elif code not in FIELDS:expected.append((code,value))
    require(list(map(key,result))==list(map(key,expected)),'Reversal changed packet, presence or unselected data')
    return c,d

def main(directory):
    specs=list(itertools.product(VERSIONS,(False,True),(False,True),range(4),(False,True)))
    stems=[f'raw-lw-reverse-{v}-{bi}-{bl}-{var}-{out}' for v,bi,bl,var,out in specs]
    names={s+'-'+side+'.dxf' for s in stems for side in ('before','after')}
    def inventory(actual):require(actual==names,'Missing or extra raw reversal fixtures')
    inventory({p.name for p in directory.glob('raw-lw-reverse-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'raw-lw-reverse-extra.dxf'}))
    controls=samples=0
    for stem,(version,_,_,variant,binary) in zip(stems,specs):
        paths=[directory/(stem+'-'+side+'.dxf') for side in ('before','after')];before,after=map(load_tags,paths)
        for path,tags in zip(paths,(before,after)):
            require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
            at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Declared version changed')
        a,b=check(before,after,variant)
        for i in range(a+1,b):
            code,value=after[i]
            for action in ('change','delete','duplicate'):
                bad=list(after)
                if action=='change':bad[i]=(code,value+.125 if isinstance(value,(int,float)) else str(value)+'_bad')
                elif action=='delete':del bad[i]
                else:bad.insert(i,bad[i])
                controls+=reject(lambda:check(before,bad,variant))
        docs=[ezdxf.readfile(p) for p in paths]
        for doc in docs:require(not any(any(c.values()) for c in audit_signature(doc)),'Graph errors or repairs')
        old,new=[doc.entitydb['A'] for doc in docs];oldpts=[tuple(p) for p in old.get_points('xyseb')];newpts=[tuple(p) for p in new.get_points('xyseb')]
        require(old.dxf.flags==new.dxf.flags and len(newpts)==3,'Independent topology differs')
        constant=old.dxf.get('const_width',0.)
        for j in range(3 if old.closed else 2):
            e=(1-j)%3
            for k in range(17):
                t=k/16;x=sample(newpts[j][:2],newpts[(j+1)%3][:2],newpts[j][4],t)
                y=sample(oldpts[e][:2],oldpts[(e+1)%3][:2],oldpts[e][4],1-t)
                wx=new.ocs().to_wcs((*x,new.dxf.elevation));wy=old.ocs().to_wcs((*y,old.dxf.elevation))
                require(all(math.isclose(a,b,rel_tol=2e-12,abs_tol=2e-12) for a,b in zip(wx,wy)),'Independent WCS arc differs')
                a=constant if constant else (1-t)*newpts[j][2]+t*newpts[j][3]
                b=constant if constant else t*oldpts[e][2]+(1-t)*oldpts[e][3]
                require(math.isclose(a,b,abs_tol=1e-14),'Reversed width progression differs');samples+=1
    print(f'PASS: {len(specs)} complete source/edit pairs / {len(specs)*2} drawings; {samples} independent WCS arc/width samples; {controls} actual-tag corruptions and two inventory controls rejected; zero graph errors/repairs.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_raw_lwpolyline_reverse.py ARTIFACTS');main(Path(sys.argv[1]))
