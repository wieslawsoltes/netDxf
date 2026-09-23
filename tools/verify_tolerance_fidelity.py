#!/usr/bin/env python3
"""Check exact DIMTM/DIMTP and sparse tolerance overrides with an independent reader."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject
from verify_dimlfac_fidelity import PROFILES, records

LOWERS = (0., -0., float.fromhex('0x0.0000000000001p-1022'), -float.fromhex('0x0.0000000000001p-1022'),
          1e-300, -1e-300, 1e-13, -1e-13, 1., math.nextafter(1., 2.))


def selected(row, field):
    return 1 <= row <= 9 if field == 0 else row in (2,4,5,6,7,8,9,11) if field == 1 else row in (3,4,5,6,7,8,9,11)


def bound(row, upper):
    return {2:(.25,.25),3:(.125,.125),4:(1.,math.nextafter(1.,2.)),5:(1e-13,0.),6:(0.,-0.),
            7:(.5,.6),8:(-.125,-.25),9:(.125,.125),11:(1e-300,-1e-300)}.get(row,(.125,.25))[0 if upper else 1]


def same(actual, expected):
    return type(actual) is type(expected) and key((0, actual)) == key((0, expected))


def fields(record, expected):
    for code, wanted in expected.items():
        actual = [v for c,v in record if c == code]
        require(len(actual) == 1 and same(actual[0], wanted), f'Incorrect/missing/duplicate group {code}')


def header(tags, name, code, wanted):
    at = [i for i,t in enumerate(tags) if t == (9,name)]
    require(len(at) == 1, 'Header identity')
    start = at[0] + 1
    end = next(i for i in range(start, len(tags)) if tags[i][0] in (0,9))
    require(end == start + 1 and key(tags[start]) == key((code,wanted)), 'Exact header value/type')


def validate_header(tags, index):
    header(tags, '$DIMTP', 40, 1.)
    header(tags, '$DIMTM', 40, LOWERS[index])
    header(tags, '$DIMTOL', 70, 1)
    header(tags, '$DIMLIM', 70, 0)
    styles = [tags[a:b] for a,b in records(tags) if tags[a] == (0,'DIMSTYLE') and (2,'TOL_HEADER') in tags[a:b]]
    require(len(styles) == 1, 'Header DIMSTYLE inventory')
    fields(styles[0], {47:1.,48:LOWERS[index],71:1,72:0})


def expected_overrides(row):
    result = {43:(1040,11.),140:(1040,.75)}
    if selected(row,0): result.update({71:(1070,0 if row in (7,8) else 1),72:(1070,1 if row == 8 else 0)})
    if selected(row,1): result[47] = (1040,bound(row,True))
    if selected(row,2): result[48] = (1040,bound(row,False))
    return result


def packet(record, row):
    app = [i for i,t in enumerate(record) if t == (1001,'ACAD')]
    require(len(app) == 1, 'One ACAD application')
    start = app[0]+1;end = next((i for i in range(start,len(record)) if record[i][0] == 1001), len(record))
    body = record[start:end]
    require(body[:3] == [(1000,'NEIGHBOR'),(1000,'DSTYLE'),(1002,'{')] and body[-2:] == [(1002,'}'),(1000,'TAIL')], 'Opaque neighbor preservation')
    pairs = body[3:-2];require(len(pairs)%2 == 0, 'Complete DSTYLE pairs')
    values = {}
    for i in range(0,len(pairs),2):
        require(pairs[i][0] == 1070 and pairs[i][1] not in values, 'Unique integer identifier')
        values[pairs[i][1]] = pairs[i+1]
    expected = expected_overrides(row)
    require(values.keys() == expected.keys(), 'Sparse fields unexpectedly added or removed')
    require(all(key(values[c]) == key(v) for c,v in expected.items()), 'Exact sparse bound/flag/neighbor values')
    return range(start,end)


def main(directory):
    header_specs = list(itertools.product(PROFILES,(False,True),range(len(LOWERS)),('source','again')))
    wire_specs = list(itertools.product(PROFILES,(False,True),(False,True),range(4),('source','False','True')))
    def hn(s):
        y,b,i,p=s;return f'tolerance-fidelity-header-AutoCad{y}-{b}-{i}-{p}.dxf'
    def wn(s):
        y,b,l,p,o=s;return f'tolerance-fidelity-wire-AutoCad{y}-{b}-{l}-{p}-{o}.dxf'
    names = {hn(s) for s in header_specs} | {wn(s) for s in wire_specs}
    def inventory(actual): require(actual == names, 'Missing or extra tolerance drawings')
    inventory({p.name for p in directory.glob('tolerance-fidelity-*.dxf')})
    reject(lambda:inventory(names-{min(names)}));reject(lambda:inventory(names|{'tolerance-fidelity-extra.dxf'}))
    controls=0
    for spec in header_specs:
        year,binary,index,_=spec;path=directory/hn(spec);tags=load_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary,'Transport')
        header(tags,'$ACADVER',1,PROFILES[year]);validate_header(tags,index)
        for name in ('$DIMTP','$DIMTM','$DIMTOL','$DIMLIM'):
            at=tags.index((9,name))+1
            bad=list(tags);c,v=bad[at];bad[at]=(c,1. if v==0 else 0.) if c==40 else (c,1-v)
            for damage in (bad,tags[:at]+tags[at+1:],tags[:at]+[tags[at]]+tags[at:]):
                controls+=reject(lambda:validate_header(damage,index))
        doc=ezdxf.readfile(path);style=doc.dimstyles.get('TOL_HEADER')
        require(same(doc.header['$DIMTM'], LOWERS[index]) and same(style.dxf.dimtm,LOWERS[index]),'Independent lower bits')
        require(same(doc.header['$DIMTP'],1.) and same(style.dxf.dimtp,1.),'Independent upper bits')
        require((style.dxf.dimtol,style.dxf.dimlim) == (1,0),'Independent style flags')
        audit=doc.audit();require(not audit.errors and not audit.fixes,'Independent header graph')
    for spec in wire_specs:
        year,binary,leader,placement,output=spec;path=directory/wn(spec);tags=load_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == (binary if output=='source' else output=='True'),'Transport')
        header(tags,'$ACADVER',1,PROFILES[year])
        entries=[tags[a:b] for a,b in records(tags)]
        styles=[r for r in entries if r[0] == (0,'DIMSTYLE') and (2,'TOL_BASE') in r]
        require(len(styles)==1,'One base style');fields(styles[0],{47:.125,48:.25,71:1,72:0})
        hosts=[r for r in entries if r[0][1] in ('DIMENSION','LEADER')]
        require(len(hosts)==12 and {v for r in hosts for c,v in r if c==8} == {f'TOL_ROW_{n:02d}' for n in range(12)},'Host inventory')
        for host in hosts:
            row,=[int(v.rsplit('_',1)[1]) for c,v in host if c==8]
            for at in packet(host,row):
                c,v=host[at]
                bad=list(host);bad[at]=(c,v+'_bad' if isinstance(v,str) else 1 if v==0 else 0)
                for damage in (bad,host[:at]+host[at+1:],host[:at]+[host[at]]+host[at:]):
                    controls+=reject(lambda:packet(damage,row))
        doc=ezdxf.readfile(path)
        space=doc.modelspace() if placement==0 else doc.layouts.get('TOL_PAPER') if placement==1 else doc.blocks['TOL_HOLDER']
        actual=list(space.query('DIMENSION LEADER'));require(len(actual)==12,'Independent host count')
        for entity in actual:
            row=int(entity.dxf.layer.rsplit('_',1)[1]);style=doc.dimstyles.get(entity.dxf.dimstyle)
            overrides=entity.get_acad_dstyle(style)
            expected={'dimdli':11.,'dimtxt':.75}
            for code,name in ((71,'dimtol'),(72,'dimlim'),(47,'dimtp'),(48,'dimtm')):
                packet_values=expected_overrides(row)
                if code in packet_values:expected[name]=packet_values[code][1]
            require(overrides.keys()==expected.keys() and all(same(overrides[k],v) for k,v in expected.items()),'Independent sparse values/names')
            require(entity.dxf.owner==space.block_record_handle and entity.dxftype()==('LEADER' if leader else 'DIMENSION'),'Independent family/ownership')
            require([(t.code,t.value) for t in entity.get_xdata('TOL_KEEP')] == [(1000,'unchanged')],'Other application')
            if not leader:
                text,=entity.get_geometry_block().query('MTEXT');require(text.text=='FIXED','Fixed label changed')
        audit=doc.audit();require(not audit.errors and not audit.fixes,'Independent wire graph')
    print(f'PASS: {len(names)} drawings / {len(wire_specs)*12} sparse dimension/leader records; '
          f'{controls} packet corruptions and two inventory controls rejected; no graph errors or repairs.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_tolerance_fidelity.py ARTIFACTS')
    main(Path(sys.argv[1]))
