#!/usr/bin/env python3
"""Verify stored UNDERLAY scale bits independently; not native rendering acceptance."""
import itertools
import math
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.math import OCS
from verify_underlay_affine import PROXY, TYPES, DEFINITIONS, FILES, CLIP, one, common
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_dimlfac_fidelity import PROFILES, records
from verify_raw_line_geometry import require, reject

VARIANTS = 19

def bits(value): return struct.pack('>d', value)
def scalar(hexadecimal): return struct.unpack('>d', bytes.fromhex(hexadecimal))[0]

def values(variant):
    if variant < 8:
        return tuple((-1 if variant & (1 << i) else 1) * n for i, n in enumerate((2., 3., 4.)))
    return {
        8: (1., 1., 1.), 9: (1e-100, -1e-100, -1e-200),
        10: (scalar('0000000000000001'), scalar('8000000000000001'), scalar('0000000000000001')),
        11: (scalar('7fefffffffffffff'), scalar('ffefffffffffffff'), scalar('ffefffffffffffff')),
        12: (0., -0., 0.), 13: (-0., 0., -0.),
        14: (scalar('0010000000000000'), scalar('8010000000000000'), scalar('8010000000000000')),
        15: (scalar('3ff0000000000001'), scalar('3fefffffffffffff'), -.125),
        16: (1., -3., -4.), 17: (-2., 1., 4.), 18: (-2., -3., 1.),
    }[variant]

def omitted(variant, code):
    return variant == 8 or (variant, code) in ((16, 41), (17, 42), (18, 43))

def spec(layer):
    parts = layer.split('_')
    require(len(parts) == 4 and parts[0] == 'US', 'Wrong scalar fixture layer')
    t, p, v = map(int, parts[1:])
    require(t in range(3) and p in range(3) and v in range(VARIANTS)
            and layer == f'US_{t}_{p}_{v:02}', 'Wrong scalar fixture identity')
    return t, p, v

def near(actual, expected):
    require(len(actual) == len(expected) and all(math.isfinite(a) and abs(a-b) <= 2e-12*max(1,abs(b))
            for a,b in zip(actual,expected)), 'Changed underlay insertion/orientation')

def check_record(record, year, source):
    t,p,v = spec(one(record,8)); require(record[0] == (0,TYPES[t]), 'Wrong underlay type')
    require(record.count((100,'AcDbUnderlayReference')) == 1, 'Underlay subclass inventory')
    for code, value in zip((41,42,43),values(v)):
        present = [a for c,a in record if c == code]
        if source and omitted(v,code): require(not present, 'Missing-field fixture unexpectedly materialized')
        else: require(len(present) == 1 and bits(present[0]) == bits(value), f'Changed/missing/duplicate scale bits: {code}')
    near(tuple(one(record,c) for c in (10,20,30)), ((5.,-3.,7.),(-5.,7.,-3.),(-5.,6.6,3.8))[p])
    near(tuple(one(record,c) for c in (210,220,230)), ((0.,0.,1.),(0.,1.,0.),(0.,.6,.8))[p])
    require(one(record,50) == 37., 'Changed scalar-fixture rotation')
    for code,wanted in ((280,11),(281,61),(282,17)): require(one(record,code) == wanted,'Changed appearance')
    require([tag for tag in record if tag[0] in (11,21)] == CLIP,'Changed clipping order/values')
    require(int(one(record,340),16) > 0 and int(one(record,5),16) > 0,'Missing identity/definition')
    size_code = 160 if year in (2013,2018) else 92
    require([tag for tag in common(record) if tag[0] in (92,160,310)] ==
            [(size_code,len(PROXY))]+[(310,PROXY[i:i+127]) for i in range(0,len(PROXY),127)],'Changed proxy packet')
    for name,wanted in (('UNDERLAY_AFFINE_KEEP',[(1000,'unchanged')]),
                        ('UNDERLAY_SCALE_DATA',[(1040,-9.25),(1041,123.5),(1042,-17.25)])):
        starts = [i for i,tag in enumerate(record) if tag == (1001,name)]
        require(len(starts) == 1, 'XData registry inventory')
        a = starts[0]+1; b = next((i for i in range(a,len(record)) if record[i][0] == 1001),len(record))
        require(record[a:b] == wanted, 'Changed unrelated XData')

