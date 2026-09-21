#!/usr/bin/env python3
"""Verify direct ELLIPSE mutation geometry and retained/cleared proxy packets."""
from pathlib import Path
import itertools
import math
import sys
import ezdxf
from ezdxf.lldxf.types import DXFTag
from ezdxf.lldxf.tags import Tags
from ezdxf.proxygraphic import load_proxy_graphic
from verify_raw_line_geometry import PROFILES, require, reject, audit_signature
from verify_ellipse_axis_proxies import load_visibility_tags

VERSIONS={k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13','AutoCad14')}
PROXY=bytes((1,3,7,11))


def check(tags,index,changed):
    starts=[i for i,t in enumerate(tags) if t==(0,'ELLIPSE')];require(len(starts)==1,'Ellipse inventory')
    start=starts[0];end=next(i for i in range(start+1,len(tags)) if tags[i][0]==0);record=tags[start:end]
    center=(-8.,9.,10.) if changed and index==0 else (1.,2.,3.)
    major=(0.,4.,0.) if changed and index==1 else (4.,0.,0.)
    first=math.pi/2 if changed and index==2 else 0.
    last=0. if changed and index==2 else math.pi/2 if changed and index==3 else math.tau
    fields=dict(zip((10,20,30),center));fields.update(zip((11,21,31),major))
    fields.update(zip((210,220,230),(0.,0.,1.)));fields.update({40:.5,41:first,42:last})
    for code,wanted in fields.items():
        actual=[v for c,v in record if c==code]
        require(len(actual)==1 and type(actual[0]) in (int,float) and math.isfinite(actual[0]) and abs(actual[0]-wanted)<3e-14,
                f'Unexpected ellipse field {code}')
    if changed:
        require(not any(c in (92,160,310) for c,_ in record),'Stale proxy retained')
    else:
        chunks=[v for c,v in record if c==310]
        require(all(isinstance(v,bytes) for v in chunks) and b''.join(chunks)==PROXY,'Unchanged proxy differs')
        require([v for c,v in record if c in (92,160)]==[len(PROXY)],'Proxy length packet differs')
    return start,end,fields


def main(directory):
    specs=list(itertools.product(VERSIONS,(False,True),range(5),(False,True)))
    names={f'ellipse-mutation-{v}-{b}-{i}-{c}.dxf' for v,b,i,c in specs}
    def inventory(actual):require(actual==names,'Missing or extra ellipse mutation fixtures')
    inventory({p.name for p in directory.glob('ellipse-mutation-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'ellipse-mutation-extra.dxf'}))
    corruptions=0
    for version,binary,index,changed in specs:
        path=directory/f'ellipse-mutation-{version}-{binary}-{index}-{changed}.dxf';tags=load_visibility_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
        at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Version changed')
        start,end,fields=check(tags,index,changed)
        for code in (10,11,40,41,42):
            at=next(i for i in range(start,end) if tags[i][0]==code)
            bad=list(tags);bad[at]=(code,float(tags[at][1])+1)
            for damage in (bad,tags[:at]+tags[at+1:],tags[:at]+[tags[at]]+tags[at:]):
                corruptions+=reject(lambda:check(damage,index,changed))
        damage=tags[:end]+[(92,4),(310,PROXY)]+tags[end:] if changed else [t for t in tags if t[0] not in (92,160,310)]
        corruptions+=reject(lambda:check(damage,index,changed))
        doc=ezdxf.readfile(path);require(not any(any(c.values()) for c in audit_signature(doc)),'Graph errors or repairs')
        entities=list(doc.modelspace().query('ELLIPSE'));require(len(entities)==1,'Independent ellipse inventory')
        entity=entities[0]
        require(tuple(entity.dxf.center)==tuple(fields[c] for c in (10,20,30)),'Independent center differs')
        require(all(abs(entity.dxf.major_axis[i]-fields[c])<3e-14 for i,c in enumerate((11,21,31))),'Independent major differs')
        require(abs(entity.dxf.start_param-fields[41])<3e-14 and abs(entity.dxf.end_param-fields[42])<3e-14,'Independent parameters differ')
        # The ELLIPSE loader skips proxy_graphic: use its generic packet decoder.
        packet=Tags(DXFTag(c,v) for c,v in tags[start:end])
        decoded=load_proxy_graphic(packet,length_code=92 if version=='AutoCad2000' else 160)
        require(decoded==(None if changed else PROXY),'Independent proxy packet differs')
    print(f'PASS: {len(specs)} drawings; {corruptions} actual packet corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_ellipse_mutation_proxies.py ARTIFACTS')
    main(Path(sys.argv[1]))
