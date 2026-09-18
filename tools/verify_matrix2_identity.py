#!/usr/bin/env python3
"""Apply the independent rational matrix oracle to the 2D identity consumers."""
import argparse
import json
from pathlib import Path
from verify_matrix_identity import check_row, reject, require


def check_rows(rows):
    require(isinstance(rows,list) and len(rows)==12,'Wrong Matrix2 corpus size')
    for row,(i,v) in zip(rows,((i,v) for i in range(4) for v in range(3))):
        check_row(row,2,i,v)


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path);directory=parser.parse_args().directory
    require({p.name for p in directory.glob('matrix2-identity-*')}=={'matrix2-identity-numerics.json'},'Wrong Matrix2 inventory')
    rows=json.loads((directory/'matrix2-identity-numerics.json').read_text());check_rows(rows)
    controls=0
    for row in rows:
        validate=lambda candidate:check_row(candidate,2,row['index'],row['variant'])
        for field in ('vector','product','reverse','transpose','inverse'):
            for i in range(len(row[field])):
                copy=dict(row);copy[field]=list(row[field]);copy[field][i]+=.125;controls+=reject(validate,copy)
        copy=dict(row);copy['determinant']+=.125;controls+=reject(validate,copy)
    controls+=reject(check_rows,rows[:-1]);controls+=reject(check_rows,rows+[rows[-1]])
    print(f'PASS: {len(rows)} regenerated Matrix2 scenarios; {controls} actual-output/corpus corruptions rejected')


if __name__=='__main__':
    main()
