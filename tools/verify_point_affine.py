#!/usr/bin/env python3
"""Verify transformed POINT WCS positions, signed extrusion and projected marker axes."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import require, reject
from verify_ellipse_axis_proxies import load_visibility_tags, VERSIONS
from verify_dimlfac_fidelity import records

MODES = ("identity", "translate", "nonuniform", "negative", "shear", "mirror", "rotate", "planar")
PROXY = bytes((1, 3, 7, 255))


def dot(a, b): return sum(x*y for x, y in zip(a, b))
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def unit(a):
    size = math.sqrt(dot(a, a))
    return tuple(x/size for x in a)
def frame(z):
    x = unit(cross((0.,1.,0.) if abs(z[0]) < 1/64 and abs(z[1]) < 1/64 else (0.,0.,1.), z))
    return x, unit(cross(z, x)), z


def expected(row, tilted):
    mode, sign = MODES[row//3], row%3-1
    matrix = ((1.,0.,0.), (0.,1.,0.), (0.,0.,1.))
    if mode == "nonuniform": matrix = ((2.,0.,0.), (0.,3.,0.), (0.,0.,4.))
    elif mode == "negative": matrix = ((-2.,0.,0.), (0.,-3.,0.), (0.,0.,-4.))
    elif mode == "shear": matrix = ((1.,2.,3.), (0.,1.,.5), (0.,0.,1.))
    elif mode == "mirror": matrix = ((-1.,0.,0.), (0.,1.,0.), (0.,0.,1.))
    elif mode == "rotate": matrix = ((0.,-1.,0.), (1.,0.,0.), (0.,0.,1.))
    elif mode == "planar": matrix = ((1.,0.,0.), (0.,1.,0.), (0.,0.,0.))
    translation = (11.,-7.,13.) if mode == "translate" else (0.,0.,0.)
    old_normal = (1.,0.,0.) if tilted else (0.,0.,1.)
    position = tuple(dot(r, (1.,2.,3.))+v for r,v in zip(matrix,translation))
    extrusion = tuple(dot(r, old_normal) for r in matrix)
    length = math.sqrt(dot(extrusion,extrusion))
    normal = unit(extrusion) if length else old_normal
    thickness = sign*1.75*length
    if mode in ("identity", "translate"): angle=30.
    else:
        old_x,old_y,_ = frame(old_normal)
        marker = tuple(old_x[i]*math.cos(math.pi/6)+old_y[i]*math.sin(math.pi/6) for i in range(3))
        transformed = tuple(dot(r,marker) for r in matrix)
        x,y,_ = frame(normal)
        angle = math.degrees(math.atan2(dot(y,transformed),dot(x,transformed))) % 360.
    values = dict(zip((10,20,30),position))
    values.update(zip((210,220,230),normal))
    values.update({39:thickness,50:360.-angle,62:3,60:1})
    return values, mode=="identity", matrix, translation, old_normal


def near(a,b):
    return isinstance(a,(float,int)) and math.isfinite(a) and math.isclose(a,b,rel_tol=2e-12,abs_tol=1e-10)


def packet(record, row, tilted, version):
    values, retained, _, _, _ = expected(row,tilted)
    require(record[0]==(0,"POINT"),"Physical entity type")
    for code,value in values.items():
        found=[v for c,v in record if c==code]
        require(len(found)==1 and near(found[0],value),f"Missing/duplicate/incorrect POINT group {code}")
    proxies=[t for t in record if t[0] in (92,160,310)]
    length_code=160 if version in ("AutoCad2013","AutoCad2018") else 92
    require(proxies==([(length_code,4),(310,PROXY)] if retained else []),"Stale or damaged proxy packet")
    return values


def inspect(path, version, binary, tilted, placement):
    require(path.read_bytes().startswith(b"AutoCAD Binary DXF")==binary,"Physical transport")
    tags=load_visibility_tags(path)
    av=tags.index((9,"$ACADVER"));require(tags[av+1]==(1,VERSIONS[version]),"Physical version")
    content=[tags[a:b] for a,b in records(tags)]
    points=[r for r in content if r[0]==(0,"POINT")]
    require(len(points)==24,"Physical point count")
    require({next(v for c,v in r if c==8) for r in points}=={f"POINT_AFFINE_{i:02d}" for i in range(24)},"Point layer inventory")
    controls=0
    for record in points:
        row=int(next(v for c,v in record if c==8).split("_")[-1])
        fields=packet(record,row,tilted,version)
        for code in fields:
            at=next(i for i,t in enumerate(record) if t[0]==code)
            for op in ("change","remove","duplicate","wrong-code"):
                bad=list(record)
                if op=="change":bad[at]=(code,bad[at][1]+.5)
                elif op=="remove":del bad[at]
                elif op=="duplicate":bad.insert(at,bad[at])
                else:bad[at]=(999,bad[at][1])
                controls+=reject(lambda:packet(bad,row,tilted,version))
        bad=[t for t in record if t[0] not in (92,160,310)] if row<3 else record+[(92,4),(310,PROXY)]
        controls+=reject(lambda:packet(bad,row,tilted,version))
    doc=ezdxf.readfile(path)
    require(doc.dxfversion==VERSIONS[version],"Independent version")
    space=doc.modelspace() if placement==0 else doc.layouts.get("POINT_PAPER") if placement==1 else doc.blocks["POINT_HOLDER"]
    points=list(space.query("POINT"));require(len(points)==24,"Independent owner placement")
    for point in points:
        row=int(point.dxf.layer.split("_")[-1]);values,retained,_,_,_=expected(row,tilted)
        require(point.dxf.owner==space.block_record_handle,"Independent owner")
        for actual,codes in ((point.dxf.location,(10,20,30)),(point.dxf.extrusion,(210,220,230))):
            require(all(near(a,values[c]) for a,c in zip(actual,codes)),"Independent WCS/extrusion")
        require(near(point.dxf.thickness,values[39]) and near(point.dxf.angle,values[50]),"Independent thickness/marker")
        require([(t.code,t.value) for t in point.get_xdata("POINT_AFFINE_KEEP")]==[(1000,"unchanged")],"Other application")
    line,=doc.modelspace().query('LINE[layer=="FOLLOWING"]')
    require(tuple(line.dxf.start)==(17.25,-4.5,2.) and tuple(line.dxf.end)==(18.5,9.25,-3.),"Following geometry")
    inserts=list(doc.modelspace().query("INSERT"));require(len(inserts)==(1 if placement==2 else 0),"Block instancing policy")
    audit=doc.audit();require(not audit.errors and not audit.fixes,"Independent database errors/repairs")
    return controls


def main(directory):
    specs=list(itertools.product(VERSIONS,(False,True),range(2),range(4),("source","False","True")))
    def name(s):
        version,binary,tilted,placement,output=s
        return f"point-affine-{version}-{binary}-{tilted}-{placement}-{output}.dxf"
    wanted={name(s) for s in specs}
    def inventory(actual):require(actual==wanted,"Missing or extra POINT affine corpus")
    inventory({p.name for p in directory.glob("point-affine-*.dxf")})
    reject(lambda:inventory(wanted-{min(wanted)}))
    reject(lambda:inventory(wanted|{"point-affine-extra.dxf"}))
    controls=0
    for s in specs:
        v,b,n,p,o=s
        controls+=inspect(directory/name(s),v,b if o=="source" else o=="True",n,p)
    print(f"PASS: {len(specs)} drawings / {24*len(specs)} POINT records; {controls} actual-packet controls and two inventory controls rejected; zero graph errors/repairs. Projected marker settings are not native glyph/viewport equivalence.")

if __name__=="__main__":
    require(len(sys.argv)==2,"Usage: verify_point_affine.py ARTIFACTS")
    main(Path(sys.argv[1]))
