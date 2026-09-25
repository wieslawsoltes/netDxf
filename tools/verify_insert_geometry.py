#!/usr/bin/env python3
"""Independently verify INSERT frames, grid steps, attributes and finite scale packets.

Exact-zero source scales are checked physically, not certified as native-valid
geometry. ezdxf's model may repair zero scales, so no zero-rendering claim is made.
"""
import itertools
import math
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.math import OCS, Vec3
from verify_raw_line_geometry import require, reject, audit_signature
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_dimlfac_fidelity import PROFILES, records

PROXY = bytes((i * 31) & 255 for i in range(300))
IDENTITY = ((1., 0., 0.), (0., 1., 0.), (0., 0., 1.))
SCALARS = ((2., -3., 4.), (1e-20, -2e-20, 3e-20),
           (5e-324, -5e-324, 5e-324), (0., -0., 0.),
           (sys.float_info.max, -sys.float_info.min, 1.), (1., 1., 1.), (1., 2., 1.))


def one(row, code, default=None):
    values = [value for c, value in row if c == code]
    require(len(values) == 1 or (not values and default is not None), f'Missing/duplicate field {code}')
    return values[0] if values else default


def vec(row, first):
    return tuple(one(row, c) for c in (first, first + 10, first + 20))


def dot(a, b): return sum(x*y for x, y in zip(a, b))
def transpose(a): return tuple(zip(*a))
def mul(a, b): return tuple(dot(row, b) for row in a)
def product(a, b): return tuple(tuple(dot(row, col) for col in transpose(b)) for row in a)
def add(a, b): return tuple(x+y for x, y in zip(a, b))
def scale(a, k): return tuple(k*v for v in a)


def frame(plane):
    # Explicit source OCS bases; do not call the production arbitrary-axis code.
    base = IDENTITY if plane == 0 else ((-1.,0.,0.),(0.,1.,0.),(0.,0.,-1.)) if plane == 1 else (
        (-1.,0.,0.),(0.,-.8,.6),(0.,.6,.8))
    c, s = math.cos(math.radians(37)), math.sin(math.radians(37))
    return product(base, ((c,-s,0.),(s,c,0.),(0.,0.,1.)))


def matrix(plane, kind):
    if kind < 2: return IDENTITY
    if kind == 2: return ((0.,1.,0.),(1.,0.,0.),(0.,0.,1.))
    if kind == 3: return ((-2.,0.,0.),(0.,-2.,0.),(0.,0.,-2.))
    if kind == 4: return product(product(frame(plane), ((2.,0.,0.),(0.,3.,0.),(0.,0.,4.))), transpose(frame(plane)))
    if kind == 5: return ((1e-14,0.,0.),(0.,1e-14,0.),(0.,0.,1e-14))
    c, s = math.cos(.7), math.sin(.7)
    return ((1.,0.,0.),(0.,c,-s),(0.,s,c))


def near(actual, expected):
    a, b = tuple(actual), tuple(expected)
    limit = max(1e-28, 2e-10 * max(abs(v) for v in b))
    require(len(a) == len(b) and all(math.isfinite(v) and abs(v-w) <= limit for v, w in zip(a,b)),
            'Incorrect INSERT/ATTRIB WCS point, axis or grid step')


def same(a, b):
    require(isinstance(a, float) and math.isfinite(a) and struct.pack('>d', a) == struct.pack('>d', b),
            'Incorrect exact binary64 scale')


def expected(plane, kind, source):
    m = IDENTITY if source else matrix(plane, kind)
    shift = (0.,0.,0.) if source or kind == 0 else (11.,-13.,17.)
    f = frame(plane)
    return [add(mul(m,(5.,-3.,7.)),shift)] + [mul(m,mul(f,axis)) for axis in
        ((2.,0.,0.),(0.,-3.,0.),(0.,0.,4.),(7.5,0.,0.),(0.,-4.25,0.))]


def common(row):
    starts = [i for i, tag in enumerate(row) if tag == (100,'AcDbEntity')]
    require(len(starts) == 1, 'Common subclass inventory')
    start = starts[0]+1
    end = next((i for i in range(start,len(row)) if row[i][0] == 100), len(row))
    return row[start:end]


def check_cache(row, year, retained):
    code = 160 if year >= 2013 else 92
    want = [(code,len(PROXY))] + [(310,PROXY[i:i+127]) for i in range(0,len(PROXY),127)] if retained else []
    require([t for t in common(row) if t[0] in (92,160,310)] == want, 'Stale, missing or altered common graphics')


def specification(name, scalar):
    if scalar:
        require(name in {f'IG_SCALAR_{i}' for i in range(7)}, 'Unknown scalar record')
        return int(name[10:]), 0, int(name[10:]) % 2
    require(name in {f'IG_{p}_{m}_{a}' for p,m,a in itertools.product(range(3),range(7),range(2))}, 'Unknown frame record')
    return tuple(map(int,name.split('_')[1:]))


