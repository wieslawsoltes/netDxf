#!/usr/bin/env python3
"""Independently qualify ELLIPSE WCS point/derivative evidence, not native execution."""
from pathlib import Path
import copy
import itertools
import json
import math
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, require, reject, audit_signature

VERSIONS = {k:v for k,v in PROFILES.items() if k not in ('AutoCad12','AutoCad13','AutoCad14')}
PARAMETERS = (0., math.pi/6, math.pi/2, math.pi, 3*math.pi/2, 5*math.pi/3, 2*math.pi)
NORMALS = ((0.,0.,1.), (1.,0.,0.), (0.,0.,-1.))
BASE_X = ((1.,0.,0.), (0.,1.,0.), (-1.,0.,0.))
BASE_Y = ((0.,1.,0.), (0.,0.,1.), (0.,1.,0.))


def axes(plane):
    c,s = math.cos(math.pi/4), math.sin(math.pi/4)
    return tuple(6*(c*x+s*y) for x,y in zip(BASE_X[plane],BASE_Y[plane])), \
        tuple(2*(-s*x+c*y) for x,y in zip(BASE_X[plane],BASE_Y[plane]))


def check_rows(rows, plane):
    require(isinstance(rows,list) and len(rows)==len(PARAMETERS), 'Parameter inventory')
    a,b=axes(plane)
    for row,t in zip(rows,PARAMETERS):
        require(set(row)=={'parameter','derivatives'} and row['parameter']==t, 'Parameter identity')
        values=row['derivatives']
        require(isinstance(values,list) and len(values)==11, 'Derivative order inventory')
        ct,st=math.cos(t),math.sin(t)
        for k,vector in enumerate(values):
            require(isinstance(vector,list) and len(vector)==3, 'WCS component inventory')
            u,v=((ct,st),(-st,ct),(-ct,-st),(st,-ct))[k%4]
            expected=tuple(a[i]*u+b[i]*v+(i+1 if k==0 else 0) for i in range(3))
            require(all(type(n) in (int,float) and math.isfinite(n) and abs(n-e)<3e-13
                        for n,e in zip(vector,expected)), 'Independent derivative differs')


def check_tags(tags,plane):
    starts=[i for i,t in enumerate(tags) if t==(0,'ELLIPSE')]
    require(len(starts)==1,'Ellipse packet inventory')
    start=starts[0]; end=next(i for i in range(start+1,len(tags)) if tags[i][0]==0)
    a,_=axes(plane)
    fields=dict(zip((10,20,30),(1.,2.,3.)))
    fields.update(zip((11,21,31),a));fields.update(zip((210,220,230),NORMALS[plane]))
    fields.update({40:1/3,41:0.,42:math.tau})
    record=tags[start:end]
    for code,value in fields.items():
        actual=[v for c,v in record if c==code]
        require(len(actual)==1 and type(actual[0]) in (int,float) and math.isfinite(actual[0]) and abs(actual[0]-value)<3e-13,
                f'Wrong ellipse field {code}')
    require(not any(c in (92,160,310) for c,_ in record),'Unexpected source proxy')
    return start,end,fields


def main(directory):
    specs=list(itertools.product(VERSIONS,(False,True),range(3)))
    stems=[f'ellipse-parameter-{v}-{b}-{p}' for v,b,p in specs]
    names={s+ext for s in stems for ext in ('.dxf','.json')}
    def inventory(actual):require(actual==names,'Missing or extra ellipse parameter evidence')
    inventory({p.name for p in directory.glob('ellipse-parameter-*')})
    reject(lambda:inventory(names-{next(iter(names))}))
    reject(lambda:inventory(names|{'ellipse-parameter-extra.json'}))
    corruptions=0
    for stem,(version,binary,plane) in zip(stems,specs):
        path=directory/(stem+'.dxf');tags=load_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
        at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Profile changed')
        start,end,fields=check_tags(tags,plane)
        rows=json.loads((directory/(stem+'.json')).read_text())
        check_rows(rows,plane)
        for k in range(11):
            for axis in range(3):
                damaged=copy.deepcopy(rows);damaged[k%len(rows)]['derivatives'][k][axis]+=1
                corruptions+=reject(lambda:check_rows(damaged,plane))
        for damaged in (rows[:-1],rows+[rows[0]],list(reversed(rows))):
            corruptions+=reject(lambda:check_rows(damaged,plane))
        damaged=copy.deepcopy(rows);damaged[0]['derivatives'].pop()
        corruptions+=reject(lambda:check_rows(damaged,plane))
        damaged=copy.deepcopy(rows);damaged[0]['derivatives'][0][0]=float('nan')
        corruptions+=reject(lambda:check_rows(damaged,plane))
        for code in fields:
            at=next(i for i in range(start,end) if tags[i][0]==code)
            damaged=list(tags);damaged[at]=(code,float(tags[at][1])+1)
            corruptions+=reject(lambda:check_tags(damaged,plane))
        doc=ezdxf.readfile(path)
        require(not any(any(c.values()) for c in audit_signature(doc)),'Graph errors or repairs')
        entities=list(doc.modelspace().query('ELLIPSE'));require(len(entities)==1,'Independent entity inventory')
        for actual,row in zip(entities[0].vertices(PARAMETERS),rows):
            require(all(abs(actual[i]-row['derivatives'][0][i])<3e-13 for i in range(3)), 'Independent reader point differs')
    print(f'PASS: {len(specs)} drawings / {len(specs)*len(PARAMETERS)*11} independently checked WCS point/derivative vectors; '
          f'{corruptions} numerical/packet corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_ellipse_parameter_evaluation.py ARTIFACTS')
    main(Path(sys.argv[1]))
