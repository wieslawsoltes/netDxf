#!/usr/bin/env python3
"""Verify stored width edits and unchanged/invalidated proxy packets independently."""
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tags import Tags
from ezdxf.lldxf.types import DXFTag
from ezdxf.proxygraphic import load_proxy_graphic
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_raw_line_geometry import PROFILES, require, reject, audit_signature
VERSIONS={k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13','AutoCad14')}

def check(tags,mode):
    a=[i for i,t in enumerate(tags) if t==(0,'LWPOLYLINE')];require(len(a)==1,'Polyline inventory')
    a=a[0];b=next(i for i in range(a+1,len(tags)) if tags[i][0]==0);r=tags[a:b]
    constant=[2.] if mode in (0,2) else [0.] if mode==6 else []
    require([v for c,v in r if c==43]==constant,'Constant width changed')
    for code in (40,41):
        widths=[v for c,v in r if c==code]
        if mode in (3,4,5):require(widths==[1.]*3,'Vertex width missing or changed')
        else:require(not widths or widths==[0.]*3,'Unexpected vertex widths')
    require([v for c,v in r if c==10]==[1.,4.,7.] and [v for c,v in r if c==20]==[2.,5.,8.],'Vertex positions changed')
    require([v for c,v in r if c==90]==[3],'Count changed')
    chunks=[v for c,v in r if c==310];retained=mode in (1,2,4)
    if retained:
        require(b''.join(chunks)==bytes((1,3,7,11)),'Valid proxy changed')
        require([v for c,v in r if c in (92,160)]==[4],'Proxy size changed')
    else:require(not any(c in (92,160,310) for c,v in r),'Stale proxy retained')
    return a,b

def main(directory):
    cases=list(itertools.product(VERSIONS,(False,True),range(7)))
    names={f'polyline-width-proxy-{v}-{b}-{m}.dxf' for v,b,m in cases}
    def inventory(actual):require(actual==names,'Missing/extra width fixtures')
    inventory({p.name for p in directory.glob('polyline-width-proxy-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'polyline-width-proxy-extra.dxf'}))
    controls=0
    for version,binary,mode in cases:
        path=directory/f'polyline-width-proxy-{version}-{binary}-{mode}.dxf';tags=load_visibility_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
        at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Version changed')
        a,b=check(tags,mode)
        for code in (10,20,90):
            at=next(i for i in range(a,b) if tags[i][0]==code);damage=list(tags);damage[at]=(code,tags[at][1]+1)
            controls+=reject(lambda:check(damage,mode))
        damage=tags[:b]+[(43,7.)]+tags[b:];controls+=reject(lambda:check(damage,mode))
        damage=([t for t in tags if t[0] not in (92,160,310)] if mode in (1,2,4)
                else tags[:b]+[(92,4),(310,bytes((1,3,7,11)))]+tags[b:])
        controls+=reject(lambda:check(damage,mode))
        doc=ezdxf.readfile(path);require(not any(any(c.values()) for c in audit_signature(doc)),'Graph errors/repairs')
        e=list(doc.modelspace().query('LWPOLYLINE'))[0]
        require(e.dxf.get('const_width',0)==(2 if mode in (0,2) else 0),'Independent width')
        packet=Tags(DXFTag(c,v) for c,v in tags[a:b]);decoded=load_proxy_graphic(packet,length_code=92 if version=='AutoCad2000' else 160)
        require(decoded==(bytes((1,3,7,11)) if mode in (1,2,4) else None),'Independent proxy packet')
    print(f'PASS: {len(cases)} drawings; {controls} actual-packet corruptions and two inventory controls rejected; zero graph errors or repairs.')
if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_polyline_width_proxies.py ARTIFACTS');main(Path(sys.argv[1]))
