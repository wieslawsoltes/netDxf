#!/usr/bin/env python3
"""Validate regenerated alternate labels and effective settings with an independent reader."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_literals import wire, load
from verify_dimension_text_blocks import one, corrupt

PRIMARY = ('8.6603','10.0000','90°','Ø10.0000','R5.0000','90°','2.0000','7.8540')
DEFAULT = ('219.97','254.00','','254.00','127.00','','50.80','199.49')
OVERRIDE = ('17.321','20.000','','20.000','10.000','','4.000','15.708')


def label(kind, variant):
    if kind in (2,5) or variant == 1: return PRIMARY[kind]
    return PRIMARY[kind] + (f'[ALT:{OVERRIDE[kind]}u]' if variant == 2 else f'[{DEFAULT[kind]}mm]')


def inspect(path, year, binary, kind, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags=load(path, year); at=tags.index((9,'$ACADVER'))
    require(tags[at+1] == (1,PROFILES[year]), 'Wrong version')
    entries=[tags[a:b] for a,b in records(tags)]
    hosts=[r for r in entries if r[0][1] in ('DIMENSION','ARC_DIMENSION')]
    require(len(hosts)==4 and {one(r,8)[1] for r in hosts}=={f'ALT_LABEL_{v}' for v in range(4)},'Host inventory')
    controls=0
    for host in hosts:
        variant=int(one(host,8)[1][-1])
        require(host[0][1] == ('ARC_DIMENSION' if kind == 7 else 'DIMENSION'),'Host family')
        style,=[r for r in entries if r[0]==(0,'DIMSTYLE') and (2,f'ALT_LABEL_{variant}') in r]
        controls+=corrupt(style,{170:0 if variant==2 else 1,171:2,143:25.4,4:'mm'})
        block,=[r for r in entries if r[0]==(0,'BLOCK_RECORD') and (2,one(host,2)[1]) in r]
        handle=one(block,5)[1]
        texts=[r for r in entries if r[0]==(0,'MTEXT') and (330,handle) in r]
        def inventory(values):require(len(values)==1,'Missing/extra generated label')
        inventory(texts);controls+=reject(lambda:inventory([]));controls+=reject(lambda:inventory(texts+texts))
        controls+=corrupt(texts[0],{1:wire(label(kind,variant),year),330:handle})
    doc=ezdxf.readfile(path)
    require(doc.dxfversion==PROFILES[year],'Independent version')
    space=doc.modelspace() if placement==0 else doc.layouts.get('ALT_PAPER') if placement==1 else doc.blocks['ALT_HOLDER']
    dimensions=list(space.query('DIMENSION ARC_DIMENSION'));require(len(dimensions)==4,'Independent placement/count')
    for dim in dimensions:
        v=int(dim.dxf.layer[-1]);style=doc.dimstyles.get(dim.dxf.dimstyle)
        require(dim.dxf.owner==space.block_record_handle,'Owner')
        require(style.dxf.dimalt==(0 if v==2 else 1) and style.dxf.dimaltf==25.4 and style.dxf.dimaltd==2,'Base alternate settings')
        expected={'dimalt':1,'dimaltf':2.,'dimaltd':3,'dimapost':'ALT:[]u'} if v==2 else {'dimclrt':3} if v==3 else {'dimalt':0} if v==1 else {}
        require(dim.get_acad_dstyle(style)==expected,'Effective override settings')
        text,=dim.get_geometry_block().query('MTEXT')
        require(text.text==wire(label(kind,v),year),'Independent numeric alternate label')
        require(dim.dxf.text=='<>','Stored measurement placeholder')
    audit=doc.audit();require(not audit.errors and not audit.fixes,'Graph errors or repairs')
    return controls


def main(directory):
    specs=[s for s in itertools.product(PROFILES,(False,True),range(8),range(3),('source','False','True')) if not(s[0]==2000 and s[2]==7)]
    def name(s):
        year,binary,kind,placement,output=s
        return f'alternate-label-AutoCad{year}-{binary}-{kind}-{placement}-{output}.dxf'
    expected={name(s) for s in specs}
    def inventory(actual):require(actual==expected,'Incomplete/extra alternate-label drawings')
    inventory({p.name for p in directory.glob('alternate-label-*.dxf')})
    reject(lambda:inventory(expected-{min(expected)}));reject(lambda:inventory(expected|{'alternate-label-extra.dxf'}))
    controls=0
    for spec in specs:
        year,binary,kind,placement,output=spec
        controls+=inspect(directory/name(spec),year,binary if output=='source' else output=='True',kind,placement)
    print(f'PASS: {len(specs)} alternate-label drawings / {4*len(specs)} independently loaded dimensions; {controls} packet/label corruptions and two inventory controls rejected; no graph errors/repairs. Numeric strings are checked, not native fit/font/visual equivalence.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_alternate_dimension_labels.py ARTIFACTS')
    main(Path(sys.argv[1]))
