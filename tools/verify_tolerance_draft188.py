#!/usr/bin/env python3
"""Check dimension tolerance strings, independent MTEXT parsing, physical flags and graphs."""
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.tools.text import MTextParser, TokenType
from ezdxf.lldxf.encoding import decode_dxf_unicode
from verify_raw_line_geometry import require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_literals import wire, load
from verify_dimension_text_blocks import one, corrupt

NOMINAL = ('8.6603','10.0000','90°','10.0000','5.0000','90°','2.0000','7.8540')
UPPER = ('8.79','10.12','90.12°','10.12','5.12','90.12°','2.12','7.98')
LOWER = ('8.41','9.75','89.75°','9.75','4.75','89.75°','1.75','7.60')


def expected(kind, row):
    prefix = 'Ø' if kind == 3 else 'R' if kind == 4 else ''
    nominal = NOMINAL[kind];symbol = '°' if kind in (2,5) else ''
    if row in (0,5): return prefix + nominal
    if row == 3: return prefix + r'{\H0.5x;\S' + UPPER[kind] + '^ ' + LOWER[kind] + ';}'
    content = '±0.12'+symbol if row == 1 else r'\S+0.12'+symbol+'^ -0.25'+symbol+';'
    return r'{\A1;' + prefix + nominal + r'{\H0.5x;' + content + '}}'


def parse(label, stack_values):
    tokens = list(MTextParser(decode_dxf_unicode(label) + '|AFTER'))
    stacks = [t for t in tokens if t.type == TokenType.STACK]
    require([t.data for t in stacks] == stack_values, 'Independent stacked numerator/denominator/type')
    require(all(t.ctx.cap_height == .5 for t in stacks), 'Tolerance height scope')
    require(tokens[-1].ctx.cap_height == 1 and int(tokens[-1].ctx.align) == 0, 'Tolerance scope leaked into following text')


