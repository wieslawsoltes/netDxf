#!/usr/bin/env python3
"""Check resized ELLIPSE axes and retained/invalidated proxy packets independently."""
import itertools
import io
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value, DXFTag
from ezdxf.lldxf.tags import Tags
from ezdxf.proxygraphic import load_proxy_graphic
from verify_raw_line_geometry import PROFILES, require, reject, audit_signature

VERSIONS={k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13','AutoCad14')}
AXES=((8.,4.),(16.,4.),(4.,16.),(8.,2.),(4.,8.),(8.,8.))

def load_visibility_tags(path):
    # Binary chunk values must be captured before generic casting: the generic
    # schema caster stringifies bytes, which destroys the independent packet view.
    data = path.read_bytes()
    tags = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else \
        ascii_tags_loader(io.StringIO(data.decode('cp1252'), newline=None))
    result = []
    for tag in tags:
        if 310 <= tag.code <= 319:
            value = tag.value
            if isinstance(value, str):
                require(len(value) % 2 == 0 and all(c in '0123456789abcdefABCDEF' for c in value),
                        'Malformed hexadecimal proxy chunk')
                value = bytes.fromhex(value)
            require(isinstance(value, bytes), 'Unexpected proxy chunk representation')
        else:
            value = cast_tag_value(tag.code, tag.value)
        result.append((tag.code, value))
    return result

def check(tags,mode):
    starts=[i for i,t in enumerate(tags) if t==(0,'ELLIPSE')];require(len(starts)==1,'Ellipse inventory')
    a=starts[0];b=next(i for i in range(a+1,len(tags)) if tags[i][0]==0);record=tags[a:b]
    major,minor=sorted(AXES[mode],reverse=True)
    expected={10:1.,20:2.,30:3.,11:major/2,21:0.,31:0.,40:minor/major,41:0.,42:6.283185307179586}
    for code,value in expected.items():require([v for c,v in record if c==code]==[value],f'Wrong/missing/duplicate ellipse field {code}')
    chunks=[v for c,v in record if c==310]
    if mode in (0,4):
        require(b''.join(chunks)==bytes((1,3,7,11)),'Unchanged proxy differs')
        require([v for c,v in record if c in (92,160)]==[4],'Proxy size differs')
    else:require(not any(c in (92,160,310) for c,v in record),'Stale proxy survived resize')
    return a,b

def main(directory):
    specs=list(itertools.product(VERSIONS,(False,True),range(6)))
    names={f'ellipse-axis-proxy-{v}-{b}-{m}.dxf' for v,b,m in specs}
    def inventory(actual):require(actual==names,'Missing/extra ellipse-axis fixtures')
    inventory({p.name for p in directory.glob('ellipse-axis-proxy-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}));reject(lambda:inventory(names|{'ellipse-axis-proxy-extra.dxf'}))
    controls=0
    for version,binary,mode in specs:
        path=directory/f'ellipse-axis-proxy-{version}-{binary}-{mode}.dxf';tags=load_visibility_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
        at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Profile changed')
        a,b=check(tags,mode)
        for code in (11,40):
            at=next(i for i in range(a,b) if tags[i][0]==code)
            changed=list(tags);changed[at]=(code,tags[at][1]+.125)
            for damage in (changed,tags[:at]+tags[at+1:],tags[:at]+[tags[at]]+tags[at:]):controls+=reject(lambda:check(damage,mode))
        if mode in (0,4):damage=[t for t in tags if t[0] not in (92,160,310)]
        else:damage=tags[:b]+[(92,4),(310,bytes((1,3,7,11)))]+tags[b:]
        controls+=reject(lambda:check(damage,mode))
        doc=ezdxf.readfile(path);require(not any(any(c.values()) for c in audit_signature(doc)),'Graph errors/repairs')
        entity=list(doc.modelspace().query('ELLIPSE'))[0]
        major,minor=sorted(AXES[mode],reverse=True)
        require(tuple(entity.dxf.major_axis)==(major/2,0.,0.) and entity.dxf.ratio==minor/major,'Independent axes differ')
        # The ELLIPSE loader intentionally skips proxy_graphic. Exercise the
        # independent general packet extractor, rather than claiming that the
        # loaded ELLIPSE object preserves data its loader deliberately omits.
        packet = Tags(DXFTag(code, value) for code, value in tags[a:b])
        decoded = load_proxy_graphic(packet, length_code=92 if version == 'AutoCad2000' else 160)
        require(decoded==(bytes((1,3,7,11)) if mode in (0,4) else None), 'Independent proxy packet differs')
    print(f'PASS: {len(specs)} ellipse drawings; {controls} corruptions and two inventory controls rejected; zero graph errors or repairs.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_ellipse_axis_proxies.py ARTIFACTS');main(Path(sys.argv[1]))
