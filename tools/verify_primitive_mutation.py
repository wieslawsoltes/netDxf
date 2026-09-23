#!/usr/bin/env python3
"""Independently verify primitive direct edits, WCS geometry and proxy removal."""
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tags import Tags
from ezdxf.lldxf.types import DXFTag
from ezdxf.proxygraphic import load_proxy_graphic
from verify_raw_line_geometry import key, require, reject
from verify_ellipse_axis_proxies import load_visibility_tags, VERSIONS
from verify_dimlfac_fidelity import records

SLOTS = (("LINE","StartPoint"),("LINE","EndPoint"),("LINE","Thickness"),
         ("POINT","Position"),("POINT","Thickness"),("POINT","Rotation"),
         ("RAY","Origin"),("RAY","Direction"),("XLINE","Origin"),("XLINE","Direction"))
PROXY = bytes((1,3,7,255))


def expected(row, tilted):
    kind, field = SLOTS[row // 2]; changed = bool(row % 2)
    first = (-8.,9.,10.) if changed and field in ('StartPoint','Position','Origin') else (1.,2.,3.)
    second = (-8.,9.,10.) if changed and field == 'EndPoint' else (4.,5.,6.)
    direction = (0.,1.,0.) if changed and field == 'Direction' else (1.,0.,0.)
    values = dict(zip((10,20,30), first)); values.update({62:3,60:1,48:1.25})
    if kind == 'LINE': values.update(zip((11,21,31), second))
    if kind in ('LINE','POINT'):
        values[39] = -2. if changed and field == 'Thickness' else 2.
        values.update(zip((210,220,230),(1.,0.,0.) if tilted else (0.,0.,1.)))
    else: values.update(zip((11,21,31),direction))
    if kind == 'POINT': values[50] = 270. if changed and field == 'Rotation' else 330.
    return kind, changed, values


def packet(record, row, tilted, version):
    kind, changed, values = expected(row, tilted)
    require(record[0] == (0,kind), 'Wrong primitive type')
    for code, wanted in values.items():
        actual = [t for t in record if t[0] == code]
        require(len(actual) == 1 and key(actual[0]) == key((code,wanted)), f'Wrong/missing/duplicate group {code}')
    if kind in ('RAY','XLINE'):
        require(not any(c in (39,50,210,220,230) for c,_ in record),'Unexpected infinite-line geometry field')
    proxies = [t for t in record if t[0] in (92,160,310)]
    length_code = 92 if version == 'AutoCad2000' else 160
    require(proxies == ([] if changed else [(length_code,4),(310,PROXY)]), 'Proxy field inventory or bytes')
    return values


def inspect(path, version, binary, tilted, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary,'Wrong transport')
    tags = load_visibility_tags(path)
    av = tags.index((9,'$ACADVER')); require(tags[av+1] == (1,VERSIONS[version]),'Wrong version')
    entries = [tags[a:b] for a,b in records(tags)]
    hosts = [r for r in entries if any(c==8 and str(v).startswith('PRIMITIVE_') for c,v in r)]
    require(len(hosts)==20,'Physical host count')
    require({next(v for c,v in r if c==8) for r in hosts} == {f'PRIMITIVE_{i:02d}' for i in range(20)},'Host layer inventory')
    controls=0
    for record in hosts:
        row=int(next(v for c,v in record if c==8).split('_')[1]);fields=packet(record,row,tilted,version)
        for code in fields:
            at=next(i for i,t in enumerate(record) if t[0]==code)
            for operation in ('change','remove','duplicate','wrong-code'):
                bad=list(record)
                if operation=='change': bad[at]=(code,bad[at][1]+1)
                elif operation=='remove': del bad[at]
                elif operation=='duplicate': bad.insert(at,bad[at])
                else: bad[at]=(999,bad[at][1])
                controls+=reject(lambda:packet(bad,row,tilted,version))
        bad=[t for t in record if t[0] not in (92,160,310)] if row%2==0 else record+[(92,4),(310,PROXY)]
        controls+=reject(lambda:packet(bad,row,tilted,version))
        if row%2==0:
            at=next(i for i,t in enumerate(record) if t[0]==310)
            bad=list(record);bad[at]=(310,b'BAD!');controls+=reject(lambda:packet(bad,row,tilted,version))
            bad=list(record);bad.insert(at,bad[at]);controls+=reject(lambda:packet(bad,row,tilted,version))
        require(load_proxy_graphic(Tags(DXFTag(c,v) for c,v in record),length_code=92 if version=='AutoCad2000' else 160)
                == (None if row%2 else PROXY),'Independent proxy extraction')
    doc=ezdxf.readfile(path)
    require(doc.dxfversion==VERSIONS[version],'Independent version')
    space=doc.modelspace() if placement==0 else doc.layouts.get('PRIMITIVE_PAPER') if placement==1 else doc.blocks['PRIMITIVE_HOLDER']
    loaded=[e for e in space if e.dxf.layer.startswith('PRIMITIVE_')]
    require(len(loaded)==20,'Independent host placement/count')
    for host in loaded:
        row=int(host.dxf.layer.split('_')[1]);kind,changed,values=expected(row,tilted)
        require(host.dxftype()==kind and host.dxf.owner==space.block_record_handle,'Independent type/owner')
        first=tuple(values[c] for c in (10,20,30))
        if kind=='LINE':
            require(tuple(host.dxf.start)==first and tuple(host.dxf.end)==tuple(values[c] for c in (11,21,31)),'Independent LINE endpoints')
        elif kind=='POINT':
            require(tuple(host.dxf.location)==first and host.dxf.angle==values[50],'Independent POINT fields')
        else:
            require(tuple(host.dxf.start)==first and tuple(host.dxf.unit_vector)==tuple(values[c] for c in (11,21,31)),'Independent infinite-line fields')
        if kind in ('LINE','POINT'):
            require(host.dxf.thickness==values[39] and tuple(host.dxf.extrusion)==tuple(values[c] for c in (210,220,230)),'Independent extrusion')
        require(host.dxf.color==3 and host.dxf.invisible==1 and host.dxf.ltscale==1.25,'Independent appearance')
        require([(t.code,t.value) for t in host.get_xdata('PRIMITIVE_KEEP')]==[(1000,'opaque'),(1004,bytes((17,33,201)))],'Independent unrelated XData')
    line, = doc.modelspace().query('LINE[layer=="FOLLOWING"]')
    require(tuple(line.dxf.start)==(17.25,-4.5,2.) and tuple(line.dxf.end)==(18.5,9.25,-3.),'Following geometry')
    inserts=list(doc.modelspace().query('INSERT'))
    require(len(inserts)==(1 if placement==2 else 0),'Block instantiation policy')
    audit=doc.audit();require(not audit.errors and not audit.fixes,'Independent graph errors or repairs')
    return controls


def main(directory):
    specs=list(itertools.product(VERSIONS,(False,True),(False,True),range(4),('source','False','True')))
    def name(s):
        v,b,t,p,o=s;return f'primitive-mutation-{v}-{b}-{t}-{p}-{o}.dxf'
    wanted={name(s) for s in specs}
    def inventory(actual):require(actual==wanted,'Missing or extra primitive fixtures')
    inventory({p.name for p in directory.glob('primitive-mutation-*.dxf')})
    reject(lambda:inventory(wanted-{min(wanted)}));reject(lambda:inventory(wanted|{'primitive-mutation-extra.dxf'}))
    controls=0
    for spec in specs:
        v,b,t,p,o=spec;controls+=inspect(directory/name(spec),v,b if o=='source' else o=='True',t,p)
    print(f'PASS: {len(specs)} drawings / {20*len(specs)} independently read primitives; {controls} packet controls and two inventory controls rejected; zero graph errors/repairs. Synthetic proxy markers do not qualify native rendering.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_primitive_mutation.py ARTIFACTS')
    main(Path(sys.argv[1]))
