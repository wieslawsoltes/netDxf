#!/usr/bin/env python3
"""Independently regenerate bounded four-dimensional distance scenarios."""
import argparse
from decimal import Decimal, localcontext
import json
import math
from pathlib import Path


def require(condition, message):
    if not condition:
        raise ValueError(message)


def inputs(index, side):
    return [((index+1)*(17+c*12)+(side+1)*(31+c*8))%97-48 for c in range(4)]


def check_row(row, index):
    require(set(row)=={'index','a','b','squared','distance'},'Wrong fields')
    a,b=inputs(index,0),inputs(index,1)
    require(row['index']==index and row['a']==a and row['b']==b,'Wrong regenerated input')
    squared=sum((x-y)**2 for x,y in zip(a,b))
    with localcontext() as context:
        context.prec=100
        distance=float(Decimal(squared).sqrt())
    require(row['squared']==squared,'Incorrect exact integer square distance')
    require(math.isfinite(row['distance']) and abs(row['distance']-distance)<=math.ulp(distance),'Incorrect distance')


def check_rows(rows):
    require(isinstance(rows,list) and len(rows)==512,'Wrong corpus size')
    for i,row in enumerate(rows):
        check_row(row,i)


def rejected(fn,value):
    try:
        fn(value)
    except (ValueError,TypeError):
        return 1
    raise AssertionError('Actual-output corruption escaped')


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);directory=p.parse_args().directory
    require({p.name for p in directory.glob('vector4-components-*')}=={'vector4-components-numerics.json'},'Wrong fixture inventory')
    rows=json.loads((directory/'vector4-components-numerics.json').read_text());check_rows(rows)
    controls=0
    for i,row in enumerate(rows):
        for field in ('squared','distance'):
            copy=dict(row);copy[field]+=.25;controls+=rejected(lambda v:check_row(v,i),copy)
        copy=dict(row);copy['a']=row['a'][:3];controls+=rejected(lambda v:check_row(v,i),copy)
    controls+=rejected(check_rows,rows[:-1]);controls+=rejected(check_rows,rows+[rows[-1]])
    print(f'PASS: {len(rows)} independently regenerated 4D cases; {controls} output/input/corpus corruptions rejected')


if __name__=='__main__':
    main()
