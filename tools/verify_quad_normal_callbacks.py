#!/usr/bin/env python3
"""Check callback-isolated SOLID/TRACE transformations with independent WCS points."""
from pathlib import Path
import itertools
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, require, reject, audit_signature

VERSIONS = {k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13','AutoCad14')}
CORNERS = ((1,2),(4,3),(2,7),(6,8))

def check(tags, trace):
    kind = 'TRACE' if trace else 'SOLID'
    starts = [i for i,t in enumerate(tags) if t==(0,kind)]
    require(len(starts)==1,'Selected quad inventory')
    start=starts[0];end=next(i for i in range(start+1,len(tags)) if tags[i][0]==0)
    record=tags[start:end]
    expected={39:-2.,210:1.,220:0.,230:0.,62:3,60:1}
    for i,(x,y) in enumerate(CORNERS):
        # Independent matrix image: (x,y,3) -> (13,20+y,30-x).
        # OCS for +X normal has axes +Y,+Z,+X.
        expected.update({10+i:20.+y,20+i:30.-x,30+i:13.})
    for code,wanted in expected.items():
        require([v for c,v in record if c==code]==[wanted],f'Incorrect, missing or repeated group {code}')
    require(not any(c in (92,160,310) for c,v in record),'Stale proxy retained')
    return start,end,expected

def main(directory):
    cases=list(itertools.product(VERSIONS,(False,True),(False,True)))
    names={f'quad-normal-callback-{v}-{b}-{t}.dxf' for v,b,t in cases}
    def inventory(actual):require(actual==names,'Missing or extra callback fixtures')
    inventory({p.name for p in directory.glob('quad-normal-callback-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'quad-normal-callback-extra.dxf'}))
    corruptions=0
    for version,binary,trace in cases:
        path=directory/f'quad-normal-callback-{version}-{binary}-{trace}.dxf'
        tags=load_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
        at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Version changed')
        start,end,fields=check(tags,trace)
        for code in fields:
            at=next(i for i in range(start,end) if tags[i][0]==code)
            changed=list(tags);changed[at]=(code,fields[code]+1)
            for altered in (changed,tags[:at]+tags[at+1:],tags[:at]+[tags[at]]+tags[at:]):
                corruptions+=reject(lambda:check(altered,trace))
        corruptions+=reject(lambda:check(tags[:end]+[(92,1),(310,b'x')]+tags[end:],trace))
        doc=ezdxf.readfile(path)
        require(not any(any(c.values()) for c in audit_signature(doc)),'Independent graph errors or repairs')
        entities=list(doc.modelspace().query('TRACE' if trace else 'SOLID'));require(len(entities)==1,'Independent quad inventory')
        entity=entities[0]
        for i,(x,y) in enumerate(CORNERS):
            actual=tuple(entity.ocs().to_wcs(entity.dxf.get(f'vtx{i}')))
            require(actual==(13.,20.+y,30.-x),'Independent WCS image differs')
        require(entity.dxf.thickness==-2 and entity.proxy_graphic is None,'Extrusion or proxy changed')
    print(f'PASS: {len(cases)} drawings / {len(cases)*4} independent WCS corners; {corruptions} packet corruptions and two inventory controls rejected; zero graph errors or repairs.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_quad_normal_callbacks.py ARTIFACT_DIRECTORY')
    main(Path(sys.argv[1]))
