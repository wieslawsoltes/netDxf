#!/usr/bin/env python3
"""Independently verify underlay OCS insertion and oriented, orthogonal page axes."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.math import OCS, Vec3
from verify_raw_line_geometry import require, reject
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_dimlfac_fidelity import PROFILES, records

PROXY = bytes((i * 31) & 255 for i in range(300))
TYPES = ('PDFUNDERLAY', 'DWFUNDERLAY', 'DGNUNDERLAY')
DEFINITIONS = ('PDFDEFINITION', 'DWFDEFINITION', 'DGNDEFINITION')
FILES = ('underlay.pdf', 'underlay.dwf', 'underlay.dgn')
CLIP = [(11, .25), (21, .5), (11, 6.), (21, .25), (11, 1.), (21, 5.)]
IDENTITY = ((1., 0., 0.), (0., 1., 0.), (0., 0., 1.))

def one(record, code):
    values = [v for c, v in record if c == code]
    require(len(values) == 1, f'Missing/duplicate UNDERLAY field {code}')
    return values[0]

def dot(a, b): return sum(x*y for x, y in zip(a, b))
def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def unit(a):
    length = math.sqrt(dot(a, a)); require(math.isfinite(length) and length > 0, 'Invalid normal')
    return tuple(v/length for v in a)
def mul(m, v): return tuple(dot(row, v) for row in m)
def transpose(m): return tuple(zip(*m))
def product(a, b): return tuple(tuple(dot(row, col) for col in transpose(b)) for row in a)

def source_axes(plane):
    # Input bases are specified independently of the netDxf arbitrary-axis helper.
    return IDENTITY if plane == 0 else ((-1.,0.,0.),(0.,0.,1.),(0.,1.,0.)) if plane == 1 else (
        (-1.,0.,0.), (0.,-.8,.6), (0.,.6,.8))

def matrix(plane, kind):
    if kind < 2: return IDENTITY
    if kind == 2: return ((2.,0.,0.),(0.,3.,0.),(0.,0.,4.))
    if kind == 3: return ((0.,1.,0.),(1.,0.,0.),(0.,0.,1.))
    if kind == 4: return ((0.,-1.,0.),(1.,0.,0.),(0.,0.,1.))
    if kind == 5: return ((-2.,0.,0.),(0.,-2.,0.),(0.,0.,-2.))
    if kind == 6:
        axes = source_axes(plane)
        return product(product(axes, ((1.,0.,0.),(0.,1.,0.),(.5,0.,1.))), transpose(axes))
    return ((1e-5,0.,0.),(0.,1e-5,0.),(0.,0.,1e-5))

def expected(plane, kind):
    angle = 0. if kind in (2, 6) else math.radians(37.)
    c, s = math.cos(angle), math.sin(angle); axes = source_axes(plane); m = matrix(plane, kind)
    position = mul(m, (5.,-3.,7.)); shift = (0.,0.,0.) if kind == 0 else (11.,-13.,17.)
    return (tuple(a+b for a,b in zip(position, shift)),
            mul(m, mul(axes, (4*c,4*s,0.))), mul(m, mul(axes, (-3*s,3*c,0.))))

def near(actual, wanted):
    actual, wanted = tuple(actual), tuple(wanted)
    scale = max(1e-280, *(abs(v) for v in wanted))
    require(len(actual) == len(wanted) and all(math.isfinite(a) and abs(a-b) <= 2e-12*scale
            for a,b in zip(actual,wanted)), 'Incorrect underlay WCS insertion or oriented page axis')

def spec(layer):
    parts = layer.split('_')
    require(len(parts) == 4 and parts[0] == 'UA' and parts[1] in ('0','1','2')
            and parts[2] in ('0','1','2') and parts[3] in tuple(map(str,range(8))), 'Unexpected underlay layer')
    return tuple(map(int,parts[1:]))

def common(record):
    starts = [i for i,t in enumerate(record) if t == (100,'AcDbEntity')]
    require(len(starts) == 1,'Common underlay subclass')
    start = starts[0]+1
    end = next((i for i in range(start,len(record)) if record[i][0] == 100),len(record))
    return record[start:end]

def stored_axes(normal):
    # Autodesk arbitrary-axis construction, separate from independent reader below.
    n = unit(normal)
    x = unit(cross((0.,1.,0.) if abs(n[0]) < 1/64 and abs(n[1]) < 1/64 else (0.,0.,1.), n))
    y = cross(n,x)
    return transpose((x,y,n))

def check_record(record, year):
    kind_type, plane, kind = spec(one(record,8))
    require(record[0] == (0,TYPES[kind_type]),'Wrong underlay kind')
    require(record.count((100,'AcDbUnderlayReference')) == 1,'Underlay subclass')
    normal = tuple(one(record,c) for c in (210,220,230))
    require(abs(dot(normal,normal)-1) <= 2e-12,'Non-unit underlay normal')
    axes = stored_axes(normal); angle = one(record,50)
    require(math.isfinite(angle) and 0 <= angle < 360,'Noncanonical rotation')
    sx, sy = one(record,41), one(record,42)
    require(math.isfinite(sx) and math.isfinite(sy) and sx > 0 and sy > 0,'Noncanonical scale')
    angle = math.radians(angle); c, s = math.cos(angle),math.sin(angle)
    actual = (mul(axes,tuple(one(record,c) for c in (10,20,30))),
              mul(axes,(sx*c,sx*s,0.)),mul(axes,(-sy*s,sy*c,0.)))
    for a,b in zip(actual,expected(plane,kind)): near(a,b)
    for code,value in ((43,1.),(280,11),(281,61),(282,17)):
        require(one(record,code) == value,'Changed underlay appearance or Z scale')
    require([t for t in record if t[0] in (11,21)] == CLIP,'Changed ordered local clipping path')
    require(isinstance(one(record,340),str) and int(one(record,340),16) > 0,'Missing definition handle')
    data = common(record); length_code = 160 if year in (2013,2018) else 92
    wanted = [] if kind != 0 else [(length_code,len(PROXY))]+[(310,PROXY[i:i+127]) for i in range(0,len(PROXY),127)]
    require([t for t in data if t[0] in (92,160,310)] == wanted,'Stale, missing or changed underlay graphics')

def corrupt(record, year):
    controls = 0
    for code in (10,20,30,41,42,43,50,210,220,230,280,281,282):
        at = next(i for i,t in enumerate(record) if t[0] == code)
        for mode in ('change','remove','duplicate'):
            bad = list(record)
            if mode == 'change': bad[at] = (code,bad[at][1]+17)
            elif mode == 'remove': del bad[at]
            else: bad.insert(at,bad[at])
            controls += reject(lambda: check_record(bad,year))
    for at,(code,value) in enumerate(record):
        if code not in (11,21): continue
        bad = list(record); bad[at] = (code,value+1)
        controls += reject(lambda: check_record(bad,year))
    if spec(one(record,8))[2] == 0:
        for at,(code,value) in enumerate(record):
            if code not in (92,160,310): continue
            for mode in ('change','remove','duplicate','wrong-code'):
                bad = list(record)
                if mode == 'change': bad[at] = (code,value+b'!' if isinstance(value,bytes) else value+1)
                elif mode == 'remove': del bad[at]
                elif mode == 'duplicate': bad.insert(at,bad[at])
                else: bad[at] = (160 if code == 92 else 92 if code == 160 else 999,value)
                controls += reject(lambda: check_record(bad,year))
    else:
        at = next(i for i,t in enumerate(record) if t == (100,'AcDbEntity'))+1
        for code in (92,160):
            bad = list(record); bad[at:at] = [(code,1),(310,b'X')]
            controls += reject(lambda: check_record(bad,year))
    return controls

def inspect(path, year, binary, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary,'Wrong underlay transport')
    tags = load_visibility_tags(path); at = tags.index((9,'$ACADVER'))
    require(tags[at+1] == (1,PROFILES[year]),'Wrong underlay profile')
    entries = [tags[a:b] for a,b in records(tags)]
    rows = [r for r in entries if r[0][1] in TYPES]
    wanted = {f'UA_{t}_{p}_{m}' for t,p,m in itertools.product(range(3),range(3),range(8))}
    require(len(rows) == 72 and {one(r,8) for r in rows} == wanted,'Physical underlay inventory')
    controls = 0
    for row in rows: check_record(row,year); controls += corrupt(row,year)
    handles = {one(r,8):one(r,5) for r in rows}
    require(len(set(handles.values())) == 72,'Duplicate underlay handles')
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year],'Independent underlay profile')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('UA_PAPER') if placement == 1 else doc.blocks['UA_HOLDER']
    loaded = list(space.query('PDFUNDERLAY DWFUNDERLAY DGNUNDERLAY'))
    require(len(loaded) == 72 and {v.dxf.layer for v in loaded} == wanted,'Independent underlay placement')
    for item in loaded:
        kind_type,plane,kind = spec(item.dxf.layer)
        ocs = OCS(item.dxf.extrusion); angle = math.radians(item.dxf.rotation); c,s = math.cos(angle),math.sin(angle)
        actual = (ocs.to_wcs(item.dxf.insert),ocs.to_wcs(Vec3(item.dxf.scale_x*c,item.dxf.scale_x*s,0)),
                  ocs.to_wcs(Vec3(-item.dxf.scale_y*s,item.dxf.scale_y*c,0)))
        for a,b in zip(actual,expected(plane,kind)): near(a,b)
        require(item.dxf.owner == space.block_record_handle and item.dxf.handle == handles[item.dxf.layer],'Independent underlay ownership/handle')
        definition = item.get_underlay_def()
        require(definition.dxftype() == DEFINITIONS[kind_type] and definition.dxf.filename == FILES[kind_type],'Independent definition target')
        require(item.dxf.flags == 11 and item.dxf.contrast == 61 and item.dxf.fade == 17,'Independent appearance')
        require([tuple(p)[:2] for p in item.boundary_path] == [(.25,.5),(6.,.25),(1.,5.)],'Independent clipping path')
        require(item.proxy_graphic == (PROXY if kind == 0 else None),'Independent proxy graphics')
        require([(t.code,t.value) for t in item.get_xdata('UNDERLAY_AFFINE_KEEP')] == [(1000,'unchanged')],'Independent underlay XData')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25,-4.5,2.) and tuple(line.dxf.end) == (18.5,9.25,-3.),'Following LINE')
    if placement >= 2: require(len(list(doc.modelspace().query('INSERT'))) == (1 if placement == 2 else 0),'Block reference policy')
    audit = doc.audit(); require(not audit.errors and not audit.fixes,'Independent database errors/repairs')
    return controls,handles

def main(directory):
    specs = list(itertools.product(PROFILES,(False,True),range(4),('source','False','True')))
    def name(s):
        year,binary,placement,output = s
        return f'underlay-affine-AutoCad{year}-{binary}-{placement}-{output}.dxf'
    wanted = {name(s) for s in specs}
    def inventory(actual): require(actual == wanted,'Missing/extra underlay fixtures')
    inventory({p.name for p in directory.glob('underlay-affine-*.dxf')})
    reject(lambda: inventory(wanted-{min(wanted)})); reject(lambda: inventory(wanted|{'underlay-affine-extra.dxf'}))
    total = 0; source_handles = {}
    for spec in specs:
        year,binary,placement,output = spec
        controls,handles = inspect(directory/name(spec),year,binary if output == 'source' else output == 'True',placement)
        total += controls; key = (year,binary,placement)
        if output == 'source': source_handles[key] = handles
        else: require(handles == source_handles[key],'Round trip changed underlay handles')
    print(f'PASS: {len(specs)} underlay drawings / {72*len(specs)} PDF/DWF/DGN records; '
          f'{total} actual-packet corruptions and two inventory controls rejected; zero database errors/repairs. '
          'Geometric/serialization checks are not native external-file rendering qualification.')

if __name__ == '__main__':
    require(len(sys.argv) == 2,'Usage: verify_underlay_affine.py ARTIFACTS')
    main(Path(sys.argv[1]))
