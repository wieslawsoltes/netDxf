#!/usr/bin/env python3
"""Check tolerance/limit strings, native settings, placement and complete fixture inventory."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_literals import load, wire
from verify_dimension_text_blocks import one, corrupt
from verify_raw_line_geometry import require, reject, key

# Derived from fixture geometry, not captured netDxf output.
MEASURE = (10*math.cos(math.pi/6), 10., 90., 10., 5., 90., 2., 5*math.pi/2)


def stack(high, low):
    return '{\\H0.5x;\\S' + high + '^ ' + low + ';}'


def expected(kind, mode):
    measure = MEASURE[kind]
    angular = kind in (2, 5)
    prefix = 'Ø' if kind == 3 else 'R' if kind == 4 else ''
    unit = '°' if angular else ''
    def label(factor, alternate):
        nominal = ('' if alternate else prefix) + f'{measure*factor:.2f}' + ('mm' if alternate else unit)
        precision = 2 if alternate else 3
        suffix = '' if alternate else unit
        high = f'{.25*factor:.{precision}f}' + suffix
        low = f'{.125*factor:.{precision}f}' + suffix
        if mode == 0: return nominal
        if mode == 1: return '{\\A1;' + nominal + '{\\H0.5x;±' + high + '}}'
        if mode == 2: return '{\\A1;' + nominal + '{\\H0.5x;\\S+' + high + '^ -' + low + ';}}'
        high = f'{(measure+.25)*factor:.{precision}f}' + suffix
        low = f'{(measure-.125)*factor:.{precision}f}' + suffix
        return ('' if alternate else prefix) + stack(high, low) + ('mm' if alternate else '')
    return label(1., False) + ('' if angular else '[' + label(2., True) + ']')


def packet(record, mode, variant):
    apps = [i for i, tag in enumerate(record) if tag == (1001, 'ACAD')]
    require(len(apps) == (0 if variant == 0 else 1), 'ACAD override presence')
    if not apps: return []
    start = apps[0] + 1
    end = next((i for i in range(start, len(record)) if record[i][0] == 1001), len(record))
    tags = record[start:end]
    require(tags[:2] == [(1000,'DSTYLE'),(1002,'{')] and tags[-1] == (1002,'}'), 'Override framing')
    require((len(tags)-3) % 2 == 0, 'Incomplete override pair')
    values = {}
    positions = []
    for i in range(2, len(tags)-1, 2):
        require(tags[i][0] == 1070 and tags[i][1] not in values, 'Duplicate or invalid identifier')
        values[tags[i][1]] = tags[i+1]; positions.append(start+i+1)
    wanted = {178:(1070,4)} if variant == 1 else {
        71:(1070,1 if mode in (1,2) else 0), 72:(1070,1 if mode == 3 else 0),
        47:(1040,.25), 48:(1040,.25 if mode==1 else .125),
        272:(1070,3),274:(1070,2),283:(1070,1),284:(1070,0),286:(1070,0)}
    require(values.keys() == wanted.keys(), 'Complete tolerance override inventory')
    require(all(key(values[k]) == key(v) for k,v in wanted.items()), 'Wrong tolerance override')
    return positions


def inspect(path, year, binary, placement, kind):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load(path, year)
    at = tags.index((9,'$ACADVER')); require(tags[at+1] == (1,PROFILES[year]), 'Version')
    entries = [tags[a:b] for a,b in records(tags)]
    hosts = [r for r in entries if r[0][1] in ('DIMENSION','ARC_DIMENSION')]
    require(len(hosts)==12 and {one(r,8)[1] for r in hosts} == {f'TOL_{m}_{v}' for m in range(4) for v in range(3)}, 'Host inventory')
    controls = 0
    for host in hosts:
        _, m, v = one(host,8)[1].split('_'); mode,variant=int(m),int(v)
        require(host[0][1] == ('ARC_DIMENSION' if kind==7 else 'DIMENSION'), 'Host type')
        name = f'TOL_STYLE_{mode}_{variant}'
        style, = [r for r in entries if r[0] == (0,'DIMSTYLE') and (2,name) in r]
        base_mode = 0 if variant==2 else mode
        controls += corrupt(style, {47:9. if variant==2 else .25,48:8. if variant==2 else .25 if mode==1 else .125,
            71:1 if base_mode in (1,2) else 0,72:1 if base_mode==3 else 0,272:1 if variant==2 else 3,
            274:1 if variant==2 else 2,146:.5})
        controls += corrupt(host,{1:'<>',3:name})
        for at in packet(host,mode,variant):
            for op in ('change','remove','duplicate','wrong-group'):
                bad=list(host); code,value=bad[at]
                if op=='change':bad[at]=(code,value+1)
                elif op=='remove':del bad[at]
                elif op=='duplicate':bad.insert(at,bad[at])
                else:bad[at]=(1000,value)
                controls += reject(lambda: packet(bad,mode,variant))
        block, = [r for r in entries if r[0] == (0,'BLOCK_RECORD') and (2,one(host,2)[1]) in r]
        handle=one(block,5)[1]
        texts=[r for r in entries if r[0] == (0,'MTEXT') and (330,handle) in r]
        require(len(texts)==1,'Label count')
        controls += corrupt(texts[0],{1:wire(expected(kind,mode),year),330:handle,40:.75})
    doc=ezdxf.readfile(path)
    require(doc.dxfversion==PROFILES[year],'Independent version')
    space=doc.modelspace() if placement==0 else doc.layouts.get('TOL_PAPER') if placement==1 else doc.blocks['TOL_HOLDER']
    loaded=list(space.query('DIMENSION ARC_DIMENSION'));require(len(loaded)==12,'Independent placement')
    for host in loaded:
        _,m,v=host.dxf.layer.split('_'); mode,variant=int(m),int(v)
        require(host.dxf.owner==space.block_record_handle,'Independent owner')
        style=doc.dimstyles.get(host.dxf.dimstyle)
        values=host.get_acad_dstyle(style)
        effective=lambda name: values.get(name,style.dxf.get(name))
        require(effective('dimtol') == (1 if mode in (1,2) else 0) and effective('dimlim') == (1 if mode==3 else 0), 'Independent mode')
        require(effective('dimtp')==.25 and effective('dimtm')==(.25 if mode==1 else .125),'Independent signed values')
        require(effective('dimtdec')==3 and effective('dimalttd')==2,'Independent precision')
        text,=host.get_geometry_block().query('MTEXT')
        require(text.text==wire(expected(kind,mode),year),'Independent generated expression')
        require(text.dxf.owner==host.get_geometry_block().block_record_handle,'Independent label owner')
        require([(t.code,t.value) for t in host.get_xdata('TOL_KEEP')]==[(1000,'untouched')],'Other application')
    line,=doc.modelspace().query('LINE')
    require(tuple(line.dxf.start)==(17.25,-4.5,2.) and tuple(line.dxf.end)==(18.5,9.25,-3.),'Following geometry')
    audit=doc.audit();require(not audit.errors and not audit.fixes,'Independent graph errors/repairs')
    return controls


def main(directory):
    specs=[s for s in itertools.product(PROFILES,(False,True),range(3),range(8),('source','False','True')) if not(s[0]==2000 and s[3]==7)]
    def name(s):
        y,b,p,k,o=s
        return f'tolerance-label-AutoCad{y}-{b}-{p}-{k}-{o}.dxf'
    wanted={name(s) for s in specs}
    def inventory(actual):require(actual==wanted,'Incomplete or extra tolerance corpus')
    inventory({p.name for p in directory.glob('tolerance-label-*.dxf')})
    reject(lambda:inventory(wanted-{min(wanted)}));reject(lambda:inventory(wanted|{'tolerance-label-extra.dxf'}))
    controls=0
    for s in specs:
        y,b,p,k,o=s
        controls+=inspect(directory/name(s),y,b if o=='source' else o=='True',p,k)
    print(f'PASS: {len(specs)} tolerance drawings / {12*len(specs)} hosts; {controls} packet mutations and two inventory controls rejected; zero graph errors/repairs. Numeric/MTEXT structure, not native font or automatic fitting, is qualified.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_tolerance_labels.py ARTIFACTS')
    main(Path(sys.argv[1]))
