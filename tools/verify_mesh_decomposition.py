#!/usr/bin/env python3
"""Independent triangulation invariants and physical 3DFACE packet validation.

No ear-clipping implementation is used by this checker. It validates unchanged
input vertices, exact integer area/winding, edge incidence, interior diagonals,
noncrossing edges, fixed packet metadata and graph audits.
"""
from collections import defaultdict
from fractions import Fraction
import io
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

POLYGONS = {
    'triangle': ((0,0),(4,0),(0,4)),
    'quad': ((0,0),(4,0),(4,4),(0,4)),
    'concave': ((0,0),(4,0),(4,4),(2,1),(0,4)),
    'ell': ((0,0),(4,0),(4,1),(1,1),(1,4),(0,4)),
    'collinear': ((0,0),(2,0),(4,0),(4,4),(0,4)),
    'notch': ((0,0),(6,0),(6,6),(4,6),(4,2),(2,2),(2,6),(0,6)),
}
PROFILES = dict(zip(('AutoCad2000','AutoCad2004','AutoCad2007','AutoCad2010','AutoCad2013','AutoCad2018'),
                    ('AC1015','AC1018','AC1021','AC1024','AC1027','AC1032')))
PREFIX = [(0,'3DFACE'),(100,'AcDbEntity'),(67,0),(8,'DECOMPOSITION'),(62,5),(6,'ByLayer'),(370,-1),(48,2.25),(60,1),(100,'AcDbFace')]
SUFFIX = [(1001,'DECOMPOSITION'),(1000,'source payload')]


def require(value, text):
    if not value:
        raise ValueError(text)


def cross(a,b,c):
    return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])


def on_segment(a,b,p):
    return cross(a,b,p)==0 and min(a[0],b[0])<=p[0]<=max(a[0],b[0]) and min(a[1],b[1])<=p[1]<=max(a[1],b[1])


def inside(p, polygon):
    winding = 0
    for a,b in zip(polygon,polygon[1:]+polygon[:1]):
        if on_segment(a,b,p):
            return False
        if a[1] <= p[1] < b[1] and cross(a,b,p)>0:
            winding += 1
        elif b[1] <= p[1] < a[1] and cross(a,b,p)<0:
            winding -= 1
    return winding != 0


def world(point,plane,scale):
    x,y=point
    return ((x*scale,y*scale,3*scale) if plane==0 else (5*scale,x*scale,y*scale) if plane==1 else
            ((x+y)*scale,(x-y)*scale,(2*x+3*y+7)*scale))


def records(data):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig')))
    output,current=[],[]
    for tag in loader:
        if tag.code==0:
            if current and current[0][1]=='3DFACE':
                output.append(current)
            current=[]
        current.append((tag.code,cast_tag_value(tag.code,tag.value)))
    result=[]
    for record in output:
        identity=[(c,v) for c,v in record if c in (5,330)]
        require([c for c,_ in identity]==[5,330] and all(int(v,16)>0 for _,v in identity),'Identity framing')
        result.append([(c,v) for c,v in record if c not in (5,330)])
    return result


