#!/usr/bin/env python3
"""Verify exact raw ELLIPSE edits and independently evaluated world-coordinate curves."""
from __future__ import annotations
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, key, require, reject, audit_signature

VERSIONS = {k:v for k,v in PROFILES.items() if k != 'AutoCad12'}
CODES = (10,20,30,11,21,31,40,41,42)

def geometry(variant, edited):
    normal = (0.,3.,4.) if variant == 2 else (0.,0.,-2.) if variant == 3 else (0.,0.,1.)
    if edited:
        return (8.,-16.,32.), (0.,-8.,6.) if variant == 2 else (6.,-8.,0.), .25, .25, 5.75, normal
    return ((1.25,-2.,0. if variant == 1 else 3.),
            (0.,4.,-3.) if variant == 2 else (-3.,4.,0.) if variant == 3 else (4.,0.,0.),
            1. if variant == 3 else .5, 5.5 if variant == 2 else 0., .4 if variant == 2 else 2*math.pi, normal)

def selected(tags):
    starts=[i for i,t in enumerate(tags) if t==(0,'ELLIPSE')]
    require(len(starts)==1,'Expected exactly one source ellipse')
    a=starts[0];b=next(i for i in range(a+1,len(tags)) if tags[i][0]==0)
    return a,b

def check(before, after, variant):
    a,b=selected(before);c,d=selected(after)
    require(a==c,'Ellipse moved')
    require(list(map(key,before[:a]))==list(map(key,after[:c])),'Prefix changed')
    require(list(map(key,before[b:]))==list(map(key,after[d:])),'Suffix changed')
    source=before[a:b];result=after[c:d]
    center,major,ratio,start,end,normal=geometry(variant,False)
    fields=dict(zip(CODES,center+major+(ratio,start,end)))
    fields.update(dict(zip((210,220,230),normal)))
    for code,value in fields.items():
        want=[] if variant==1 and code in (30,31,210,220,230) else [key((code,value))]
        require([key(t) for t in source if t[0]==code]==want,'Regenerated input differs')
    center,major,ratio,start,end,_=geometry(variant,True)
    values=dict(zip(CODES,center+major+(ratio,start,end)));present={c for c,v in source}
    expected=[]
    for code,value in source:
        expected.append((code,values.get(code,value)))
        if code in (20,21) and code+10 not in present and values[code+10] != 0.:
            expected.append((code+10,values[code+10]))
    require(list(map(key,result))==list(map(key,expected)),'Unexpected edited packet')
    return c,d

def verify_curve(entity, variant, edited):
    center,major,ratio,start,end,normal=geometry(variant,edited)
    require(tuple(entity.dxf.center)==center and tuple(entity.dxf.major_axis)==major,'WCS definition differs')
    require(entity.dxf.ratio==ratio and entity.dxf.start_param==start and entity.dxf.end_param==end,'Parameters changed')
    require(tuple(entity.dxf.extrusion)==normal,'Extrusion normalized or changed')
    length=math.hypot(*normal);nx,ny,nz=(v/length for v in normal);ax,ay,az=major
    minor=((ny*az-nz*ay)*ratio,(nz*ax-nx*az)*ratio,(nx*ay-ny*ax)*ratio)
    # Sample the stored parameter domain directly, without changing reversed intervals.
    params=[start+(end-start)*i/16 for i in range(17)]
    actual=list(entity.vertices(params))
    for t,p in zip(params,actual):
        expected=tuple(center[i]+major[i]*math.cos(t)+minor[i]*math.sin(t) for i in range(3))
        require(all(math.isclose(p[i],expected[i],rel_tol=2e-13,abs_tol=2e-13) for i in range(3)), 'Independent curve sample differs')

def main(directory):
    specs=list(itertools.product(VERSIONS,(False,True),(False,True),range(4),(False,True)))
    stems=[f'raw-ellipse-{v}-{bi}-{bl}-{var}-{out}' for v,bi,bl,var,out in specs]
    names={stem+'-'+side+'.dxf' for stem in stems for side in ('before','after')}
    def inventory(actual):require(actual==names,'Missing or extra ellipse fixtures')
    inventory({p.name for p in directory.glob('raw-ellipse-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'raw-ellipse-extra.dxf'}))
    corruptions=0
    for stem,(version,_,_,variant,binary) in zip(stems,specs):
        paths=[directory/(stem+'-'+side+'.dxf') for side in ('before','after')]
        before,after=map(load_tags,paths)
        for path,tags in zip(paths,(before,after)):
            require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
            at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Declared version changed')
        a,b=check(before,after,variant)
        for at in range(a+1,b):
            code,value=after[at]
            for op in ('change','delete','duplicate'):
                damaged=list(after)
                if op=='change':damaged[at]=(code,value+.125 if isinstance(value,(int,float)) else str(value)+'_bad')
                elif op=='delete':del damaged[at]
                else:damaged.insert(at,damaged[at])
                corruptions+=reject(lambda:check(before,damaged,variant))
        for edited,path in enumerate(paths):
            doc=ezdxf.readfile(path)
            require(not any(any(c.values()) for c in audit_signature(doc)),'Graph errors or repairs')
            entity=doc.entitydb['A'];require(entity.dxftype()=='ELLIPSE','Identity changed')
            verify_curve(entity,variant,bool(edited))
    print(f'PASS: {len(specs)} source/edit pairs / {len(specs)*2} drawings; {len(specs)*34} independent curve samples; '
          f'{corruptions} tag corruptions and two inventory controls rejected; zero graph errors or repairs.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_raw_ellipse_geometry.py ARTIFACTS')
    main(Path(sys.argv[1]))
