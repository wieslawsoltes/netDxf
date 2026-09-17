#!/usr/bin/env python3
"""Independently check LINE affine WCS endpoints, signed extrusion and rollback outputs.

Fraction arithmetic supplies exact once-rounded binary64 dot products; geometric
normal/thickness checks have relative, scale-aware tolerances. This is a scoped
synthetic corpus, not native AutoCAD execution or visual qualification.
"""
import argparse
from fractions import Fraction
import io
import json
import math
from pathlib import Path
import struct

import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from verify_legacy_utilities import PROFILES
from verify_mleader_inputs import check

I = ((1,0,0),(0,1,0),(0,0,1))
MODES = {
    'identity': I, 'translate': I,
    'uniform': ((2,0,0),(0,2,0),(0,0,2)),
    'nonuniform': ((2,0,0),(0,3,0),(0,0,4)),
    'negative': ((-2,0,0),(0,-3,0),(0,0,-4)),
    'shear': ((1,2,3),(0,1,.5),(0,0,1)),
    'mirror': ((-1,0,0),(0,1,0),(0,0,1)),
    'rotate': ((0,-1,0),(1,0,0),(0,0,1)),
    'planar': ((1,0,0),(0,1,0),(0,0,0)),
    'collapse': ((0,0,0),(0,0,0),(0,0,0)),
    'tiny': ((1e-200,0,0),(0,1e-200,0),(0,0,1e-200)),
    'huge': ((1e200,0,0),(0,1e200,0),(0,0,1e200)),
}
START, END = (3.,-4.,5.), (7.,2.,-11.)
GEOMETRY = {10,20,30,11,21,31,39,210,220,230}
PROXY = {92,160,310}


def inventory(directory):
    expected = {f'line-affine-review-{mode}-{normal}-{thickness}-{version}-{binary}.dxf'
                for mode in MODES for normal in range(3) for thickness in range(3)
                for version in PROFILES for binary in (False,True)}
    check({p.name for p in directory.glob('line-affine-review-*.dxf')} == expected,
          'LINE fixture inventory differs: missing or extra output')
    return expected


def exact_dot(row, point, translation=0):
    return float(sum((Fraction(a)*Fraction(b) for a,b in zip(row,point)), Fraction(translation)))


def image(matrix, point, translation=(0,0,0)):
    return tuple(exact_dot(row,point,t) for row,t in zip(matrix,translation))


def exact(actual, expected, label):
    # Zero signs are a separately tested in-memory identity contract. Arithmetic
    # zero cancellation is canonical +0; DXF semantic zero need not retain sign.
    check(actual == expected and math.isfinite(actual), label)


def near(actual, expected, label):
    check(math.isfinite(actual), label+' finite')
    if expected == 0:
        check(abs(actual) <= 8*math.ulp(0.0), label+' zero')
    else:
        check(abs((actual-expected)/expected) <= 4e-13, label)


def raw_lines(path, binary):
    data=path.read_bytes()
    check(data.startswith(b'AutoCAD Binary DXF') == binary, 'LINE transport differs')
    tags=list(binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode('ascii'))))
    result=[]; section=None; current=None
    for index,tag in enumerate(tags):
        if tag.code==0 and tag.value=='SECTION': section=tags[index+1].value
        elif tag.code==0 and tag.value=='ENDSEC': section=None;current=None
        elif section=='ENTITIES':
            if tag.code==0:
                current=[] if tag.value=='LINE' else None
                if current is not None: result.append(current)
            if current is not None and tag.code not in (5,330):
                value=(bytes.fromhex(tag.value) if isinstance(tag.value,str) else tag.value) if tag.code==310 else cast_tag_value(tag.code,tag.value)
                current.append((tag.code,value))
    check(len(result)==2, 'LINE raw record inventory differs')
    return result


def scalar(record,code):
    matches=[value for actual,value in record if actual==code]
    check(len(matches)==1, 'LINE missing or duplicate field '+str(code))
    return matches[0]


def geometry(record):
    return {code:scalar(record,code) for code in GEOMETRY}


