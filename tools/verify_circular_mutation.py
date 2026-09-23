#!/usr/bin/env python3
"""Independently check CIRCLE/ARC direct edits, OCS geometry and proxy packets."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.types import DXFTag
from ezdxf.lldxf.tags import Tags
from ezdxf.proxygraphic import load_proxy_graphic
from verify_raw_line_geometry import PROFILES, require, reject
from verify_ellipse_axis_proxies import load_visibility_tags

VERSIONS={k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13','AutoCad14')}
PROXY=bytes((1,3,7,255))


def expected(arc,field,changed,tilted):
    center=(-8.,9.,10.) if changed and field=='Center' else (1.,2.,3.)
    ocs=(center[1],center[2],center[0]) if tilted else center
    fields=dict(zip((10,20,30),ocs))
    fields.update({40:4. if changed and field=='Radius' else 3.,39:-2. if changed and field=='Thickness' else 1.,62:3,60:1})
    fields.update(zip((210,220,230),(1.,0.,0.) if tilted else (0.,0.,1.)))
    if arc:fields.update({50:90. if changed and field=='StartAngle' else 30.,51:90. if changed and field=='EndAngle' else 210.})
    return center,fields


def check(tags,arc,field,changed,tilted):
    at=[i for i,t in enumerate(tags) if t==(0,'ARC' if arc else 'CIRCLE')]
    require(len(at)==1,'Circular entity inventory');start=at[0]
    end=next(i for i in range(start+1,len(tags)) if tags[i][0]==0);record=tags[start:end]
    _,fields=expected(arc,field,changed,tilted)
    for code,wanted in fields.items():
        values=[v for c,v in record if c==code]
        require(len(values)==1 and type(values[0]) in (int,float) and math.isfinite(values[0]) and values[0]==wanted,
                f'Wrong/absent/repeated group {code}')
    chunks=[v for c,v in record if c==310]
    lengths=[v for c,v in record if c in (92,160)]
    require((not chunks and not lengths) if changed else (lengths==[4] and all(isinstance(x,bytes) for x in chunks) and b''.join(chunks)==PROXY),'Wrong proxy bytes/presence')
    return start,end,fields


def main(directory):
    specs=[(v,b,a,f,c,t) for v,b,a,c,t in itertools.product(VERSIONS,(False,True),(False,True),(False,True),(False,True))
           for f in (('Center','Radius','Thickness','StartAngle','EndAngle') if a else ('Center','Radius','Thickness'))]
    def name(s):
        v,b,a,f,c,t=s;return f'circular-mutation-{v}-{b}-{a}-{f}-{c}-{t}.dxf'
    names={name(s) for s in specs}
    def inventory(actual):require(actual==names,'Missing or extra circular mutation fixtures')
    inventory({p.name for p in directory.glob('circular-mutation-*.dxf')})
    reject(lambda:inventory(names-{min(names)}));reject(lambda:inventory(names|{'circular-mutation-extra.dxf'}))
    controls=0
    for spec in specs:
        version,binary,arc,field,changed,tilted=spec;path=directory/name(spec);tags=load_visibility_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport')
        at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Profile')
        start,end,fields=check(tags,arc,field,changed,tilted)
        for code in fields:
            at=next(i for i in range(start,end) if tags[i][0]==code)
            bad=list(tags);bad[at]=(code,float(tags[at][1])+1)
            for damage in (bad,tags[:at]+tags[at+1:],tags[:at]+[tags[at]]+tags[at:]):
                controls+=reject(lambda:check(damage,arc,field,changed,tilted))
        bad=tags[:end]+[(92,4),(310,PROXY)]+tags[end:] if changed else [t for t in tags if t[0] not in (92,160,310)]
        controls+=reject(lambda:check(bad,arc,field,changed,tilted))
        packet=Tags(DXFTag(c,v) for c,v in tags[start:end])
        require(load_proxy_graphic(packet,length_code=92 if version=='AutoCad2000' else 160)==(None if changed else PROXY),'Independent proxy decoder')
        doc=ezdxf.readfile(path);entities=list(doc.modelspace().query('ARC CIRCLE'));require(len(entities)==1,'Independent entity inventory')
        entity=entities[0];center,_=expected(arc,field,changed,tilted)
        require(tuple(entity.ocs().to_wcs(entity.dxf.center))==center and entity.dxf.radius==fields[40] and entity.dxf.thickness==fields[39],'Independent world center/radius/thickness')
        first=fields[50] if arc else 0.;span=(fields[51]-first)%360 if arc else 360.
        for j in range(17):
            angle=math.radians(first+span*j/16);u=fields[40]*math.cos(angle);v=fields[40]*math.sin(angle)
            want=(center[0],center[1]+u,center[2]+v) if tilted else (center[0]+u,center[1]+v,center[2])
            point=entity.ocs().to_wcs((entity.dxf.center.x+u,entity.dxf.center.y+v,entity.dxf.center.z))
            require(all(abs(point[i]-want[i])<1e-13 for i in range(3)),'Independent WCS sample')
        audit=doc.audit();require(not audit.errors and not audit.fixes,'Independent graph errors/repairs')
    print(f'PASS: {len(specs)} drawings / {17*len(specs)} WCS samples; {controls} packet corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_circular_mutation.py ARTIFACTS')
    main(Path(sys.argv[1]))
