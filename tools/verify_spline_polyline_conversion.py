#!/usr/bin/env python3
"""Independent rational quadratic SPLINE-to-polyline geometry and packet oracle.

Expected WCS samples use exact Fraction Bernstein evaluation. OCS frames use the
shared independent Decimal oracle, never either library's curve/frame helpers.
All selected ordered tags except identity values are compared; identities retain
separate framing, uniqueness and ownership checks. XY/WCS: 8 ULP at source scale.
"""
from fractions import Fraction as F
from pathlib import Path
import argparse
import io
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from curve_projection_oracle import (PROFILES, NORMALS, require, rejected, frame,
    packet, common, lw_expected, check_packet, corruptions, audit)


def samples():
    controls=((2,3,5),(-4,6,7),(8,-2,9));weights=(F(1),F(1,2),F(2))
    result=[]
    for i in range(9):
        t=F(i,8);basis=((1-t)**2,2*t*(1-t),t*t)
        coeff=[b*w for b,w in zip(basis,weights)];den=sum(coeff)
        result.append(tuple(float(sum(c*p[a] for c,p in zip(coeff,controls))/den) for a in range(3)))
    return result


def legacy_packet(data):
    tags=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'),newline=None))
    records=[];current=[]
    for tag in tags:
        if tag.code==0:
            if current and current[0][1] in ('POLYLINE','VERTEX','SEQEND'):records.append(current)
            current=[]
        value=tag.value if tag.code==1004 else cast_tag_value(tag.code,tag.value)
        if tag.code==1004 and isinstance(value,str):value=bytes.fromhex(value)
        current.append((tag.code,value))
    if current and current[0][1] in ('POLYLINE','VERTEX','SEQEND'):records.append(current)
    require([r[0][1] for r in records]==['POLYLINE']+['VERTEX']*9+['SEQEND'],'Wrong legacy sequence')
    handles=[]
    for r in records:
        require([c for c,v in r[:3]]==[0,5,330], 'Identity framing')
        require(sum(c==5 for c,v in r)==sum(c==330 for c,v in r)==1,'Repeated identity')
        handle,owner=r[1][1],r[2][1]
        require(int(handle,16)>0 and int(owner,16)>0,'Invalid identity')
        if handles:require(owner==handles[0],'Wrong child owner')
        handles.append(handle)
    require(len(set(handles))==len(handles),'Duplicate sequence identity')
    return [tag for r in records for tag in r[:1]+r[3:]]


def legacy_expected(points,index,version):
    n=tuple(map(float,frame(index)[2]))
    result=common(version,'POLYLINE')+[(100,'AcDb3dPolyline'),(10,0.),(20,0.),(30,0.),(70,8),(75,0)]
    result+=list(zip((210,220,230),n))+[(1001,'CURVE_PROJECTION'),(1000,'retained'),(1004,b'\x09\x02\x06')]
    for point in points:
        result += [(0,'VERTEX'),(100,'AcDbEntity'),(8,'PROJECTION'),(62,5 if version=='AutoCad2000' else 137)]
        if version!='AutoCad2000':result.append((420,0x204060))
        result += [(100,'AcDbVertex'),(100,'AcDb3dPolylineVertex')]+list(zip((10,20,30),point))+[(70,32),(40,0.),(41,0.)]
    return result+[(0,'SEQEND'),(100,'AcDbEntity'),(8,'PROJECTION')]


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path)
    directory=parser.parse_args().directory
    cases=[(n,p,v,b) for n in range(len(NORMALS)) for p in range(3) for v in PROFILES for b in (False,True)]
    names={f'spline-conversion-{n}-{p}-{v}-{b}.dxf' for n,p,v,b in cases}
    actual={p.name for p in directory.glob('spline-conversion-*')}
    inventory=lambda value:require(value==names,'Spline conversion file inventory differs')
    inventory(actual); inventory_controls=rejected(inventory,actual-{next(iter(actual))})+rejected(inventory,actual|{'spline-conversion-extra.dxf'})
    points=samples(); negative=0
    for n,p,v,b in cases:
        path=directory/f'spline-conversion-{n}-{p}-{v}-{b}.dxf';data=path.read_bytes()
        actual=legacy_packet(data) if p==2 else packet(data)
        expected=legacy_expected(points,n,v) if p==2 else lw_expected(points,n,False,0. if p==0 else -7.5,v,False)
        check=lambda value:check_packet(value,expected)
        check(actual);negative+=corruptions(actual,check);audit(path,v,b)
    print(f'PASS: {len(cases)} rational spline conversion drawings / {len(cases)*9} physical samples; '
          f'{negative} packet and {inventory_controls} inventory corruptions rejected; zero graph errors/repairs')


if __name__=='__main__':main()