def check_row(row, year, source, scalar):
    name = one(row,2); p, kind, array = specification(name,scalar)
    require(row[0] == (0,'INSERT'), 'Entity type')
    require(row.count((100,'AcDbMInsertBlock' if array else 'AcDbBlockReference')) == 1, 'INSERT subclass')
    require(one(row,70,1) == (3 if array else 1) and one(row,71,1) == (2 if array else 1), 'Grid counts')
    require(one(row,66) == 1, 'Missing attribute-sequence flag')
    require([t for t in row if t[0] >= 1000] == [(1001,'IG_KEEP'),(1000,'unchanged')], 'Unrelated XData changed')
    extrusion = vec(row,210); require(abs(dot(extrusion,extrusion)-1) <= 2e-12, 'Non-unit extrusion')
    ocs = OCS(extrusion)
    position = ocs.to_wcs(vec(row,10))
    rotation = one(row,50); require(math.isfinite(rotation) and 0 <= rotation < 360, 'Noncanonical rotation')
    if scalar:
        for code, value in zip((41,42,43),SCALARS[p]): same(one(row,code,1.),value)
        present = {code for code,value in row if code in (41,42,43)}
        required = set() if source and p == 5 else {42} if source and p == 6 else {41,42,43}
        require(present == required, 'Optional scale-field inventory')
        near(position,(5.,-3.,7.) if source else (6.,-1.,10.))
        same(rotation,0.); same(one(row,44),7.5); same(one(row,45),-4.25)
        near(extrusion,(0.,0.,1.))
        retained = source
    else:
        c, s = math.cos(math.radians(rotation)), math.sin(math.radians(rotation))
        sx, sy, sz = (one(row,code) for code in (41,42,43))
        require(all(math.isfinite(v) and v != 0 for v in (sx,sy,sz)), 'Invalid frame scale')
        dx, dy = one(row,44), one(row,45)
        actual = [position,ocs.to_wcs((sx*c,sx*s,0.)),ocs.to_wcs((-sy*s,sy*c,0.)),ocs.to_wcs((0.,0.,sz)),
                  ocs.to_wcs((dx*c,dx*s,0.)),ocs.to_wcs((-dy*s,dy*c,0.))]
        for a,b in zip(actual,expected(p,kind,source)): near(a,b)
        retained = source or kind == 0
    check_cache(row,year,retained)
    return p, kind, array


def check_attribute(row, owner, year, source, scalar, p, kind):
    require(row[0] == (0,'ATTRIB') and one(row,330) == owner, 'Attribute ownership/sequence')
    require(one(row,2) == 'TAG' and one(row,1) == 'KEEP' and one(row,7) == 'Standard', 'Attribute tag/value/style')
    position = OCS(vec(row,210)).to_wcs(vec(row,10))
    wanted = (6.,2.,4.)
    if not source: wanted = add(wanted,(1.,2.,3.)) if scalar else add(mul(matrix(p,kind),wanted), (0.,0.,0.) if kind == 0 else (11.,-13.,17.))
    near(position,wanted)
    check_cache(row,year,not scalar and (source or kind == 0))


def corrupt(row, year, source, scalar):
    count = 0
    for code in (10,20,30,41,42,43,44,45,50,70,71,210,220,230,66):
        indexes = [i for i,t in enumerate(row) if t[0] == code]
        if not indexes: continue
        index = indexes[0]
        # Bit corruption for scalar scale fidelity; large perturbations for geometry tolerance.
        value = row[index][1]
        altered = struct.unpack('>d', struct.pack('>Q',struct.unpack('>Q',struct.pack('>d',value))[0]^1))[0] \
            if scalar and code in (41,42,43) else value+17
        for mode in ('change','duplicate'):
            bad = list(row)
            if mode == 'change': bad[index] = (code,altered)
            else: bad.insert(index,bad[index])
            count += reject(lambda: check_row(bad,year,source,scalar))
    data = common(row)
    if any(c in (92,160,310) for c,v in data):
        bad = [t for t in row if t[0] not in (92,160,310)]
    else:
        index = row.index((100,'AcDbEntity'))+1
        bad = row[:index]+[(160 if year >= 2013 else 92,1),(310,b'X')]+row[index:]
    count += reject(lambda: check_row(bad,year,source,scalar))
    return count


