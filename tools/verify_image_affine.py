#!/usr/bin/env python3
"""Verify IMAGE WCS pixel bases independently of netDxf's OCS conversion."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import require, reject
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_dimlfac_fidelity import PROFILES, records

PROXY = bytes((i * 17) & 255 for i in range(300))
KINDS = tuple(str(i) for i in range(7)) + ('R',)
MATRICES = (
    ((1,0,0),(0,1,0),(0,0,1)), ((1,0,0),(0,1,0),(0,0,1)),
    ((2,0,0),(0,3,0),(0,0,4)), ((0,-1,0),(1,0,0),(0,0,1)),
    ((1,.75,0),(0,1,0),(0,0,1)), ((1,0,0),(0,1,0),(.5,0,1)),
    ((-1,0,0),(0,1,0),(0,0,1)),
)

def one(record, code):
    values = [value for c, value in record if c == code]
    require(len(values) == 1, f'Missing/duplicate IMAGE field {code}')
    return values[0]

def mul(matrix, vector):
    return tuple(sum(a*b for a,b in zip(row,vector)) for row in matrix)

def ocs(plane, vector):
    x,y = vector
    # Independently specified axes for the three input planes in the fixture.
    return (x,y,0.) if plane == 0 else (-x,0.,y) if plane == 1 else (-x,-.8*y,.6*y)

def expected(plane, skew, kind):
    position = (5.,-3.,7.)
    if kind == 'R':
        return position, ocs(plane,(0.,.5)), ocs(plane,(-.5,0.) if skew == 0 else (.14,.48))
    u = ocs(plane,(.5,0.) if skew == 0 else (.3,.4))
    v = ocs(plane,(0.,.5) if skew == 0 else (.4,.3))
    m = int(kind); matrix = MATRICES[m]
    shift = (0.,0.,0.) if m == 0 else (11.,-13.,17.)
    return tuple(a+b for a,b in zip(mul(matrix,position),shift)), mul(matrix,u), mul(matrix,v)

def near_vector(actual, wanted):
    actual = tuple(actual)
    require(len(actual) == 3, 'Wrong vector length')
    scale = max(1., *(abs(v) for v in wanted))
    require(all(math.isfinite(a) and abs(a-b) <= 2e-12*scale for a,b in zip(actual,wanted)), 'Wrong IMAGE position or pixel vector')

def row_spec(layer):
    parts = layer.split('_')
    require(len(parts) == 4 and parts[0] == 'IA' and parts[1] in ('0','1','2')
            and parts[2] in ('0','1') and parts[3] in KINDS, 'Unexpected IMAGE row')
    return int(parts[1]), int(parts[2]), parts[3]

def common(record):
    starts = [i for i,t in enumerate(record) if t == (100,'AcDbEntity')]
    require(len(starts) == 1, 'Common entity subclass')
    start = starts[0]+1
    end = next((i for i in range(start,len(record)) if record[i][0] == 100),len(record))
    return record[start:end]

def check_record(record, year):
    require(record[0] == (0,'IMAGE'), 'Wrong entity')
    plane,skew,kind = row_spec(one(record,8))
    for codes,wanted in zip(((10,20,30),(11,21,31),(12,22,32)),expected(plane,skew,kind)):
        near_vector([one(record,c) for c in codes],wanted)
    for code,value in ((13,8.),(23,6.),(280,1),(281,61),(282,37),(283,9),(71,2)):
        require(one(record,code) == value, 'Image pixel dimensions or display metadata')
    data = common(record)
    length_code = 160 if year in (2013, 2018) else 92
    proxies = [t for t in data if t[0] in (92,160,310)]
    wanted = [] if kind != '0' else [(length_code,len(PROXY))] + [
        (310,PROXY[i:i+127]) for i in range(0,len(PROXY),127)]
    require(proxies == wanted, 'Common proxy presence, length, framing or bytes')

def corrupt(record, year):
    controls = 0
    codes = (10,20,30,11,21,31,12,22,32,13,23,280,281,282,283,71)
    for code in codes:
        index = next(i for i,t in enumerate(record) if t[0] == code)
        for operation in ('change','remove','duplicate'):
            bad = list(record)
            if operation == 'change': bad[index] = (code,bad[index][1]+17)
            elif operation == 'remove': del bad[index]
            else: bad.insert(index,bad[index])
            controls += reject(lambda: check_record(bad,year))
    if one(record,8).endswith('_0'):
        for at,(code,value) in enumerate(record):
            if code not in (92,160,310): continue
            for operation in ('change','remove','duplicate','wrong-code'):
                bad = list(record)
                if operation == 'change': bad[at] = (code,value+b'!' if isinstance(value,bytes) else value+1)
                elif operation == 'remove': del bad[at]
                elif operation == 'duplicate': bad.insert(at,bad[at])
                else: bad[at] = (160 if code == 92 else 92 if code == 160 else 999,value)
                controls += reject(lambda: check_record(bad,year))
    else:
        at = next(i for i,t in enumerate(record) if t == (100,'AcDbEntity'))+1
        # Both legacy and modern length codes must be detected as stale.
        for length_code in (92,160):
            bad = list(record); bad[at:at] = [(length_code,1),(310,b'X')]
            controls += reject(lambda: check_record(bad,year))
    return controls

def inspect(path, year, binary, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary,'Wrong transport')
    tags = load_visibility_tags(path); at = tags.index((9,'$ACADVER'))
    require(tags[at+1] == (1,PROFILES[year]),'Wrong profile')
    entries = [tags[a:b] for a,b in records(tags)]
    images = [r for r in entries if r[0] == (0,'IMAGE')]
    wanted_layers = {f'IA_{p}_{s}_{m}' for p,s,m in itertools.product(range(3),range(2),KINDS)}
    require(len(images) == 48 and {one(r,8) for r in images} == wanted_layers,'Physical image inventory')
    controls = 0
    for record in images:
        check_record(record,year); controls += corrupt(record,year)
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year],'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('IA_PAPER') if placement == 1 else doc.blocks['IA_HOLDER']
    loaded = list(space.query('IMAGE'))
    require(len(loaded) == 48 and {v.dxf.layer for v in loaded} == wanted_layers,'Independent image placement')
    for item in loaded:
        plane,skew,kind = row_spec(item.dxf.layer)
        for actual,wanted in zip((item.dxf.insert,item.dxf.u_pixel,item.dxf.v_pixel),expected(plane,skew,kind)):
            near_vector(actual,wanted)
        require(item.dxf.owner == space.block_record_handle,'Independent owner')
        require(tuple(item.dxf.image_size) == (8.,6.,0.),'Independent pixel count')
        definition = doc.entitydb[item.dxf.image_def_handle]
        require(definition.dxftype() == 'IMAGEDEF' and tuple(definition.dxf.image_size) == (8.,6.,0.),'Image definition reference')
        require(item.dxf.clipping == 1 and item.dxf.brightness == 61 and item.dxf.contrast == 37 and item.dxf.fade == 9,'Independent metadata')
        require(item.proxy_graphic == (PROXY if kind == '0' else None),'Independent graphics')
        require([(t.code,t.value) for t in item.get_xdata('IMAGE_AFFINE_KEEP')] == [(1000,'unchanged')],'Independent XData')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25,-4.5,2.) and tuple(line.dxf.end) == (18.5,9.25,-3.),'Following geometry')
    if placement >= 2:
        inserts = list(doc.modelspace().query('INSERT'))
        require(len(inserts) == (1 if placement == 2 else 0),'Referenced/unreferenced definition policy')
    audit = doc.audit()
    require(not audit.errors and not audit.fixes,'Independent database errors/repairs')
    return controls

def main(directory):
    specs = list(itertools.product(PROFILES,(False,True),range(4),('source','False','True')))
    def name(spec):
        year,binary,placement,output = spec
        return f'image-affine-AutoCad{year}-{binary}-{placement}-{output}.dxf'
    wanted = {name(s) for s in specs}
    def inventory(actual): require(actual == wanted,'Missing/extra IMAGE fixtures')
    inventory({p.name for p in directory.glob('image-affine-*.dxf')})
    reject(lambda: inventory(wanted-{min(wanted)}))
    reject(lambda: inventory(wanted|{'image-affine-extra.dxf'}))
    controls = 0
    for spec in specs:
        year,binary,placement,output = spec
        controls += inspect(directory/name(spec),year,binary if output == 'source' else output == 'True',placement)
    print(f'PASS: {len(specs)} IMAGE drawings / {48*len(specs)} independent IMAGE records; '
          f'{controls} actual-packet corruptions and two inventory controls rejected; zero database errors/repairs. '
          'WCS pixel geometry and stored metadata are qualified, not native raster rendering.')

if __name__ == '__main__':
    require(len(sys.argv) == 2,'Usage: verify_image_affine.py ARTIFACTS')
    main(Path(sys.argv[1]))