def inspect(path, year, binary, kind, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load(path,year);at = tags.index((9,'$ACADVER'));require(tags[at+1] == (1,PROFILES[year]),'Physical version')
    entries = [tags[a:b] for a,b in records(tags)]
    hosts = [r for r in entries if r[0][1] in ('DIMENSION','ARC_DIMENSION')]
    require(len(hosts) == 6 and {one(r,8)[1] for r in hosts} == {f'TOL_LABEL_{r}' for r in range(6)},'Physical host inventory')
    controls = 0
    for host in hosts:
        row=int(one(host,8)[1].rsplit('_',1)[1]);require(host[0][1] == ('ARC_DIMENSION' if kind==7 else 'DIMENSION'),'Dimension family')
        style,=[r for r in entries if r[0] == (0,'DIMSTYLE') and (2,f'TOL_LABEL_STYLE_{row}') in r]
        controls += corrupt(style,{47:.125,48:.125 if row==1 else .25,71:1 if row in (1,2,5) else 0,72:1 if row==3 else 0,272:2,146:.5})
        block,=[r for r in entries if r[0] == (0,'BLOCK_RECORD') and (2,one(host,2)[1]) in r];handle=one(block,5)[1]
        text,=[r for r in entries if r[0] == (0,'MTEXT') and (330,handle) in r]
        controls += corrupt(text,{1:wire(expected(kind,row),year),330:handle})
    doc = ezdxf.readfile(path)
    space = doc.modelspace() if placement==0 else doc.layouts.get('TOL_LABEL_PAPER') if placement==1 else doc.blocks['TOL_LABEL_HOLDER']
    dimensions = list(space.query('DIMENSION ARC_DIMENSION'));require(len(dimensions)==6,'Independent host inventory')
    for dim in dimensions:
        row=int(dim.dxf.layer.rsplit('_',1)[1]);style=doc.dimstyles.get(dim.dxf.dimstyle)
        require(dim.dxf.owner == space.block_record_handle and dim.dxf.text == '<>','Owner and placeholder')
        overrides = {'dimclrt':3} if row>=2 else {}
        if row in (4,5):overrides.update({'dimtol':1 if row==4 else 0,'dimlim':0})
        require(dim.get_acad_dstyle(style)==overrides,'Effective and inherited settings')
        text,=dim.get_geometry_block().query('MTEXT');label=text.text
        require(label==wire(expected(kind,row),year),'Independent complete label')
        symbol='°' if kind in (2,5) else ''
        stacks=[(UPPER[kind],LOWER[kind],'^')] if row==3 else [('+0.12'+symbol,'-0.25'+symbol,'^')] if row in (2,4) else []
        parse(label,stacks)
    audit=doc.audit();require(not audit.errors and not audit.fixes,'Independent graph errors/repairs')
    return controls


def main(directory):
    specs = [s for s in itertools.product(PROFILES,(False,True),range(8),range(4),('source','False','True')) if not(s[0]==2000 and s[2]==7)]
    def name(s):
        y,b,k,p,o=s;return f'draft188-tolerance-label-AutoCad{y}-{b}-{k}-{p}-{o}.dxf'
    names={name(s) for s in specs}
    reports={f'draft188-tolerance-label-linear-{i}.txt' for i in range(6)} | {f'draft188-tolerance-label-angular-{i}.txt' for i in range(5)} | {'draft188-tolerance-label-alternate.txt'}
    def inventory(actual):require(actual==names|reports,'Missing or extra tolerance label fixtures')
    inventory({p.name for p in directory.glob('draft188-tolerance-label-*')})
    reject(lambda:inventory(names|reports-{min(reports)}));reject(lambda:inventory(names|reports|{'draft188-tolerance-label-extra.dxf'}))
    controls=0
    for spec in specs:
        y,b,k,p,o=spec;controls+=inspect(directory/name(spec),y,b if o=='source' else o=='True',k,p)
    upper=('1.25E+00','1.25','1.25"','1 1/4"','1 1/4','1,25')
    lower=('0.50E+00','0.50','0.50"','0 1/2"','0 1/2','0,50')
    lower=('5.00E-01',)+lower[1:]
    for i in range(6):
        label=(directory/f'draft188-tolerance-label-linear-{i}.txt').read_text()
        def check(value):
            tokens=list(MTextParser(value+'|AFTER'));stacks=[t for t in tokens if t.type==TokenType.STACK]
            require(stacks and stacks[-1].data==('+'+upper[i],'-'+lower[i],'^') and stacks[-1].ctx.cap_height==.5,'Parsed number-format tolerance')
            require(tokens[-1].ctx.cap_height==1 and int(tokens[-1].ctx.align)==0,'Number-format scope leaked')
        check(label);controls+=reject(lambda:check(label.replace('^ ', '/ ',1 if i not in (3,4) else 99)))
    for i,symbol in enumerate(('°',None,'g','r','°')):
        label=(directory/f'draft188-tolerance-label-angular-{i}.txt').read_text()
        top='+0°7\'30"' if i==1 else '+0.125'+symbol
        bottom='-0°15\'0"' if i==1 else '-0.250'+symbol
        parse(label,[(top,bottom,'^')]);controls+=reject(lambda:parse(label.replace('^ ','/ '),[(top,bottom,'^')]))
    label=(directory/'draft188-tolerance-label-alternate.txt').read_text()
    parse(label,[('+0.12','-0.25','^'),('+0.250','-0.500','^')])
    require('20.0000' in label and '40.000' in label and '[{\\A1;ALT:40.000u' in label,'Alternate measurement/affixes')
    controls+=reject(lambda:parse(label.replace('+0.250','+0.500'),[('+0.12','-0.25','^'),('+0.250','-0.500','^')]))
    print(f'PASS: {len(specs)} drawings / {len(specs)*6} dimensions and 12 numerical reports; '
          f'{controls} packet/label corruptions and two inventory controls rejected; independent MTEXT parsing and graphs pass.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_tolerance_labels.py ARTIFACTS')
    main(Path(sys.argv[1]))
