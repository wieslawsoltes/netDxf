#!/usr/bin/env python3
"""Verify polygon-mesh WCS affine output with exact Fraction arithmetic.

All selected ordered POLYLINE/VERTEX/SEQEND fields except handle values are
compared. Handle framing, uniqueness and child ownership are checked separately.
Normals admit four ULP per component; vertex coordinates are exact binary64
roundings of independent rational dot products. This is not native AutoCAD QA.
"""
from decimal import Decimal, localcontext
from fractions import Fraction
import io
import itertools
import math
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

PROFILES = {'AutoCad2000':'AC1015', 'AutoCad2004':'AC1018', 'AutoCad2007':'AC1021',
            'AutoCad2010':'AC1024', 'AutoCad2013':'AC1027', 'AutoCad2018':'AC1032'}
MODES = ('identity','translate','scale','shear','reflect','rank2','collapse','tiny','huge')
KINDS = ('POLYLINE','VERTEX','SEQEND')
MASK = (1 << 64)-1


def require(value, message):
    if not value:
        raise ValueError(message)


def matrix(mode):
    if mode == 'shear':
        return ((1.,2.,.5),(0.,1.,.25),(0.,0.,1.))
    diagonal = {'identity':(1.,1.,1.), 'translate':(1.,1.,1.), 'scale':(2.,3.,4.),
                'reflect':(-1.,1.,1.), 'rank2':(1.,1.,0.), 'collapse':(0.,0.,0.),
                'tiny':(1e-200,)*3, 'huge':(1e200,)*3}[mode]
    return tuple(tuple(diagonal[r] if r == c else 0. for c in range(3)) for r in range(3))


def image(a, point, translation=(0.,0.,0.)):
    return tuple(sum((Fraction.from_float(x)*Fraction.from_float(y) for x,y in zip(row,point)),
                     Fraction.from_float(t)) for row,t in zip(a,translation))


def normal(mode):
    # These are the stored binary64 components for this explicit source vector.
    original = (2.*(1./7.), -3.*(1./7.), 6.*(1./7.))
    if mode in ('identity','translate','collapse'):
        return original
    result = image(matrix(mode), original)
    with localcontext() as context:
        context.prec = 1100
        values = [Decimal(v.numerator)/Decimal(v.denominator) for v in result]
        length = sum(v*v for v in values).sqrt()
        return tuple(float(v/length) for v in values)


def ordered(value):
    bits = int.from_bytes(struct.pack('>d', value), 'big')
    return (~bits & MASK) if bits >> 63 else bits | (1 << 63)


def expected_packets(version, mode):
    a = matrix(mode)
    translation = (7.,-11.,13.) if mode in ('translate','collapse') else (0.,0.,0.)
    proxy = [(160 if version in ('AutoCad2013','AutoCad2018') else 92,4),(310,bytes((2,3,5,7)))] if mode == 'identity' else []
    # PolygonAffineSubject explicitly assigns DensityU=5 and DensityV=7.
    # Unfitted grids retain these dormant hints; a zero default is not a reset.
    head = [(0,'POLYLINE'),(100,'AcDbEntity'),(67,0),(8,'POLYGON_AFFINE'),(62,3),
            (6,'ByLayer'),(370,-1),(48,1.75),(60,1)] + proxy + [
            (100,'AcDbPolygonMesh'),(10,0.),(20,0.),(30,0.),(71,4),(72,4),(73,5),(74,7),
            (70,16),(75,0),*zip((210,220,230),normal(mode)),(1001,'POLYGON_AFFINE'),(1000,'unchanged')]
    packets = [head]
    # Model uses U-fast controls; DXF VERTEX records are M-major / N-fast.
    for u in range(4):
        for v in range(4):
            point = (float(1+u),float(2+v),3.+(u+4*v)*.125)
            transformed = tuple(float(x) for x in image(a,point,translation))
            packets.append([(0,'VERTEX'),(100,'AcDbEntity'),(8,'POLYGON_AFFINE'),(62,3),
                            (100,'AcDbVertex'),(100,'AcDbPolygonMeshVertex'),
                            *zip((10,20,30),transformed),(70,64),(40,0.),(41,0.)])
    packets.append([(0,'SEQEND'),(100,'AcDbEntity'),(8,'POLYGON_AFFINE')])
    return packets