def verify_pair(records,mode,normal,thickness):
    check(len(records)==2, 'LINE pair cardinality')
    source,target=records
    for record in records:
        check(scalar(record,0)=='LINE', 'LINE kind')
        for code,value in ((8,'LINE_AFFINE_LAYER'),(62,3),(60,1),(48,1.75),(1001,'LINE_AFFINE'),(1000,f'line-{normal}-{thickness}')):
            check(scalar(record,code)==value, 'LINE common metadata '+str(code))
        check([v for c,v in record if c==100]==['AcDbEntity','AcDbLine'], 'LINE subclasses')
    source_proxy=[(c,v) for c,v in source if c in PROXY]
    check(len(source_proxy)==2 and source_proxy[0][0] in (92,160) and source_proxy[0][1]==5 and
          source_proxy[1]==(310,bytes((7,31,83,normal,thickness))), 'LINE source proxy')
    target_proxy=[(c,v) for c,v in target if c in PROXY]
    check(target_proxy==(source_proxy if mode=='identity' else []), 'LINE stale/missing target proxy')
    # Every other ordered field is invariant under this geometric operation.
    check([(c,v) for c,v in source if c not in GEOMETRY|PROXY] ==
          [(c,v) for c,v in target if c not in GEOMETRY|PROXY], 'LINE unrelated record changed')
    s,t=geometry(source),geometry(target)
    for code,value in zip((10,20,30,11,21,31),START+END): exact(s[code],value,'LINE source coordinate')
    normal_value=((0.,0.,1.),(1.,0.,0.),tuple(i/math.sqrt(14) for i in (1.,2.,3.)))[normal]
    for code,value in zip((210,220,230),normal_value): near(s[code],value,'LINE source normal')
    exact(s[39],(thickness-1)*1.75,'LINE source thickness')
    translation=(11,-7,13) if mode in ('translate','collapse') else (0,0,0)
    for code,value in zip((10,20,30,11,21,31),image(MODES[mode],START,translation)+image(MODES[mode],END,translation)):
        exact(t[code],value,'LINE once-rounded WCS image')
    actual_source_normal=tuple(s[code] for code in (210,220,230))
    transformed=image(MODES[mode],actual_source_normal)
    length=math.hypot(*transformed)
    expected_normal=tuple(v/length for v in transformed) if length else actual_source_normal
    for code,value in zip((210,220,230),expected_normal): near(t[code],value,'LINE normal direction')
    near(math.hypot(*(t[c] for c in (210,220,230))),1.,'LINE unit normal')
    near(t[39],s[39]*length,'LINE signed thickness')
    for code,value in zip((210,220,230),image(MODES[mode],tuple(v*s[39] for v in actual_source_normal))):
        near(t[code]*t[39],value,'LINE physical extrusion image')


def rejects(action,label):
    try: action()
    except ValueError: return 1
    raise AssertionError('LINE corruption escaped checker: '+label)


def corrupt(value):
    if isinstance(value,str): return value+'_corrupt'
    if isinstance(value,bytes): return value+b'\x7f'
    if isinstance(value,float): return value*1.01 if value else .25
    return value+1


def verify_exact_corpus(directory):
    rows=json.loads((directory/'line-affine-exact-dot.json').read_text())
    check(len(rows)==512, 'Exact dot corpus cardinality')
    controls=0
    def validate(row):
        check(set(row)=={'matrix','point','translation','result'}, 'Exact dot schema')
        check(len(row['matrix'])==9 and all(len(row[k])==3 for k in ('point','translation','result')), 'Exact dot shape')
        for r in range(3):
            expected=exact_dot(row['matrix'][3*r:3*r+3],row['point'],row['translation'][r])
            check(struct.pack('>d',row['result'][r])==struct.pack('>d',expected), 'Exact dot bits')
    for row in rows:
        validate(row)
        for index in range(3):
            value=row['result'][index]
            row['result'][index]=math.nextafter(value,math.inf)
            controls+=rejects(lambda:validate(row),'one-ULP exact dot')
            row['result'][index]=value
    return controls


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path)
    directory=parser.parse_args().directory; expected=inventory(directory);controls=0
    for mode in MODES:
        for normal in range(3):
            for thickness in range(3):
                for version,profile in PROFILES.items():
                    for binary in (False,True):
                        path=directory/f'line-affine-review-{mode}-{normal}-{thickness}-{version}-{binary}.dxf'
                        records=raw_lines(path,binary);verify_pair(records,mode,normal,thickness)
                        document=ezdxf.readfile(path)
                        check(document.dxfversion==profile,'LINE DXF profile')
                        check([e.dxftype() for e in document.modelspace()]==['LINE','LINE'],'LINE modelspace inventory')
                        audit=document.audit();check(not audit.errors and not audit.fixes,'LINE audit needs repairs')
                        for record in records:
                            for index,(code,value) in enumerate(record):
                                record[index]=(code,corrupt(value))
                                controls+=rejects(lambda:verify_pair(records,mode,normal,thickness),str(code))
                                record[index]=(code,value)
                        controls+=rejects(lambda:verify_pair(records[:1],mode,normal,thickness),'missing LINE')
                        if mode!='identity':
                            records[1].extend([(160,5),(310,bytes((7,31,83,normal,thickness)))])
                            controls+=rejects(lambda:verify_pair(records,mode,normal,thickness),'stale proxy reintroduced')
    dots=verify_exact_corpus(directory)
    print(f'PASS ezdxf {ezdxf.__version__}: {len(expected)} LINE drawings / {2*len(expected)} entities; '
          f'zero audit errors/repairs; {controls} actual-record corruptions rejected; '
          f'512 exact arithmetic scenarios / {3*512} bit comparisons / {dots} one-ULP corruptions rejected')

if __name__=='__main__': main()