def inspect(path, year, binary, placement, source, scalar):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary,'Wrong transport')
    tags = load_visibility_tags(path); index = tags.index((9,'$ACADVER'))
    require(tags[index+1] == (1,PROFILES[year]),'Wrong DXF version')
    entries = [tags[a:b] for a,b in records(tags)]
    wanted = {f'IG_SCALAR_{i}' for i in range(7)} if scalar else {f'IG_{p}_{m}_{a}' for p,m,a in itertools.product(range(3),range(7),range(2))}
    selected = [(i,r) for i,r in enumerate(entries) if r[0] == (0,'INSERT') and any(c==2 and v in wanted for c,v in r)]
    require(len(selected) == len(wanted) and {one(r,2) for i,r in selected} == wanted,'INSERT record inventory')
    handles, controls = {}, 0
    for index,row in selected:
        p,kind,array = check_row(row,year,source,scalar)
        name,handle = one(row,2),one(row,5)
        attribute, end = entries[index+1:index+3]
        check_attribute(attribute,handle,year,source,scalar,p,kind)
        require(end[0] == (0,'SEQEND'), 'Missing sequence terminator')
        require(one(end,330) == handle, 'Sequence owner')
        handles[name] = (handle,one(row,330),one(attribute,5),one(end,5))
        controls += corrupt(row,year,source,scalar)
        # Actual attribute record corruption, not a constructed unrelated payload.
        bad = list(attribute); at = next(i for i,t in enumerate(bad) if t[0] == 20); bad[at] = (20,bad[at][1]+17)
        controls += reject(lambda: check_attribute(bad,handle,year,source,scalar,p,kind))
    require(len({v[0] for v in handles.values()}) == len(wanted),'Duplicate INSERT handles')
    doc = ezdxf.readfile(path)
    space = doc.modelspace() if placement == 0 else doc.layouts.get('IG_PAPER') if placement == 1 else doc.blocks['IG_CONTAINER']
    loaded = list(space.query('INSERT'))
    require(len(loaded) == len(wanted) and {item.dxf.name for item in loaded} == wanted,'Independent placement/inventory')
    for item in loaded:
        name = item.dxf.name; p,kind,array = specification(name,scalar)
        require(item.dxf.handle == handles[name][0] and item.dxf.owner == space.block_record_handle == handles[name][1], 'Independent owner/handle')
        require(len(item.attribs) == 1 and item.attribs[0].dxf.handle == handles[name][2] and item.seqend.dxf.handle == handles[name][3], 'Independent sequence handles')
        require(item.attribs[0].dxf.text == 'KEEP' and item.attribs[0].dxf.tag == 'TAG','Independent attribute data')
        definition = doc.blocks[name]
        near(definition.base_point,(1.,-2.,3.))
        line, = definition.query('LINE'); near(line.dxf.start,(2.,3.,4.)); near(line.dxf.end,(5.,-1.,7.))
        if not scalar:
            # Independent INSERT transform includes the block-origin offset.
            transform = item.matrix44(); wanted_geometry = expected(p,kind,source)
            near(transform.transform(Vec3(1.,-2.,3.)),wanted_geometry[0])
            for axis,wanted_axis in zip(((1.,0.,0.),(0.,1.,0.),(0.,0.,1.)),wanted_geometry[1:4]):
                near(transform.transform_direction(Vec3(axis)),wanted_axis)
        # Do not substitute ezdxf's potentially repaired zero scales for source bits.
    line, = doc.modelspace().query('LINE')
    near(line.dxf.start,(101.,102.,103.)); near(line.dxf.end,(104.,105.,106.))
    if not scalar: require(not any(any(c.values()) for c in audit_signature(doc)), 'Independent geometry audit errors/repairs')
    return handles, controls


def main(directory):
    names = {f'insert-{kind}-AutoCad{year}-{transport}-{place}-{stage}.dxf'
             for kind,year,transport,place,stage in itertools.product(('geometry','scalars'),PROFILES,('text','binary'),range(4),('source','output','resave'))}
    def inventory(actual): require(actual == names,'Missing/extra INSERT geometry files')
    inventory({p.name for pattern in ('insert-geometry-*.dxf','insert-scalars-*.dxf') for p in directory.glob(pattern)})
    controls = reject(lambda: inventory(names-{next(iter(names))})) + reject(lambda: inventory(names|{'extra.dxf'}))
    for scalar,year,transport,place in itertools.product((False,True),PROFILES,('text','binary'),range(4)):
        previous = None
        for stage in ('source','output','resave'):
            path = directory/f'insert-{"scalars" if scalar else "geometry"}-AutoCad{year}-{transport}-{place}-{stage}.dxf'
            binary = (transport == 'binary') if stage != 'resave' else (transport != 'binary')
            if scalar and stage == 'source': binary = False
            handles, rejected = inspect(path,year,binary,place,stage == 'source',scalar)
            require(previous is None or previous == handles,'INSERT/ATTRIB/SEQEND handles or owners changed')
            previous = handles; controls += rejected
    print(f'PASS: {len(names)} drawings /7056 INSERT records and attribute sequences; {controls} corruptions rejected. '
          'Geometry drawings have zero independent audit errors/repairs. Exact-zero scale fidelity is physical-only; no native acceptance claim.')


if __name__ == '__main__':
    require(len(sys.argv) == 2,'Usage: verify_insert_geometry.py ARTIFACTS')
    main(Path(sys.argv[1]))
