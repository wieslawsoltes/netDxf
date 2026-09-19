#!/usr/bin/env python3
"""Independent tangent-intersection and curve-locus checks for rational conic conversion.

Production uses angular midpoints/cosine weights. Expected controls here are
intersections of endpoint tangent lines. ezdxf independently evaluates each
loaded NURBS; its points must lie on the source conic with the intended sweep.
Geometry has a 2e-12 absolute bound for this fixed, ordinary-scale corpus.
"""
import io
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.math import OCS, Vec3
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

PROFILES = dict(zip(('AutoCad2000', 'AutoCad2004', 'AutoCad2007', 'AutoCad2010', 'AutoCad2013', 'AutoCad2018'),
                    ('AC1015', 'AC1018', 'AC1021', 'AC1024', 'AC1027', 'AC1032')))
CENTER = Vec3(7, -11, 3)
NORMALS = ((0, 0, 1), (0, 0, -1), (2, -3, 6))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def definition(kind):
    closed = kind in (0, 4)
    a, b, rotation = (4., 4., 0.) if kind < 4 else (6., 2., math.radians(31 if kind == 4 else 37))
    angles = ((0, 360), (0, 90), (270, 90), (22, 297), (0, 360), (0, 90), (270, 90), (25, 215))[kind]
    if closed:
        lo, hi = 0., math.tau
    elif kind < 4:
        lo, hi = map(math.radians, angles)
    else:
        def eccentric(angle):
            if angle % 90 == 0:
                return math.radians(angle)
            p = math.radians(angle)
            return math.atan2(a * math.sin(p), b * math.cos(p)) % math.tau
        lo, hi = map(eccentric, angles)
    if hi < lo:
        hi += math.tau
    return a, b, rotation, lo, hi, closed


def expected(kind, normal):
    a, b, rotation, lo, hi, closed = definition(kind)
    spans = math.ceil((hi - lo) / (math.pi / 2))
    frame = OCS(NORMALS[normal]); c, s = math.cos(rotation), math.sin(rotation)
    def world(x, y):
        return tuple(CENTER + frame.to_wcs((a*x*c-b*y*s, a*x*s+b*y*c, 0)))
    controls, weights = [], []
    for i in range(spans):
        t0, t1 = lo + (hi-lo)*i/spans, lo + (hi-lo)*(i+1)/spans
        c0, s0, c1, s1 = math.cos(t0), math.sin(t0), math.cos(t1), math.sin(t1)
        determinant = c0*s1-s0*c1
        if i == 0:
            controls.append(world(c0, s0)); weights.append(1.)
        controls.append(world((s1-s0)/determinant, (c0-c1)/determinant))
        weights.append(math.sqrt((1+c0*c1+s0*s1)/2))
        controls.append(world(c1, s1)); weights.append(1.)
    if closed:
        controls[-1] = controls[0]
    knots = [0.]*3 + [float(i) for i in range(1, spans) for _ in range(2)] + [float(spans)]*3
    return controls, weights, knots


def record(data):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    records, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0] == (0, 'SPLINE'):
                records.append(current)
            current = []
        current.append((tag.code, tag.value))
    require(len(records) == 1, 'Expected exactly one SPLINE')
    return records[0]


