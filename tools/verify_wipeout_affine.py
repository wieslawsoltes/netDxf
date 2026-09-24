#!/usr/bin/env python3
"""Independent WIPEOUT world-space clipping, raster basis and proxy verification."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import require, reject
from verify_ellipse_axis_proxies import load_visibility_tags, VERSIONS
from verify_dimlfac_fidelity import records

PROXY = bytes((i * 29) & 255 for i in range(300))
MAPS = (((1,0,0),(0,1,0),(0,0,1)), ((1,0,0),(0,1,0),(0,0,1)),
        ((2,0,0),(0,3,0),(0,0,4)), ((.6,-.8,0),(.8,.6,0),(0,0,1)),
        ((1,.5,0),(0,1,0),(0,0,1)), ((1,0,.5),(.5,1,0),(.25,.75,1)),
        ((-1,0,0),(0,1,0),(0,0,1)), ((1,0,0),(0,1,0),(0,0,0)),
        ((0,-1,0),(1,0,0),(0,0,1)))
FRAMES = (((1,0,0),(0,1,0),(0,0,1)), ((-1,0,0),(0,1,0),(0,0,-1)),
          ((-1,0,0),(0,-.8,.6),(0,.6,.8)))


def add(*vectors): return tuple(sum(v[i] for v in vectors) for i in range(3))
def scale(v, k): return tuple(x*k for x in v)
def mv(m, v): return tuple(sum(a*b for a,b in zip(row,v)) for row in m)
def corners(p): return [p[0], (p[1][0],p[0][1]), p[1], (p[0][0],p[1][1])]
def expected_affine(polygon, plane, transform):
    points = [(1,-2),(5,-2),(6,1),(3,4),(0,2)] if polygon else corners([(1,-2),(5,3)])
    x,y,n = FRAMES[plane]; t = (0,0,0) if transform == 0 else (7,-11,13)
    return [add(mv(MAPS[transform],add(scale(x,a),scale(y,b),scale(n,2.5))),t) for a,b in points]


def expected_basis(shape, basis):
    u = ((4,0,0),(0,2,1),(-.6,.8,0))[basis]
    v = ((0,2,0),(-3,1,0),(.8,.6,0))[basis]
    p = corners([(-.5,-.5),(2.5,1.5)]) if shape == 0 else [(-.5,-.5),(2.5,-.5),(2.5,1.5),(.5,2.5),(-.5,1.5)]
    return [add((3,-5,7),scale(u,x+.5),scale(v,2.5-y)) for x,y in p]


def close(a,b): return all(math.isclose(x,y,rel_tol=2e-12,abs_tol=1e-10) for x,y in zip(a,b))
def footprint(actual, wanted):
    if len(actual)>1 and close(actual[0],actual[-1]): actual=actual[:-1]
    require(len(actual)==len(wanted),'Corner count')
    n=len(wanted)
    require(any(all(close(actual[i],wanted[(start+step*i)%n]) for i in range(n))
                for start in range(n) for step in (-1,1)), 'WCS corners or cyclic edge order')


def one(tags, code):
    values=[v for c,v in tags if c==code]
    require(len(values)==1, f'Field {code} absent/duplicate')
    return values[0]


def packet(record, wanted, cache, version):
    require(record[0]==(0,'WIPEOUT'),'Record kind')
    indexes=[i for i,t in enumerate(record) if t==(100,'AcDbWipeout')]
    require(len(indexes)==1,'Wipeout subclass')
    start=indexes[0]; body=record[start+1:]
    common=record[:start]
    proxies=[t for t in common if t[0] in (92,160,310)]
    length_code=160 if version in ('AutoCad2013','AutoCad2018') else 92
    expected=[(length_code,len(PROXY))]+[(310,PROXY[i:i+127]) for i in range(0,len(PROXY),127)] if cache else []
    require(proxies==expected,'Proxy packet or presence')
    vec=lambda c:tuple(one(body,c+i) for i in (0,10,20))
    position,u,v=vec(10),vec(11),vec(12)
    height=one(body,23); require(math.isfinite(height) and height>0,'Image height')
    require(all(math.isfinite(x) for row in (position,u,v) for x in row),'Nonfinite raster basis')
    count=one(body,91); typ=one(body,71)
    xs=[x for c,x in body if c==14];ys=[y for c,y in body if c==24]
    require(len(xs)==len(ys)==count and typ in (1,2),'Clip topology/pair count')
    pairs=list(zip(xs,ys))
    if typ==1:
        require(count==2,'Rectangle needs two corners');pairs=corners(pairs)
    else: require(count>=3,'Polygon needs three corners')
    footprint([add(position,scale(u,x+.5),scale(v,height-.5-y)) for x,y in pairs],wanted)
    return [i for i in range(start+1,len(record)) if record[i][0] in (10,20,30,11,21,31,12,22,32,23,71,91,14,24)]


def inspect(path, version, binary, polygon=None, placement=None, shape=None, basis=None):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport')
    tags=load_visibility_tags(path); av=tags.index((9,'$ACADVER'))
    require(tags[av+1]==(1,VERSIONS[version]),'Physical version')
    entries=[tags[a:b] for a,b in records(tags)]
    hosts=[r for r in entries if r[0]==(0,'WIPEOUT')]
    require(len(hosts)==(27 if shape is None else 1),'Physical wipeout inventory')
    controls=0
    for host in hosts:
        if shape is None:
            _,p,m=one(host,8).split('_');p,m=int(p),int(m)
            wanted=expected_affine(polygon,p,m);cache=m==0
        else: wanted=expected_basis(shape,basis);cache=True
        positions=packet(host,wanted,cache,version)
        # Structural mutations reject independently of the geometric tolerance.
        for at in positions:
            for operation in ('remove','duplicate'):
                bad=list(host)
                if operation=='remove': del bad[at]
                else: bad.insert(at,bad[at])
                controls+=reject(lambda:packet(bad,wanted,cache,version))
        # Translation necessarily changes every world corner, including degenerate components.
        at=next(i for i in positions if host[i][0]==10);bad=list(host);bad[at]=(10,bad[at][1]+1)
        controls+=reject(lambda:packet(bad,wanted,cache,version))
        bad=list(host);bad.insert(next(i for i,t in enumerate(bad) if t==(100,'AcDbWipeout')),(310,b'BAD'))
        controls+=reject(lambda:packet(bad,wanted,cache,version))
    doc=ezdxf.readfile(path);require(doc.dxfversion==VERSIONS[version],'Independent version')
    space=doc.modelspace() if placement in (None,0) else doc.layouts.get('WA_PAPER') if placement==1 else doc.blocks['WA_HOLDER']
    loaded=list(space.query('WIPEOUT'));require(len(loaded)==len(hosts),'Independent count/placement')
    if shape is None: require({w.dxf.layer for w in loaded}=={f'WA_{p}_{m}' for p in range(3) for m in range(9)},'Row identities')
    for w in loaded:
        if shape is None:
            _,p,m=w.dxf.layer.split('_');p,m=int(p),int(m);wanted=expected_affine(polygon,p,m);cache=m==0
            require(w.dxf.color==4 and [(t.code,t.value) for t in w.get_xdata('WIPEOUT_AFFINE_KEEP')]==[(1000,'unchanged')],'Appearance/XData')
        else: wanted=expected_basis(shape,basis);cache=True
        footprint([tuple(p) for p in w.boundary_path_wcs()],wanted)
        require(w.proxy_graphic==(PROXY if cache else None),'Independent cache')
        require(w.dxf.owner==space.block_record_handle,'Independent owner')
    if shape is None:
        line,=doc.modelspace().query('LINE')
        require(tuple(line.dxf.start)==(17.25,-4.5,2) and tuple(line.dxf.end)==(18.5,9.25,-3),'Following LINE')
    audit=doc.audit();require(not audit.errors and not audit.fixes,'Independent graph errors/repairs')
    return controls


def main(directory):
    versions=list(VERSIONS)
    affine=list(itertools.product(versions,(False,True),(False,True),range(4),('source','False','True')))
    bases=list(itertools.product(versions,(False,True),range(3),range(3),('source','False','True')))
    def aname(s):v,b,p,l,o=s;return f'wipeout-affine-{v}-{b}-{p}-{l}-{o}.dxf'
    def bname(s):v,b,sh,ba,o=s;return f'wipeout-basis-{v}-{b}-{sh}-{ba}-{o}.dxf'
    expected={aname(s) for s in affine}|{bname(s) for s in bases}
    def inventory(actual):require(actual==expected,f'Wipeout inventory missing={len(expected-actual)},extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('wipeout-*.dxf')})
    reject(lambda:inventory(expected-{min(expected)}));reject(lambda:inventory(expected|{'wipeout-extra.dxf'}))
    controls=0
    for s in affine:
        v,b,p,l,o=s;controls+=inspect(directory/aname(s),v,b if o=='source' else o=='True',polygon=p,placement=l)
    for s in bases:
        v,b,sh,ba,o=s;controls+=inspect(directory/bname(s),v,b if o=='source' else o=='True',shape=sh,basis=ba)
    print(f'PASS: {len(expected)} WIPEOUT drawings / {27*len(affine)+len(bases)} independent records; '
          f'{controls} actual packet corruptions and two inventory controls rejected; zero graph errors/repairs. '
          'Clipping geometry is qualified, not native draw-order or masking appearance.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_wipeout_affine.py ARTIFACTS')
    main(Path(sys.argv[1]))
