#!/usr/bin/env python3
"""Independent reversed segment attributes and stale-proxy checks."""
from pathlib import Path
import itertools
import math
import sys
import ezdxf
from ezdxf.math import bulge_to_arc, Vec2
from verify_raw_lwpolyline_topology import load_tags
from verify_raw_line_geometry import PROFILES, require, reject, audit_signature, key

VERSIONS = {k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13','AutoCad14')}
POINTS = ((-3.,0.),(2.,4.),(7.,-1.),(8.,3.))
BULGES = (0.,.5,-.25,-.75)

def sample(p,q,b,t):
    if b == 0:
        return tuple((1-t)*p[i]+t*q[i] for i in range(2))
    center,_,_,radius = bulge_to_arc(Vec2(p),Vec2(q),b)
    angle = math.atan2(p[1]-center.y,p[0]-center.x)+4*math.atan(b)*t
    return center.x+radius*math.cos(angle),center.y+radius*math.sin(angle)

def check(tags,closed):
    starts=[i for i,t in enumerate(tags) if t==(0,'LWPOLYLINE')]
    require(len(starts)==1,'Expected one lightweight polyline')
    a=starts[0];b=next(i for i in range(a+1,len(tags)) if tags[i][0]==0)
    rec=tags[a:b]
    require(not any(c in (92,160,310) for c,v in rec),'Stale proxy survived reversal')
    for code,value in ((90,4),(70,int(closed)),(38,5.),(39,-2.),(210,0.),(220,0.),(230,-1.)):
        require([v for c,v in rec if c==code]==[value],f'Header {code} differs')
    positions=[i for i,(c,v) in enumerate(rec) if c==10]
    require(len(positions)==4,'Vertex inventory changed')
    for j,start in enumerate(positions):
        stop=positions[j+1] if j<3 else len(rec)
        packet=rec[start:stop]
        p=3-j;e=(p-1)%4
        fields={10:[POINTS[p][0]],20:[POINTS[p][1]],42:[-BULGES[e]],
                40:[] if e%2==0 else [.5+e*.25],41:[.25+e*.25] if e%2==0 else []}
        for code,wanted in fields.items():
            require([key((code,float(v))) for c,v in packet if c==code]==[key((code,v)) for v in wanted],f'Reversed vertex {j} field {code} differs')
    return a,b

def main(directory):
    specs=list(itertools.product(VERSIONS,(False,True),(False,True)))
    names={f'polyline-reverse-atomic-{v}-{bi}-{cl}.dxf' for v,bi,cl in specs}
    def inventory(actual):require(actual==names,'Missing or extra reversal fixtures')
    inventory({p.name for p in directory.glob('polyline-reverse-atomic-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'polyline-reverse-atomic-extra.dxf'}))
    controls=samples=0
    for version,binary,closed in specs:
        path=directory/f'polyline-reverse-atomic-{version}-{binary}-{closed}.dxf';tags=load_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport differs')
        at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Version differs')
        a,b=check(tags,closed)
        for i in range(a+1,b):
            code,value=tags[i]
            if code not in (10,20,40,41,42,70,90):continue
            changed=list(tags);changed[i]=(code,value+.125)
            for mutation in (changed,tags[:i]+tags[i+1:],tags[:i]+[tags[i]]+tags[i:]):
                controls+=reject(lambda:check(mutation,closed))
        controls+=reject(lambda:check(tags[:b]+[(92,4),(310,b'abcd')]+tags[b:],closed))
        doc=ezdxf.readfile(path);require(not any(any(c.values()) for c in audit_signature(doc)),'Graph errors or repairs')
        entities=list(doc.modelspace().query('LWPOLYLINE'));require(len(entities)==1,'Independent inventory differs')
        p=entities[0];points=[tuple(v) for v in p.get_points('xyseb')]
        require(p.proxy_graphic is None,'Independent reader retained proxy')
        for j in range(4 if closed else 3):
            old=(2-j)%4;next_j=(j+1)%4
            for k in range(17):
                t=k/16
                actual=sample(points[j][:2],points[next_j][:2],points[j][4],t)
                expected=sample(POINTS[old],POINTS[(old+1)%4],BULGES[old],1-t)
                require(all(math.isclose(x,y,rel_tol=2e-12,abs_tol=2e-12) for x,y in zip(actual,expected)),'Reversed arc locus differs')
                sw=.25+old*.25 if old%2==0 else 0.;ew=.5+old*.25 if old%2 else 0.
                require(math.isclose((1-t)*points[j][2]+t*points[j][3],t*sw+(1-t)*ew,abs_tol=1e-14),'Width progression differs')
                samples+=1
    print(f'PASS: {len(specs)} drawings / {samples} independent reversed arc and width samples; {controls} packet corruptions and two inventory controls rejected; zero graph errors/repairs.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_polyline_reverse_atomic.py ARTIFACTS');main(Path(sys.argv[1]))