def check_record(tags, kind, normal, version):
    def values(code):
        return [v for c, v in tags if c == code]
    def numeric(code):
        return list(map(float, values(code)))
    controls, weights, knots = expected(kind, normal)
    for code, wanted in ((70, [37 if kind in (0,4) else 36]), (71, [2]), (40, knots), (60, [1]), (370, [35]), (48, [2.5])):
        require(numeric(code) == wanted, f'Incorrect scalar/count/knot group {code}')
    for code, wanted in ((8, ['PROJECTION']), (6, ['Dashed']), (1001, ['CURVE_PROJECTION']), (1000, ['retained'])):
        require(values(code) == wanted, f'Incorrect appearance group {code}')
    require(values(430) == ([] if version == 'AutoCad2000' else ['Book$Ink']), 'Color name differs')
    require(numeric(284) == ([] if version in ('AutoCad2000','AutoCad2004') else [3]), 'Shadow mode differs')
    require(numeric(420) == ([] if version == 'AutoCad2000' else [2113632]), 'True color differs')
    require(numeric(440) == ([33554687] if version == 'AutoCad2000' else [33554597]), 'Transparency differs')
    require(not values(310), 'Converted entity retained stale proxy graphics')
    payload = values(1004)
    require(len(payload)==1 and (bytes.fromhex(payload[0]) if isinstance(payload[0],str) else payload[0])==bytes((9,2,6)), 'Binary XData differs')
    for axis in range(3):
        actual = numeric(10+10*axis); wanted = [p[axis] for p in controls]
        require(len(actual) == len(wanted), 'Control count differs')
        require(all(math.isfinite(v) and abs(v-e)<=2e-12 for v,e in zip(actual,wanted)), 'Tangent-intersection control differs')
    actual = numeric(41)
    require(len(actual)==len(weights) and all(math.isfinite(v) and abs(v-e)<=8*math.ulp(1.) for v,e in zip(actual,weights)), 'Rational weight differs')


def check_curve(entity, kind, normal):
    a,b,rotation,lo,hi,closed=definition(kind);frame=OCS(NORMALS[normal]);c,s=math.cos(rotation),math.sin(rotation)
    curve=entity.construction_tool(); parameters=[]
    for i in range(65):
        point=frame.from_wcs(curve.point(curve.max_t*i/64)-CENTER)
        x,y=(point.x*c+point.y*s)/a,(point.y*c-point.x*s)/b
        require(abs(x*x+y*y-1)<=2e-12 and abs(point.z)<=2e-12,'Independent NURBS evaluation left conic')
        angle=math.atan2(y,x)
        if parameters:
            while angle < parameters[-1]-1e-12:
                angle+=math.tau
        parameters.append(angle)
    require(abs((parameters[-1]-parameters[0])-(hi-lo))<=2e-12,'NURBS traversal/sweep differs')
    require(abs(math.sin(parameters[0])-math.sin(lo))<=2e-12 and abs(math.cos(parameters[0])-math.cos(lo))<=2e-12,'NURBS start differs')
    require(bool(entity.closed)==closed,'Loaded closed flag differs')


def rejected(check,value):
    try:
        check(value)
    except ValueError:
        return 1
    raise AssertionError('Corruption escaped positive validator')


def main(directory):
    directory=Path(directory)
    names={f'conic-spline-{k}-{n}-{v}-{b}.dxf' for k in range(8) for n in range(3) for v in PROFILES for b in (False,True)}
    require({p.name for p in directory.glob('conic-spline-*')}==names,'Conic fixture inventory differs')
    mutations=0
    for kind in range(8):
        for normal in range(3):
            for version,profile in PROFILES.items():
                for binary in (False,True):
                    path=directory/f'conic-spline-{kind}-{normal}-{version}-{binary}.dxf';data=path.read_bytes()
                    require(data.startswith(b'AutoCAD Binary DXF')==binary,'Transport differs')
                    tags=record(data);check_record(tags,kind,normal,version)
                    for i,(code,value) in enumerate(tags):
                        if code not in (10,20,30,40,41,70,71,8,6,1000,430,284):continue
                        for op in ('change','drop','duplicate'):
                            changed=list(tags)
                            if op=='drop':del changed[i]
                            elif op=='duplicate':changed.insert(i,changed[i])
                            else:changed[i]=(code,'CORRUPT' if code in (8,6,1000,430) else float(value)+.125)
                            mutations+=rejected(lambda r:check_record(r,kind,normal,version),changed)
                    doc=ezdxf.readfile(path);require(doc.dxfversion==profile,'Version differs')
                    check_curve(doc.modelspace()[0],kind,normal)
                    audit=doc.audit();require(not audit.errors and not audit.fixes,'Independent graph audit requires repair')
    print(f'PASS: {len(names)} rational conic drawings / {len(names)*65} independently evaluated samples; '
          f'{mutations} packet corruptions rejected; zero graph errors/repairs')


if __name__=='__main__':
    main(sys.argv[1])
