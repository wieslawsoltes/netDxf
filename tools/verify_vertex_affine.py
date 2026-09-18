#!/usr/bin/env python3
"""Independent exact WCS-coordinate and ordered-packet check for MESH/3DFACE."""
from fractions import Fraction
import io
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

PROFILES = dict(zip(('AutoCad2000','AutoCad2004','AutoCad2007','AutoCad2010','AutoCad2013','AutoCad2018'),
                    ('AC1015','AC1018','AC1021','AC1024','AC1027','AC1032')))
MODES = ('identity','translate','scale','shear','reflect','rank2','collapse','tiny','huge')
POINTS = ((1,2,3),(5,-1,7),(-2,4,9),(8,6,-3))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def expected(mesh, mode, version):
    diagonal = {'scale': (2,3,4), 'reflect': (-1,1,1), 'rank2': (1,1,0),
                'collapse': (0,0,0), 'tiny': (1e-200,)*3, 'huge': (1e200,)*3}.get(mode, (1,1,1))
    matrix = [[diagonal[r] if r == c else 0 for c in range(3)] for r in range(3)]
    if mode == 'shear':
        matrix = ((1,2,.5),(0,1,.25),(0,0,1))
    translation = (7,-11,13) if mode in ('translate','collapse') else (0,0,0)
    vertices = [tuple(float(sum((Fraction(matrix[r][c])*p[c] for c in range(3)), Fraction(translation[r])))
                      for r in range(3)) for p in POINTS]
    tags = [(0,'MESH' if mesh else '3DFACE'), (100,'AcDbEntity'), (67,0), (8,'VERTEX_AFFINE'),
            (62,3), (6,'ByLayer'), (370,-1), (48,1.75), (60,1)]
    if mode == 'identity':
        tags += [(160 if version in ('AutoCad2013','AutoCad2018') else 92,4),(310,bytes((1,3,7,11)))]
    tags.append((100,'AcDbSubDMesh' if mesh else 'AcDbFace'))
    if mesh:
        tags += [(71,2),(72,1),(91,2),(92,4)]
    for i, p in enumerate(vertices):
        code = 10 if mesh else 10+i
        tags += list(zip((code, code+10, code+20), p))
    if mesh:
        tags += [(93,8)] + [(90,v) for v in (3,0,1,2,3,0,2,3)]
        tags += [(94,1),(90,0),(90,2),(95,1),(140,1.5),(90,0)]
    else:
        tags += [(70,10)]
    return tags + [(1001,'VERTEX_AFFINE'),(1000,'unchanged')]


def record(data):
    binary = data.startswith(b'AutoCAD Binary DXF')
    tags = binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig')))
    records, current = [], []
    for tag in tags:
        if tag.code == 0:
            if current and current[0][1] in ('3DFACE','MESH'):
                records.append(current)
            current = []
        current.append((tag.code, (tag.value if binary else bytes.fromhex(tag.value)) if tag.code == 310 else cast_tag_value(tag.code, tag.value)))
    require(len(records)==1, 'Selected entity inventory differs')
    handles = [(c,v) for c,v in records[0] if c in (5,330)]
    require([c for c,_ in handles] == [5,330] and all(int(v,16)>0 for _,v in handles), 'Identity framing differs')
    return [(c,v) for c,v in records[0] if c not in (5,330)]


def verify(actual, wanted):
    require(actual == wanted, 'Ordered entity packet or exact coordinate differs')


def reject(function):
    try:
        function()
    except ValueError:
        return 1
    raise AssertionError('Corruption escaped positive checker')


def main(directory):
    cases = [(mesh,mode,n,four,v,binary) for mesh,mode,n,four,v,binary in
             itertools.product((False,True),MODES,(0,1),(False,True),PROFILES,(False,True))
             if not mesh or v in ('AutoCad2010','AutoCad2013','AutoCad2018')]
    name = lambda row: 'meshface-affine-'+'-'.join(map(str,row))+'.dxf'
    wanted_names = {name(row) for row in cases}
    actual_names = {p.name for p in directory.glob('meshface-affine-*')}
    require(actual_names == wanted_names, 'Output inventory differs')
    controls = reject(lambda: require(actual_names - {next(iter(actual_names))} == wanted_names, 'Missing output'))
    controls += reject(lambda: require(actual_names | {'meshface-affine-extra.dxf'} == wanted_names, 'Extra output'))
    for row in cases:
        mesh,mode,_,_,v,binary = row
        path = directory/name(row); data = path.read_bytes()
        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
        actual = record(data); wanted = expected(mesh,mode,v); verify(actual,wanted)
        for i,(code,value) in enumerate(actual):
            changed = list(actual)
            changed[i] = (code, math.nextafter(value,math.inf) if isinstance(value,float) else
                           value+1 if isinstance(value,int) else value+b'X' if isinstance(value,bytes) else value+'X')
            controls += reject(lambda: verify(changed,wanted))
            controls += reject(lambda: verify(actual[:i]+actual[i+1:],wanted))
        document = ezdxf.readfile(path)
        require(document.dxfversion == PROFILES[v], 'Version differs')
        audit = document.audit(); require(not audit.errors and not audit.fixes, 'Graph audit errors/repairs')
    print(f'PASS {len(cases)} drawings, {len(cases)*4} exact WCS vertices; {controls} corruptions rejected; zero audit errors/repairs')

if __name__ == '__main__':
    main(Path(sys.argv[1]))
