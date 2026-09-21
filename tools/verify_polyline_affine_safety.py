#!/usr/bin/env python3
"""Check affine polyline WCS loci, extrusion, widths and cache packets independently."""
from pathlib import Path
import math
import sys
import itertools
import ezdxf
from verify_raw_line_geometry import PROFILES, require, reject, audit_signature
from verify_raw_lwpolyline_topology import load_tags
VERSIONS={k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13','AutoCad14')}
POINTS=((1.,2.),(4.,5.),(7.,1.))

def sample(a,b,bulge,t):
    if bulge==0:
        return ((1-t)*a[0]+t*b[0],(1-t)*a[1]+t*b[1])
    dx,dy=b[0]-a[0],b[1]-a[1]
    f=(1-bulge*bulge)/(4*bulge)
    c=((a[0]+b[0])/2-dy*f,(a[1]+b[1])/2+dx*f)
    x,y=a[0]-c[0],a[1]-c[1];r=4*math.atan(bulge)*t
    return (c[0]+x*math.cos(r)-y*math.sin(r),c[1]+x*math.sin(r)+y*math.cos(r))

def transform(p,plane,mode):
    x,y,z=p
    if not plane: q=(2*x,2*y,4*z)
    elif mode==0:q=(x,y,.5*x+z)
    elif mode==1:q=(x+2*z,y,z)
    elif mode==2:q=(-x,y,z)
    else:q=(x+.5*y,y,z)
    return (q[0]+10,q[1]+20,q[2]+30)

def packet_check(tags,plane,mode):
    starts=[i for i,t in enumerate(tags) if t==(0,'LWPOLYLINE')];require(len(starts)==1,'Polyline count')
    a=starts[0];b=next(i for i in range(a+1,len(tags)) if tags[i][0]==0);r=tags[a:b]
    require([v for c,v in r if c==90]==[3],'Vertex count')
    require([v for c,v in r if c==70]==[129],'Flags')
    require(not any(c in (92,160,310) for c,v in r),'Stale proxy')
    require([v for c,v in r if c==39]==([-8.] if not plane else [0.]),'Signed thickness')
    expected=[.5,0.,0.] if (mode==2 if plane else mode==1) else [0.,0.,0.]
    require([v for c,v in r if c==42]==expected,'Stored bulges')
    return a,b

def main(directory):
    specs=[(v,b,p,m) for v in VERSIONS for b in (False,True) for p in (False,True) for m in range(4 if p else 3)]
    def name(v,b,p,m):return f'polyline-affine-safety-{ "plane-" if p else ""}{v}-{b}-{m}.dxf'
    names={name(*s) for s in specs}
    def inventory(actual):require(actual==names,'Missing/extra affine fixtures')
    inventory({p.name for p in directory.glob('polyline-affine-safety-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'polyline-affine-safety-extra.dxf'}))
    count=controls=0
    for version,binary,plane,mode in specs:
        path=directory/name(version,binary,plane,mode);tags=load_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport')
        at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Version')
        a,b=packet_check(tags,plane,mode)
        for code in (39,42,70,90):
            at=next(i for i in range(a,b) if tags[i][0]==code)
            damaged=list(tags);damaged[at]=(code,tags[at][1]+1)
            for variant in (damaged,tags[:at]+tags[at+1:],tags[:at]+[tags[at]]+tags[at:]):controls+=reject(lambda:packet_check(variant,plane,mode))
        controls+=reject(lambda:packet_check(tags[:b]+[(92,1),(310,b'X')]+tags[b:],plane,mode))
        doc=ezdxf.readfile(path);require(not any(any(c.values()) for c in audit_signature(doc)),'Graph diagnostics')
        entity=list(doc.modelspace().query('LWPOLYLINE'))[0];pts=[tuple(p) for p in entity.get_points('xyseb')]
        require(entity.dxf.elevation==42 if not plane else math.isfinite(entity.dxf.elevation),'Elevation')
        if not plane and mode==2:
            require(entity.dxf.const_width==4 and pts[1][2:4]==(6.,8.),'Width scaling')
        for edge in range(3):
            for j in range(17):
                t=j/16;bulge=.5 if edge==0 and (mode==2 if plane else mode==1) else 0
                src=sample(POINTS[edge],POINTS[(edge+1)%3],bulge,t)
                expected=transform(src+(3.,),plane,mode)
                q=sample(pts[edge][:2],pts[(edge+1)%3][:2],pts[edge][4],t)
                actual=entity.ocs().to_wcs(q+(entity.dxf.elevation,))
                require(all(math.isclose(expected[i],actual[i],abs_tol=2e-12,rel_tol=2e-12) for i in range(3)),'Affine centerline sample')
                count+=1
        extrusion=entity.ocs().uz*entity.dxf.thickness
        require(tuple(extrusion)==((0.,0.,-8.) if not plane else (0.,0.,0.)),'Extrusion vector')
    print(f'PASS: {len(specs)} drawings / {count} independent affine locus samples; {controls} actual-packet mutations and 2 inventory controls rejected; zero graph errors or repairs.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_polyline_affine_safety.py ARTIFACTS');main(Path(sys.argv[1]))
