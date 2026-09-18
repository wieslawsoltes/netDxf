#!/usr/bin/env python3
"""Independently verify OCS matrices and physical DXF coordinates.

Uses high-precision Decimal construction of Autodesk's arbitrary-axis algorithm,
not either library's OCS helper. Small real tilts must not be hidden by an
absolute unit-scale tolerance. Graph audits are separate from geometry checks.
"""
from __future__ import annotations
import argparse
from decimal import Decimal, localcontext
import io
import json
import math
from pathlib import Path
import struct
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

PROFILES = {'AutoCad2000':'AC1015', 'AutoCad2004':'AC1018', 'AutoCad2007':'AC1021',
            'AutoCad2010':'AC1024', 'AutoCad2013':'AC1027', 'AutoCad2018':'AC1032'}
KINDS = ('CIRCLE', 'TEXT', 'ARC')
POSITIONS = {'CIRCLE':(1e12,-2e12,3e12), 'TEXT':(-4e12,5e12,-6e12), 'ARC':(7e12,-8e12,9e12)}
NORMALS = ((1e-13,2e-13,1.), (-1e-13,-2e-13,1.), (1e-13,0.,-1.),
           (0.,1e-13,1.), (0.,1.,0.), (2.,3.,6.))


def require(ok, message):
    if not ok:
        raise ValueError(message)


def bits(value):
    return struct.pack('>d', value).hex().upper()


def value(raw):
    require(isinstance(raw, str) and len(raw) == 16 and all(c in '0123456789ABCDEF' for c in raw), 'Bad binary64 encoding')
    return struct.unpack('>d', bytes.fromhex(raw))[0]


def inputs():
    result = []
    for exponent in (-1074,-1022,-1000,-600,-500,-50,0,50,500,600,1000,1021):
        s = math.ldexp(1., exponent)
        result.extend(((s,-2*s,3*s),(0.,0.,s),(0.,0.,-s)))
    result.extend((NORMALS[0],NORMALS[1],NORMALS[2],NORMALS[3],(0.,1.,0.),(1.,0.,0.),
                   (float.fromhex('0x1.fffffffffffffp1023'),)*3,
                   (math.ldexp(1.,-1074),0.,float.fromhex('0x1.fffffffffffffp1023')),
                   (.015624,.015624,math.sqrt(1-2*.015624*.015624)),
                   (.015626,.015624,math.sqrt(1-.015626*.015626-.015624*.015624)),
                   (.015624,.015626,math.sqrt(1-.015624*.015624-.015626*.015626))))
    return result


