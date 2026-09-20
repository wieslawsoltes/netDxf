#!/usr/bin/env python3
"""Compare complete SOLID/TRACE source/edit tags and independently decoded OCS geometry."""
from __future__ import annotations
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, key, require, reject, audit_signature

CODES = (10,20,30,11,21,31,12,22,32,13,23,33)
TARGET = ((-8.5,16.25,-32.), (64.,-128.5,256.25), (7.,9.,-11.), (-17.,19.,23.))
VALUES = dict(zip(CODES, (v for p in TARGET for v in p)))
VALUES[39] = -4.25


def corners(variant):
    p = [(1.,2.,3.), (5.,2.,3.), (1.,6.,3.), (5.,6.,3.)]
    if variant == 1:
        p = [(x,y,0.) for x,y,_ in p]
    if variant == 2:
        p[3] = p[2]
    if variant == 3:
        p = [(x,y,float(1 << i)) for i,(x,y,_) in enumerate(p)]
    return p


def target_range(tags, kind):
    starts = [i for i,t in enumerate(tags) if t == (0,kind)]
    require(len(starts) == 1, 'Expected one selected quad')
    start = starts[0]
    end = next(i for i in range(start+1,len(tags)) if tags[i][0] == 0)
    return start,end


def check_pair(before, after, kind, variant):
    a,b = target_range(before,kind); c,d = target_range(after,kind)
    require(a == c, 'Selected quad moved')
    require(list(map(key,before[:a])) == list(map(key,after[:c])), 'Unrelated prefix changed')
    require(list(map(key,before[b:])) == list(map(key,after[d:])), 'Unrelated suffix changed')
    source,result = before[a:b],after[c:d]
    original = dict(zip(CODES,(v for p in corners(variant) for v in p)))
    for code,value in original.items():
        expected = [] if variant == 1 and code >= 30 else [value]
        require([v for k,v in source if k == code] == expected, 'Regenerated source OCS corner differs')
    require([v for k,v in source if k == 39] == ([] if variant == 1 else [-2.5]), 'Source thickness differs')
    existing = {k for k,_ in source}
    last = max(i for i,(k,_) in enumerate(source) if k in CODES)
    expected = []
    for i,(code,value) in enumerate(source):
        expected.append((code, VALUES.get(code,value)))
        if 20 <= code <= 23 and code+10 not in existing:
            expected.append((code+10,VALUES[code+10]))
        if i == last and 39 not in existing:
            expected.append((39,VALUES[39]))
    require(list(map(key,result)) == list(map(key,expected)), 'Quad edit or unselected tag preservation differs')


def main(directory):
    specs = list(itertools.product(PROFILES,(False,True),(False,True),(False,True),range(4),(False,True)))
    stems = [f'raw-quad-{v}-{tr}-{bi}-{bl}-{var}-{out}' for v,tr,bi,bl,var,out in specs]
    names = {stem+'-'+side+'.dxf' for stem in stems for side in ('before','after')}
    def inventory(actual):
        require(actual == names,'Missing or extra raw quad fixture')
    inventory({p.name for p in directory.glob('raw-quad-*.dxf')})
    reject(lambda: inventory(names-{next(iter(names))}))
    reject(lambda: inventory(names|{'raw-quad-extra.dxf'}))
    corruptions = 0
    for stem,(version,trace,_,_,variant,binary) in zip(stems,specs):
        kind = 'TRACE' if trace else 'SOLID'
        paths = [directory/(stem+'-'+side+'.dxf') for side in ('before','after')]
        before,after = map(load_tags,paths)
        for path,tags in zip(paths,(before,after)):
            data = path.read_bytes()
            require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
            at = tags.index((9,'$ACADVER'))
            require(tags[at+1] == (1,PROFILES[version]), 'Declared profile changed')
            if binary:
                require((data[23] != 0) == (version == 'AutoCad12'), 'Legacy binary framing changed')
        check_pair(before,after,kind,variant)
        a,b = target_range(after,kind)
        for i in range(a+1,b):
            code,value = after[i]
            for op in ('change','remove','repeat'):
                bad = list(after)
                if op == 'remove':
                    del bad[i]
                elif op == 'repeat':
                    bad.insert(i,bad[i])
                else:
                    bad[i] = (code,value+.5 if isinstance(value,(int,float)) else str(value)+'_corrupt')
                corruptions += reject(lambda: check_pair(before,bad,kind,variant))
        for marker in ((1,'unchanged'),(10,11.)):
            at = next(i for i in range(b,len(after)) if after[i] == marker)
            bad = list(after); bad[at] = (marker[0],'corrupt' if isinstance(marker[1],str) else 12.)
            corruptions += reject(lambda: check_pair(before,bad,kind,variant))
        normal = (0.,0.,1.) if variant == 1 else (0.,0.,-1.) if variant == 2 else (0.,6.,8.) if variant == 3 else (0.,.6,.8)
        for path,points,thickness in zip(paths,(corners(variant),TARGET),(0. if variant == 1 else -2.5,-4.25)):
            doc = ezdxf.readfile(path)
            signature = audit_signature(doc)
            require(not any(any(c.values()) for c in signature),'Quad graph errors or repairs')
            quad = doc.entitydb['A'];require(quad.dxftype() == kind,'Independent quad type changed')
            for i,point in enumerate(points):
                require(tuple(quad.dxf.get(f'vtx{i}')) == tuple(point),'Independent stored OCS corner differs')
            require(quad.dxf.thickness == thickness,'Independent thickness differs')
            require(tuple(quad.dxf.extrusion) == normal,'Independent extrusion was normalized or changed')
    print(f'PASS: {len(stems)} source/edit pairs / {len(stems)*2} drawings; SOLID and TRACE across nine raw families; '
          f'{corruptions} actual-tag corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2,'Usage: verify_raw_quad_geometry.py ARTIFACTS')
    main(Path(sys.argv[1]))