def corrupt(record, year, source):
    count = 0; v = spec(one(record,8))[2]
    for code in (41,42,43):
        indices = [i for i,tag in enumerate(record) if tag[0] == code]
        if not indices:
            bad = list(record); bad.insert(record.index((100,'AcDbUnderlayReference'))+1,(code,1.))
            count += reject(lambda: check_record(bad,year,source)); continue
        at = indices[0]; value = record[at][1]
        for replacement in (-value if value != 1. else -1., 1., 17., math.inf):
            if bits(replacement) == bits(value): continue
            bad = list(record); bad[at] = (code,replacement)
            count += reject(lambda: check_record(bad,year,source))
        for bad in (record[:at]+record[at+1:],record[:at]+[record[at]]+record[at:]):
            count += reject(lambda: check_record(bad,year,source))
    for code in (10,20,30,50,210,220,230,280,281,282,1040,1041,1042):
        at = next(i for i,tag in enumerate(record) if tag[0] == code)
        bad = list(record); bad[at] = (code,bad[at][1]+7)
        count += reject(lambda: check_record(bad,year,source))
    bad = [tag for tag in record if tag[0] != 310]
    count += reject(lambda: check_record(bad,year,source))
    return count

def inspect(path, year, binary, placement, source):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary,'Changed scalar transport')
    tags = load_visibility_tags(path); at = tags.index((9,'$ACADVER'))
    require(tags[at+1] == (1,PROFILES[year]),'Changed scalar version')
    rows = [tags[a:b] for a,b in records(tags) if tags[a][1] in TYPES]
    wanted = {f'US_{t}_{p}_{v:02}' for t,p,v in itertools.product(range(3),range(3),range(VARIANTS))}
    require(len(rows) == len(wanted) and {one(r,8) for r in rows} == wanted,'Missing/extra scalar records')
    count = 0
    for row in rows: check_record(row,year,source); count += corrupt(row,year,source)
    handles = {one(r,8):one(r,5) for r in rows}; require(len(set(handles.values())) == len(wanted),'Duplicate handles')
    doc = ezdxf.readfile(path)
    space = doc.modelspace() if placement == 0 else doc.layouts.get('US_PAPER') if placement == 1 else doc.blocks['US_HOLDER']
    items = list(space.query('PDFUNDERLAY DWFUNDERLAY DGNUNDERLAY'))
    require(len(items) == len(wanted) and {i.dxf.layer for i in items} == wanted,'Independent placement/inventory')
    for item in items:
        t,p,v = spec(item.dxf.layer)
        require(tuple(bits(a) for a in (item.dxf.scale_x,item.dxf.scale_y,item.dxf.scale_z)) ==
                tuple(bits(a) for a in values(v)), 'Independent scalar bits differ')
        near(tuple(OCS(item.dxf.extrusion).to_wcs(item.dxf.insert)), (5.,-3.,7.))
        require(item.dxf.handle == handles[item.dxf.layer] and item.dxf.owner == space.block_record_handle,'Independent identity/owner')
        definition = item.get_underlay_def()
        require(definition.dxftype() == DEFINITIONS[t] and definition.dxf.filename == FILES[t],'Independent definition')
        require(item.proxy_graphic == PROXY,'Independent graphics')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25,-4.5,2.) and tuple(line.dxf.end) == (18.5,9.25,-3.),'Following LINE changed')
    if placement >= 2: require(len(list(doc.modelspace().query('INSERT'))) == (1 if placement == 2 else 0),'Block reference policy')
    audit = doc.audit(); require(not audit.errors and not audit.fixes,'Independent database errors/repairs')
    return count,handles

def main(directory):
    specs = list(itertools.product(PROFILES,(False,True),range(4),('source','False','True')))
    def name(s):
        y,b,p,o = s; return f'underlay-scale-AutoCad{y}-{b}-{p}-{o}.dxf'
    wanted = {name(s) for s in specs}
    def inventory(actual): require(actual == wanted,'Missing/extra scalar drawings')
    inventory({p.name for p in directory.glob('underlay-scale-*.dxf')})
    reject(lambda: inventory(wanted-{min(wanted)})); reject(lambda: inventory(wanted|{'underlay-scale-extra.dxf'}))
    controls = 0; sources = {}
    for s in specs:
        y,b,p,o = s; c,h = inspect(directory/name(s),y,b if o == 'source' else o == 'True',p,o == 'source')
        controls += c
        if o == 'source': sources[(y,b,p)] = h
        else: require(h == sources[(y,b,p)], 'Round trip changed scalar-fixture handles')
    print(f'PASS: {len(specs)} underlay scale drawings / {len(specs)*9*VARIANTS} records; '
          f'{controls} packet corruptions and two inventory controls rejected; zero database errors/repairs. '
          'Finite zero retention is not native rendering acceptance.')

if __name__ == '__main__':
    require(len(sys.argv) == 2,'Usage: verify_underlay_scales.py ARTIFACTS')
    main(Path(sys.argv[1]))
