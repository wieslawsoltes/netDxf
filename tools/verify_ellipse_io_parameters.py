#!/usr/bin/env python3
"""Validate physical ELLIPSE IO packets and ordinary independent-reader curves."""
from pathlib import Path
import itertools
import math
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, require, reject, audit_signature

VERSIONS = {k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13','AutoCad14')}
CASES = (
    (4,.5,0,math.tau,True), (4,.5,.3,.3+1e-11,False),
    (4,.5,0,math.tau-1e-11,False), (4,.5,1e-13,2e-13,False),
    (4,.5,5.5,.4,False), (4,.5,-math.pi/2,0,False),
    (4,.5,math.pi,3*math.pi/2,False),
    (1e-200,.5,.25,2.5,False), (1e-310,.5,.25,2.5,False),
    (1e200,.5,.25,2.5,False), (1e307,.5,.25,2.5,False),
    (4,1e-200,math.pi/2,math.pi,False), (4,.5,.3,.3,True),
)


def check(tags,kind,source):
    indices=[i for i,t in enumerate(tags) if t==(0,'ELLIPSE')]
    require(len(indices)==1,'ELLIPSE inventory')
    at=indices[0]; end=next(i for i in range(at+1,len(tags)) if tags[i][0]==0)
    record=tags[at:end];axis,ratio,first,last,full=CASES[kind]
    def value(code):
        found=[v for c,v in record if c==code]
        require(len(found)==1 and type(found[0]) in (int,float) and math.isfinite(found[0]),f'Invalid field {code}')
        return found[0]
    for code,wanted in ((10,1.),(20,2.),(30,3.),(11,0.),(31,0.),(210,0.),(220,0.),(230,1.)):
        require(value(code)==wanted,f'Plane/center field {code} changed')
    require(abs(value(21)/axis-1)<2e-12 and abs(value(40)/ratio-1)<2e-12,'Axis/ratio changed')
    a,b=value(41),value(42)
    if source:
        require(a==first and b==last,'Source parameters changed')
    elif full:
        require(a==0 and b==math.tau,'Full ellipse parameter packet')
    else:
        require(a%math.tau!=b%math.tau,'Distinct arc collapsed')
        require(abs(math.remainder(a-first,math.tau))<8e-14 and abs(math.remainder(b-last,math.tau))<8e-14,'Parameter phase changed')
    return at,end


def main(directory):
    specs=list(itertools.product(VERSIONS,(False,True),(False,True),range(len(CASES))))
    names={f'ellipse-io-{v}-{b}-{block}-{k}-{side}.dxf' for v,b,block,k in specs for side in ('source','False','True')}
    def inventory(actual):require(actual==names,'Missing or extra ELLIPSE IO files')
    inventory({p.name for p in directory.glob('ellipse-io-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'ellipse-io-extra.dxf'}))
    corruptions=ordinary=0
    for version,binary,block,kind in specs:
        for side in ('source','False','True'):
            path=directory/f'ellipse-io-{version}-{binary}-{block}-{kind}-{side}.dxf'
            tags=load_tags(path);source=side=='source'
            require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==(binary if source else side=='True'),'Transport')
            at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Version')
            start,end=check(tags,kind,source)
            for code in (11,21,31,40,41,42):
                at=next(i for i in range(start,end) if tags[i][0]==code)
                damage=list(tags);damage[at]=(code,float(tags[at][1])*2+1)
                for damaged in (damage,tags[:at]+tags[at+1:],tags[:at]+[tags[at]]+tags[at:]):
                    corruptions+=reject(lambda:check(damaged,kind,source))
            # Extreme magnitudes/aspect ratios are physically checked above;
            # native/independent geometric acceptance is not inferred for them.
            if kind in (0,1,2,3,4,5,6,12):
                doc=ezdxf.readfile(path)
                require(not any(any(c.values()) for c in audit_signature(doc)),'Graph errors/repairs')
                entities=list((doc.blocks['ELLIPSE_IO'] if block else doc.modelspace()).query('ELLIPSE'))
                require(len(entities)==1,'Independent entity inventory')
                e=entities[0];a=e.dxf.start_param;b=e.dxf.end_param
                axis,ratio,first,last,full=CASES[kind]
                span=math.tau if full else (last-first)%math.tau
                output_span=math.tau if full else (b-a)%math.tau
                for i,p in enumerate(e.vertices(a+output_span*j/16 for j in range(17))):
                    t=(0 if full and not source else first)+span*i/16
                    expected=(1-axis*ratio*math.sin(t),2+axis*math.cos(t),3)
                    require(all(abs(p[j]-expected[j])<2e-12 for j in range(3)),'Independent WCS sample')
                ordinary+=1
    print(f'PASS: {len(names)} physical drawings; {ordinary} independently loaded ordinary drawings / {ordinary*17} WCS samples; '
          f'{corruptions} packet corruptions and two inventory controls rejected.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_ellipse_io_parameters.py ARTIFACTS')
    main(Path(sys.argv[1]))