def records(data):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    selected, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0][1] in KINDS:
                selected.append(current)
            current = []
        value = tag.value if tag.code == 310 else cast_tag_value(tag.code,tag.value)
        if tag.code == 310 and isinstance(value,str):
            value = bytes.fromhex(value)
        current.append((tag.code,value))
    if current and current[0][1] in KINDS:
        selected.append(current)
    require(selected and selected[0][0] == (0,'POLYLINE'), 'Missing selected mesh')
    handles, parent, output = set(), None, []
    for index, packet in enumerate(selected):
        require([c for c,_ in packet[:3]] == [0,5,330], 'Identity framing differs')
        require(sum(c == 5 for c,_ in packet) == 1 and sum(c == 330 for c,_ in packet) == 1, 'Repeated identity')
        handle, owner = int(packet[1][1],16), int(packet[2][1],16)
        require(handle > 0 and owner > 0 and handle not in handles, 'Invalid/duplicate identity')
        handles.add(handle)
        if index == 0:
            parent = handle
        else:
            require(owner == parent, 'Child ownership differs')
        output.append([packet[0]]+packet[3:])
    return output


def check_packets(actual, expected):
    require(len(actual) == len(expected), 'Packet inventory differs')
    for packet, wanted in zip(actual,expected):
        require(len(packet) == len(wanted), 'Packet length differs')
        for (code,value),(expected_code,expected_value) in zip(packet,wanted):
            require(code == expected_code, 'Packet order/code differs')
            if code in (210,220,230):
                require(math.isfinite(value), 'Nonfinite normal')
                require(value == expected_value or abs(ordered(value)-ordered(expected_value)) <= 4, 'Normal exceeds four ULP')
            else:
                require(value == expected_value, 'Exact coordinate/metadata differs')
    n = [value for code,value in actual[0] if code in (210,220,230)]
    require(abs(math.fsum(v*v for v in n)-1.) <= 2e-15, 'Normal is not unit length')


def rejected(check, value):
    try:
        check(value)
    except (ValueError,TypeError,IndexError,KeyError):
        return 1
    raise AssertionError('Actual corruption escaped positive validator')


def corrupt(value):
    if isinstance(value,(int,float)):
        return value*1.125+math.copysign(1.,value) if abs(value) >= 1 else value+.125
    if isinstance(value,bytes):
        return value+b'!'
    return value+'!'


def main():
    directory = Path(sys.argv[1])
    inventory = {f'polygon-affine-{version}-{binary}-{mode}.dxf':(version,binary,mode)
                 for version,binary,mode in itertools.product(PROFILES,(False,True),MODES)}
    def check_inventory(actual):
        require(actual == set(inventory), 'Exact polygon affine inventory differs')
    names = {p.name for p in directory.glob('polygon-affine-*')}
    check_inventory(names)
    inventory_controls = rejected(check_inventory,names-{next(iter(names))})
    inventory_controls += rejected(check_inventory,names|{'polygon-affine-extra.dxf'})
    controls = 0
    for name,(version,binary,mode) in inventory.items():
        path = directory/name
        data = path.read_bytes()
        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
        expected = expected_packets(version,mode)
        packets = records(data)
        check = lambda value: check_packets(value,expected)
        check(packets)
        # Reject the previous data-loss behavior and valid-looking swapped hints,
        # not just malformed values. All existing packet corruption checks remain.
        for density_code, wrong_values in ((73, (0, 7)), (74, (0, 5))):
            slot = next(i for i, tag in enumerate(packets[0]) if tag[0] == density_code)
            for wrong in wrong_values:
                changed = list(packets)
                changed[0] = list(packets[0])
                changed[0][slot] = (density_code, wrong)
                controls += rejected(check, changed)
        for r,packet in enumerate(packets):
            controls += rejected(check,packets[:r]+packets[r+1:])
            for n,(code,value) in enumerate(packet):
                changed = list(packets); changed[r] = list(packet)
                changed[r][n] = (code,corrupt(value))
                controls += rejected(check,changed)
                changed = list(packets); changed[r] = packet[:n]+packet[n+1:]
                controls += rejected(check,changed)
        document = ezdxf.readfile(path)
        require(document.dxfversion == PROFILES[version], 'Profile differs')
        meshes = list(document.modelspace().query('POLYLINE'))
        require(len(meshes) == 1, 'Placement differs')
        require(meshes[0].dxf.m_smooth_density == 5 and meshes[0].dxf.n_smooth_density == 7,
                'Independent dormant density interpretation differs')
        audit = document.audit()
        require(not audit.errors and not audit.fixes, 'Graph requires repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: {len(inventory)} polygon affine drawings / {len(inventory)*16} exact WCS vertices; '
          f'{controls} actual-packet and {inventory_controls} inventory corruptions rejected; zero graph errors/repairs')


if __name__ == '__main__':
    main()
