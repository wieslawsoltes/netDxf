#!/usr/bin/env python3
"""Check composite-Bezier parameterization with independent ezdxf evaluation."""
import argparse
import math
from pathlib import Path
import ezdxf
from ezdxf.math import BSpline

def endpoint(i): return (5*i,i*i,2*i)
def add(a,b): return tuple(x+y for x,y in zip(a,b))
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def expected(i,degree):
    p0,p3=endpoint(i),endpoint(i+1)
    if degree==2:
        p1=add(p0,(2,3,4));return tuple((a+2*b+c)/4 for a,b,c in zip(p0,p1,p3))
    p1,p2=add(p0,(1,2,3)),sub(p3,(2,3,-1));return tuple((a+3*b+3*c+d)/8 for a,b,c,d in zip(p0,p1,p2,p3))
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('artifacts',type=Path);args=p.parse_args()
    paths=sorted(args.artifacts.glob('bezier-knots-*.dxf'))
    if len(paths)!=24:raise ValueError('Expected 24 version/transport/degree files')
    for path in paths:
        doc=ezdxf.readfile(path);splines=list(doc.modelspace().query('SPLINE'))
        if len(splines)!=2:raise ValueError('Original and clone required')
        for spline in splines:
            # construction_tool() rounds knots to dxf.knot_tolerance. Evaluate
            # the actual imported knot values to test their parameterization.
            degree=spline.dxf.degree
            curve=BSpline(spline.control_points, order=degree+1, knots=spline.knots, weights=spline.weights)
            for i in range(3):
                if math.dist(curve.point((i+.5)/3),expected(i,degree))>1e-10:raise ValueError(f'{path}: unequal Bezier span {i}')
            if math.dist(curve.point(0),endpoint(0))>1e-10 or math.dist(curve.point(1),endpoint(3))>1e-10:raise ValueError('Curve endpoints changed')
        audit=doc.audit()
        if audit.errors or audit.fixes:raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print('PASS',path.name)
    print(f'PASS ezdxf {ezdxf.__version__}: 24 files / 48 splines / 144 midpoint evaluations; zero audit errors or repairs')
if __name__=='__main__':main()
