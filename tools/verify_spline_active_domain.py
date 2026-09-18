#!/usr/bin/env python3
"""Independent Fraction Cox-de Boor oracle for nonperiodic active-domain sampling.

The corpus, control polygons, knots, weights and file inventory are regenerated
here. Production uses a local nonzero-basis/scaled-sum kernel; this oracle uses
recursive exact basis fractions across the entire control polygon. Geometry
admits eight ULP of the maximum source magnitude on each coordinate axis.
"""
from fractions import Fraction as F
from functools import lru_cache
import io
import json
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
PROFILES=dict(zip(('AutoCad2000','AutoCad2004','AutoCad2007','AutoCad2010','AutoCad2013','AutoCad2018'),
                  ('AC1015','AC1018','AC1021','AC1024','AC1027','AC1032')))
DEGREES=(1,2,3,5,10)
def require(test,message):
    if not test: raise ValueError(message)
def inputs(p,form,exponent):
    n=p+4
    controls=[(3*i+10,(17*i)%13-5,i%4-2) for i in range(n)]
    weights=[math.ldexp(1.,i%5-2) for i in range(n)]
    if form==0: knots=[i-p-2 for i in range(n+p+1)]
    elif form==3:
        knots=[0]
        for i in range(1,n+p+1): knots.append(knots[-1]+1+i%3)
    else:
        internal=[1,2,3] if form==1 else [1,1,2] if p==1 else [2,2,2]
        knots=[0]*(p+1)+internal+[4]*(p+1)
    return controls,weights,[math.ldexp(k,exponent) for k in knots]
def point(controls,weights,knots,p,u,right_endpoint=False):
    knots=[F(k) for k in knots];u=F(u)
    @lru_cache(None)
    def basis(i,d):
        if d==0:
            return F(knots[i]<u<=knots[i+1] if right_endpoint else knots[i]<=u<knots[i+1])
        left=knots[i+d]-knots[i];right=knots[i+d+1]-knots[i+1]
        return ((u-knots[i])/left*basis(i,d-1) if left else F(0))+((knots[i+d+1]-u)/right*basis(i+1,d-1) if right else F(0))
    coeff=[basis(i,p)*F(w) for i,w in enumerate(weights)];denom=sum(coeff)
    require(denom!=0,'Oracle rational pole')
    return tuple(float(sum(c*F(v[a]) for c,v in zip(coeff,controls))/denom) for a in range(3))
@lru_cache(None)
def expected(p,form,exponent,closed):
    c,w,k=inputs(p,form,exponent);start,end=k[p],k[len(c)];intervals=9 if closed else 8
    step=(end-start)/intervals
    samples=[point(c,w,k,p,end if not closed and i==8 else start+step*i,not closed and i==8) for i in range(9)]
    return samples,[8*math.ulp(float(max(abs(v[a]) for v in c))) for a in range(3)]
def check_points(actual,wanted,bounds):
    require(len(actual)==len(wanted),'Sample inventory differs')
    for a,e in zip(actual,wanted):
        require(len(a)==3,'Coordinate shape differs')
        for axis,(v,t) in enumerate(zip(a,e)):
            require(isinstance(v,(int,float)) and math.isfinite(v) and abs(v-t)<=bounds[axis],'Incorrect curve sample')
def records(data):
    load=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'),newline=None))
    result=[];current=[]
    for tag in load:
        if tag.code==0:
            if current and current[0][1] in ('SPLINE','POLYLINE','VERTEX','SEQEND'):result.append(current)
            current=[]
        current.append((tag.code,tag.value))
    return result
def check_records(data,kind):
    require([r[0][1] for r in data]==['SPLINE','POLYLINE']+['VERTEX']*9+['SEQEND'],'Entity/vertex sequence differs')
    p=2+kind%2;form=kind//2;c,w,k=inputs(p,form,0);s=data[0]
    for code,values in [(40,k),(41,w)]+[(10+10*a,[v[a] for v in c]) for a in range(3)]:
        require([float(v) for t,v in s if t==code]==values,'Stored spline source differs')
    actual=[]
    for r in data[2:-1]:
        vector=[]
        for code in (10,20,30):
            values=[float(v) for t,v in r if t==code];require(len(values)==1,'Duplicate/missing coordinate');vector.append(values[0])
        actual.append(vector)
    check_points(actual,*expected(p,form,0,False))
def rejected(check,value):
    try:check(value)
    except ValueError:return 1
    raise AssertionError('Corruption escaped positive validator')
def main(directory):
    directory=Path(directory)
    names={f'spline-active-domain-{kind}-{v}-{b}.dxf' for kind in range(4) for v in PROFILES for b in (False,True)}|{'spline-active-domain-numerics.json'}
    require({p.name for p in directory.glob('spline-active-domain-*')}==names,'Exact output inventory differs')
    rows=json.loads((directory/'spline-active-domain-numerics.json').read_text())
    keys=[(p,f,e,c) for p in DEGREES for f in range(4) for e in (-500,0,500) for c in (False,True)]
    require([(r['degree'],r['form'],r['exponent'],r['closed']) for r in rows]==keys,'Numerical input inventory differs')
    numerical=0
    for row,key in zip(rows,keys):
        wanted,bounds=expected(*key);check_points(row['points'],wanted,bounds)
        for index in range(9):
            changed=[list(v) for v in row['points']];changed[index][0]+=.125
            numerical+=rejected(lambda v:check_points(v,wanted,bounds),changed)
    mutations=0
    for kind in range(4):
        for version,profile in PROFILES.items():
            for binary in (False,True):
                path=directory/f'spline-active-domain-{kind}-{version}-{binary}.dxf';data=path.read_bytes()
                require(data.startswith(b'AutoCAD Binary DXF')==binary,'Transport differs');selected=records(data);check_records(selected,kind)
                for r in range(2,11):
                    for at,(code,value) in enumerate(selected[r]):
                        if code not in (10,20,30):continue
                        for op in ('change','drop','duplicate'):
                            changed=[list(x) for x in selected]
                            if op=='change':changed[r][at]=(code,float(value)+.125)
                            elif op=='drop':del changed[r][at]
                            else:changed[r].insert(at,changed[r][at])
                            mutations+=rejected(lambda v:check_records(v,kind),changed)
                doc=ezdxf.readfile(path);require(doc.dxfversion==profile,'Profile differs')
                audit=doc.audit();require(not audit.errors and not audit.fixes,'Independent graph repair required')
    print(f'PASS: {len(rows)} Fraction scenarios / {len(rows)*9} samples; {len(names)-1} drawings; {numerical} numerical and {mutations} physical corruptions rejected; zero graph errors/repairs')
if __name__=='__main__':main(sys.argv[1])
