#!/usr/bin/env python3
"""Independently check vertex-packet edits and preserved raw LWPOLYLINE structure."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, key, require, reject, audit_signature

VERSIONS={k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13')}
FIELDS=(10,20,40,41,42)

def record_range(tags):
    a=[i for i,t in enumerate(tags) if t==(0,'LWPOLYLINE')]
    require(len(a)==1,'Expected one physical polyline')
    b=next(i for i in range(a[0]+1,len(tags)) if tags[i][0]==0)
    return a[0],b

def packets(record):
    starts=[i for i,t in enumerate(record) if t[0]==10]
    require(len(starts)==3,'Vertex inventory')
    # The fixture's trailing extrusion marks the end of its final vertex packet.
    ends=starts[1:]+[next(i for i,t in enumerate(record) if t[0]==210)]
    return [record[a:b] for a,b in zip(starts,ends)],list(zip(starts,ends))

def check(before,after,variant):
    a,b=record_range(before);c,d=record_range(after)
    require(a==c and list(map(key,before[:a]))==list(map(key,after[:c])),'Prefix changed')
    require(list(map(key,before[b:]))==list(map(key,after[d:])),'Suffix changed')
    source=before[a:b];actual=after[c:d]
    vertex_packets,ranges=packets(source)
    for i,p in enumerate(vertex_packets):
        expected={10:1.+i*3,20:2.+i*3,91:-7 if i==1 else 100+i}
        if variant!=1:expected.update({40:.25+i*.25,41:.5+i*.25,42:-.5 if i==1 else 0.})
        require({code for code,value in p}==set(expected),'Source vertex field inventory')
        for code,value in expected.items():
            require([key(t) for t in p if t[0]==code]==[key((code,value))],'Source vertex differs')
    x,y=ranges[1];target={10:-8.5,20:16.25,40:.5 if variant==2 else 1.25,41:.75 if variant==2 else 2.5,42:1.}
    part=[(code,target.get(code,value)) for code,value in source[x:y]]
    present={code for code,value in source[x:y]}
    part.extend((code,target[code]) for code in (40,41,42) if code not in present)
    expected=source[:x]+part+source[y:]
    require(list(map(key,actual))==list(map(key,expected)),'Complete edited packet differs')
    return c,d

def main(directory):
    cases=list(itertools.product(VERSIONS,(False,True),(False,True),range(4),(False,True)))
    stems=[f'raw-lwpolyline-{v}-{bi}-{bl}-{var}-{out}' for v,bi,bl,var,out in cases]
    names={s+'-'+side+'.dxf' for s in stems for side in ('before','after')}
    def inventory(actual):require(actual==names,'Missing or extra fixtures')
    inventory({p.name for p in directory.glob('raw-lwpolyline-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'raw-lwpolyline-extra.dxf'}))
    controls=0
    for stem,(version,_,_,variant,binary) in zip(stems,cases):
        paths=[directory/(stem+'-'+side+'.dxf') for side in ('before','after')]
        before,after=map(load_tags,paths)
        for path,tags in zip(paths,(before,after)):
            require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
            at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Declared version changed')
        a,b=check(before,after,variant)
        for at in range(a+1,b):
            code,value=after[at]
            for action in ('change','missing','repeat'):
                bad=list(after)
                if action=='change':bad[at]=(code,value+.125 if isinstance(value,(int,float)) else str(value)+'_bad')
                elif action=='missing':del bad[at]
                else:bad.insert(at,bad[at])
                controls+=reject(lambda:check(before,bad,variant))
        doc=ezdxf.readfile(paths[1]);require(not any(any(c.values()) for c in audit_signature(doc)),'Graph errors or repairs')
        entity=doc.entitydb['A'];require(entity.dxftype()=='LWPOLYLINE','Identity changed')
        expected=[(1.,2.,.25,.5,0.),(-8.5,16.25,1.25,2.5,1.),(7.,8.,.75,1.,0.)]
        if variant==1:expected[0]=(1.,2.,0.,0.,0.);expected[2]=(7.,8.,0.,0.,0.)
        if variant==2:expected[1]=(-8.5,16.25,.5,.75,1.)
        require([tuple(p) for p in entity.get_points('xyseb')]==expected,'Independent vertex/width/bulge decoding differs')
        require(entity.dxf.flags==(0 if variant==0 else 129),'Flags differ')
        require(entity.dxf.elevation==5 and entity.dxf.thickness==-2,'Plane scalars differ')
        require(tuple(entity.dxf.extrusion)==((0.,.6,.8) if variant==2 else (0.,0.,-1.)),'Extrusion differs')
        require(entity.dxf.get('const_width',0)==(2. if variant==2 else 0.),'Constant width differs')
    print(f'PASS: {len(cases)} complete source/edit pairs / {len(cases)*2} drawings across seven raw profiles; '
          f'{controls} actual-tag corruptions and two inventory controls rejected; zero graph errors or repairs.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_raw_lwpolyline_geometry.py ARTIFACTS');main(Path(sys.argv[1]))