def validate(packets, polygon, reverse, points):
    n=len(polygon);require(len(packets)==n-2,'Triangle inventory')
    indices={p:i for i,p in enumerate(points)}
    boundary={tuple(sorted((i,(i+1)%n))) for i in range(n)}
    incidence=defaultdict(list);area=0;triangle_count=0
    for packet in packets:
        require(len(packet)==25 and packet[:10]==PREFIX and packet[-2:]==SUFFIX,'Packet framing/metadata')
        corners=[]
        for k in range(4):
            values=packet[10+3*k:13+3*k]
            require([c for c,_ in values]==[10+k,20+k,30+k],'Corner framing')
            p=tuple(v for _,v in values)
            require(p in indices,'Invented or modified source vertex')
            corners.append(indices[p])
        a,b,c,d=corners
        require(c==d and len({a,b,c})==3,'Triangle repeated corner')
        expected_flags=4
        for u,v,flag in ((a,b,1),(b,c,2),(c,a,8)):
            edge=tuple(sorted((u,v)))
            if edge not in boundary:
                expected_flags |= flag
            incidence[edge].append((u,v))
        require(packet[22]==(70,expected_flags),'Hidden boundary or visible diagonal')
        signed=cross(polygon[a],polygon[b],polygon[c])
        require(signed<0 if reverse else signed>0,'Triangle winding/area')
        area+=signed;triangle_count+=1
        centroid=tuple(Fraction(polygon[a][j]+polygon[b][j]+polygon[c][j],3) for j in (0,1))
        require(inside(centroid,polygon),'Triangle centroid outside polygon')
    require(boundary.issubset(incidence),'Missing outline edges')
    for edge,directions in incidence.items():
        if edge in boundary:
            require(len(directions)==1,'Repeated outline edge')
        else:
            require(len(directions)==2 and directions[0]==directions[1][::-1],'Interior edge incidence')
            a,b=(polygon[i] for i in edge)
            midpoint=tuple(Fraction(a[j]+b[j],2) for j in (0,1))
            require(inside(midpoint,polygon),'Interior diagonal leaves polygon')
            require(not any(on_segment(a,b,p) for i,p in enumerate(polygon) if i not in edge),'Diagonal skips boundary vertex')
    edges=list(incidence)
    for i,(a,b) in enumerate(edges):
        for c,d in edges[i+1:]:
            if {a,b}&{c,d}:
                continue
            a0,b0,c0,d0=(polygon[j] for j in (a,b,c,d))
            require(not (cross(a0,b0,c0)*cross(a0,b0,d0)<0 and cross(c0,d0,a0)*cross(c0,d0,b0)<0),'Crossing triangle edges')
    expected=sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(polygon,polygon[1:]+polygon[:1]))
    require(area==(-expected if reverse else expected),'Exact polygon area coverage')
    return triangle_count


def rejected(function):
    try:
        function()
    except ValueError:
        return 1
    raise AssertionError('Actual-output corruption escaped the validator')


def main(directory):
    cases=list(itertools.product(POLYGONS,(False,True),range(3),range(3),PROFILES,(False,True)))
    name=lambda row:'mesh-decompose-'+'-'.join(map(str,row))+'.dxf'
    wanted={name(row) for row in cases};actual={p.name for p in directory.glob('mesh-decompose-*')}
    require(actual==wanted,'Exact decomposition output inventory')
    controls=rejected(lambda:require(actual-{next(iter(actual))}==wanted,'Missing output'))
    controls+=rejected(lambda:require(actual|{'mesh-decompose-extra.dxf'}==wanted,'Extra output'))
    triangles=0
    for row in cases:
        kind,reverse,plane,s,version,binary=row
        polygon=POLYGONS[kind];points=[world(p,plane,(1.,1e-200,1e200)[s]) for p in polygon]
        path=directory/name(row);data=path.read_bytes()
        require(data.startswith(b'AutoCAD Binary DXF')==binary,'Transport')
        packets=records(data);triangles+=validate(packets,polygon,reverse,points)
        for i,packet in enumerate(packets):
            controls+=rejected(lambda:validate(packets[:i]+packets[i+1:],polygon,reverse,points))
            controls+=rejected(lambda:validate(packets+[packet],polygon,reverse,points))
            for k,(code,value) in enumerate(packet):
                changed=[list(p) for p in packets]
                changed[i][k]=(code,math.nextafter(value,math.inf) if isinstance(value,float) else value+1 if isinstance(value,int) else value+'X')
                controls+=rejected(lambda:validate(changed,polygon,reverse,points))
                del changed[i][k]
                controls+=rejected(lambda:validate(changed,polygon,reverse,points))
        doc=ezdxf.readfile(path);require(doc.dxfversion==PROFILES[version],'DXF profile')
        require(all(e.dxftype()=='3DFACE' for e in doc.modelspace()),'Unexpected output entity')
        audit=doc.audit();require(not audit.errors and not audit.fixes,'Graph audit errors/repairs')
    print(f'PASS {len(cases)} drawings / {triangles} triangles; exact vertex, area, winding, incidence and noncrossing checks; {controls} corruptions rejected; zero audit errors/repairs')


if __name__=='__main__':
    main(Path(sys.argv[1]))