def dot(a, b):
    return sum(x*y for x,y in zip(a,b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def unit(a):
    length = dot(a,a).sqrt()
    return tuple(x/length for x in a)


def frame(source):
    with localcontext() as context:
        context.prec = 1500
        z = unit(tuple(Decimal.from_float(x) for x in source))
        threshold = Decimal(1)/64
        reference = (Decimal(0),Decimal(1),Decimal(0)) if abs(z[0]) < threshold and abs(z[1]) < threshold else (Decimal(0),Decimal(0),Decimal(1))
        x = unit(cross(reference,z)); y = unit(cross(z,x))
        return x,y,z


def component(actual, expected):
    require(math.isfinite(actual), 'Nonfinite basis component')
    if actual == expected == 0:
        return
    tolerance = 16 * math.ulp(expected)
    require(abs(actual-expected) <= tolerance, 'Basis component differs by more than sixteen ULP')


def check_matrix(row, source):
    require(isinstance(row,dict) and set(row)=={'input','matrix'}, 'Wrong matrix row shape')
    require(row['input']==[bits(v) for v in source], 'Input corpus changed')
    require(len(row['matrix'])==9, 'Wrong matrix length')
    actual = [value(v) for v in row['matrix']]
    axes = frame(source)
    expected = [float(axes[c][r]) for r in range(3) for c in range(3)]
    for a,e in zip(actual,expected):
        component(a,e)
    for c in range(3):
        require(abs(math.fsum(actual[r*3+c]**2 for r in range(3))-1) <= 4e-15, 'Non-unit basis')


def records(data):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'),newline=None))
    result=[]; record=[]
    for tag in loader:
        if tag.code==0:
            if record and record[0][1] in KINDS: result.append(record)
            record=[]
        record.append((tag.code,tag.value))
    if record and record[0][1] in KINDS: result.append(record)
    return result


def one(record, code):
    found=[v for c,v in record if c==code]
    require(len(found)==1, f'Missing/repeated group {code}')
    return found[0]


def triple(record, code):
    return tuple(float(one(record,c)) for c in (code,code+10,code+20))


def check_records(actual, normal):
    require([r[0][1] for r in actual]==list(KINDS), 'Selected entity inventory changed')
    axes=frame(normal)
    for record in actual:
        kind=record[0][1]; position=POSITIONS[kind]
        expected_normal=tuple(float(x) for x in axes[2])
        for a,e in zip(triple(record,210),expected_normal): component(a,e)
        with localcontext() as context:
            context.prec=1500
            point=tuple(Decimal.from_float(x) for x in position)
            expected=tuple(float(dot(axis,point)) for axis in axes)
        stored=triple(record,10)
        tolerance=8*math.ulp(max(abs(p) for p in position))
        require(all(math.isfinite(a) and abs(a-e)<=tolerance for a,e in zip(stored,expected)), 'Physical OCS position disagrees with independent frame')
        require(one(record,8)=='0', 'Layer changed')
        if kind=='TEXT':
            require(one(record,1)=='OCS tilt' and float(one(record,40))==2 and float(one(record,50))==37, 'Text content/height/rotation changed')
        else:
            require(float(one(record,40))==(7 if kind=='CIRCLE' else 5), 'Radius changed')
            if kind=='ARC': require(float(one(record,50))==20 and float(one(record,51))==210, 'Arc interval changed')


def rejected(check, candidate):
    try:
        check(candidate)
    except (ValueError, TypeError, OverflowError):
        return 1
    raise AssertionError('Corruption escaped the same positive validator')


def main():
    parser=argparse.ArgumentParser(description=__doc__); parser.add_argument('directory',type=Path)
    directory=parser.parse_args().directory
    expected_files={f'ocs-axis-{v}-{b}-{n}-{i}.dxf' for v in PROFILES for b in (False,True) for n in (False,True) for i in range(6)}
    expected_files.add('ocs-axis-numerics.json')
    actual_files={p.name for p in directory.glob('ocs-axis-*')}
    inventory=lambda candidate:require(candidate==expected_files,'OCS output inventory differs')
    inventory(actual_files)
    controls=rejected(inventory,actual_files-{'ocs-axis-numerics.json'})+rejected(inventory,actual_files|{'ocs-axis-extra.dxf'})
    rows=json.loads((directory/'ocs-axis-numerics.json').read_text())
    sources=inputs(); require(len(rows)==len(sources),'Numerical row count differs')
    numerical_controls=0
    for row,source in zip(rows,sources):
        check=lambda candidate:check_matrix(candidate,source)
        check(row)
        for index in range(9):
            changed=dict(row); matrix=list(row['matrix']); x=value(matrix[index])
            for _ in range(64): x=math.nextafter(x, math.inf)
            matrix[index]=bits(x); changed['matrix']=matrix
            numerical_controls+=rejected(check,changed)
    drawings=packet_controls=0
    for version,profile in PROFILES.items():
        for binary in (False,True):
            for nested in (False,True):
                for index,normal in enumerate(NORMALS):
                    path=directory/f'ocs-axis-{version}-{binary}-{nested}-{index}.dxf'
                    data=path.read_bytes(); require(data.startswith(b'AutoCAD Binary DXF')==binary,'Wrong transport')
                    found=records(data); check=lambda candidate:check_records(candidate,normal); check(found)
                    for r,record in enumerate(found):
                        for code in (10,20,30,210,220,230):
                            at=next(i for i,(c,_) in enumerate(record) if c==code)
                            for mode in ('change','nan','omit','duplicate'):
                                candidate=[list(rec) for rec in found]
                                if mode=='change': candidate[r][at]=(code,float(record[at][1])+1.)
                                elif mode=='nan': candidate[r][at]=(code,math.nan)
                                elif mode=='omit': del candidate[r][at]
                                else: candidate[r].insert(at,candidate[r][at])
                                packet_controls+=rejected(check,candidate)
                        packet_controls+=rejected(check,found[:r]+found[r+1:])
                    document=ezdxf.readfile(path); require(document.dxfversion==profile,'Wrong DXF profile')
                    audit=document.audit(); require(not audit.errors and not audit.fixes,'OCS drawing needs repairs')
                    drawings+=1
    print(f'PASS ezdxf {ezdxf.__version__}: {drawings} drawings / {drawings*3} physical OCS positions; '
          f'{len(rows)} Decimal frame scenarios / {len(rows)*9} component checks; '
          f'{packet_controls} record, {numerical_controls} numerical and {controls} inventory corruptions rejected; zero graph errors/repairs')


if __name__=='__main__':
    main()
