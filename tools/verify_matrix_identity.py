#!/usr/bin/env python3
"""Independent exact-rational oracle for the near-identity matrix corpus."""
import argparse
from fractions import Fraction
import json
import math
from pathlib import Path
import struct


def require(condition, message):
    if not condition:
        raise ValueError(message)


def bits(value):
    return struct.pack('>d', value)


def product(a, b, n, columns):
    return [float(sum((Fraction(a[r*n+k])*Fraction(b[k*columns+c]) for k in range(n)), Fraction()))
            for r in range(n) for c in range(columns)]


def check_row(row, n, index, variant):
    keys = {'size','index','variant','input','point','other','vector','product','reverse','transpose','inverse','determinant'}
    require(set(row) == keys, 'Wrong row fields')
    require((row['size'], row['index'], row['variant']) == (n,index,variant), 'Wrong scenario identity')
    a = [float(r == c) for r in range(n) for c in range(n)]
    a[index] += (math.ldexp(1., -45), -math.ldexp(1., -45), 1e-13)[variant]
    p = [math.ldexp(i+1 if i%2 == 0 else -i-1, 40) for i in range(n)]
    b = [float(i%7-3) for i in range(n*n)]
    inverse = [float(r == c) for r in range(n) for c in range(n)]
    diagonal = index//n == index%n
    inverse[index] = float(1/Fraction(a[index])) if diagonal else -a[index]
    expected = {'input':a,'point':p,'other':b,'vector':product(a,p,n,1),'product':product(a,b,n,n),
                'reverse':product(b,a,n,n),'transpose':[a[c*n+r] for r in range(n) for c in range(n)],'inverse':inverse}
    for name, values in expected.items():
        require(isinstance(row[name],list) and len(row[name]) == len(values), name + ' length')
        for actual, wanted in zip(row[name], values):
            require(isinstance(actual,(int,float)) and math.isfinite(actual), name + ' nonfinite')
            # Cofactor inverse retains ordinary floating-point rounding. All other
            # operations in this single-entry corpus have exact rational expectations.
            if name == 'inverse':
                require(abs(actual-wanted) <= 4*math.ulp(wanted) if wanted else actual == 0, 'Inverse differs')
            else:
                require(actual == wanted, name + ' exact arithmetic differs')
    require(row['determinant'] == (a[index] if diagonal else 1.), 'Determinant differs')


def reject(fn, value):
    try:
        fn(value)
    except (ValueError, TypeError):
        return 1
    raise AssertionError('Corrupted actual output was accepted')


def check_rows(rows):
    cases = [(n,i,v) for n in (3,4) for i in range(n*n) for v in range(3)]
    require(isinstance(rows,list) and len(rows) == len(cases), 'Wrong corpus length')
    for row,case in zip(rows,cases):
        check_row(row,*case)


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path)
    directory=parser.parse_args().directory
    names={p.name for p in directory.glob('matrix-identity-*')}
    require(names == {'matrix-identity-numerics.json'}, 'Wrong matrix fixture inventory')
    rows=json.loads((directory/'matrix-identity-numerics.json').read_text());check_rows(rows)
    controls=0
    for row in rows:
        validate=lambda candidate: check_row(candidate,row['size'],row['index'],row['variant'])
        for field in ('vector','product','reverse','transpose','inverse'):
            for i in range(len(row[field])):
                copy=dict(row);copy[field]=list(row[field]);copy[field][i] += .125
                controls += reject(validate,copy)
        copy=dict(row);copy['determinant']+=.125;controls+=reject(validate,copy)
    controls+=reject(check_rows,rows[:-1]);controls+=reject(check_rows,rows+[rows[-1]])
    print(f'PASS: {len(rows)} regenerated near-identity scenarios, {controls} actual-output/corpus corruptions rejected')


if __name__ == '__main__':
    main()
