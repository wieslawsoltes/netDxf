#!/usr/bin/env python3
"""Independently qualify STYLE flags, optional42, full font prefix and unrelated XData."""
import argparse
import hashlib
import json
import io
from pathlib import Path
import re
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

YEARS = (2000, 2004, 2007, 2010, 2013, 2018)
PROFILES = dict(zip(YEARS, ('AC1015','AC1018','AC1021','AC1024','AC1027','AC1032')))
FONT_FLAGS = -216910762
LITERAL = 'Literal \\U+0041 青😀\0\r\n'

def require(condition, message):
    if not condition: raise ValueError(message)

def records(path, year):
    data = path.read_bytes()
    tags = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8' if year >= 2007 else 'cp1252'), newline=None))
    result=[]; current=[]
    for tag in tags:
        if tag.code == 0 and current: result.append(current); current=[]
        current.append(tag)
    if current: result.append(current)
    return result

def decode(value):
    # Pair escaped UTF-16 units into Unicode scalars; reject invalid output.
    return decode_dxf_unicode(value).encode("utf-16", "surrogatepass").decode("utf-16")

def values(record, code):
    return [t.value for t in record if t.code == code]

def scalar(record, code, expected):
    found = values(record, code)
    if expected is None: require(not found, f'Unexpected group {code}: {found}'); return
    require(len(found) == 1, f'Group {code} count {len(found)}')
    actual = decode(found[0]) if isinstance(expected,str) else type(expected)(found[0])
    require(actual == expected, f'Group {code}: {actual!r} != {expected!r}')

def xdata(record, app):
    inside=False; output=[]
    for tag in record:
        if tag.code == 1001:
            if inside: break
            inside = tag.value.upper() == app.upper()
        elif inside:
            value = tag.value
            if tag.code in (1000,1003): value = decode(value)
            elif tag.code == 1004: value = bytes.fromhex(value) if isinstance(value,str) else value
            elif tag.code in (1070,1071): value = int(value)
            output.append((tag.code,value))
    return output

def named(styles, name):
    found=[r for r in styles if values(r,2) == [name]]
    require(len(found)==1, f'STYLE {name} count {len(found)}')
    return found[0]

def inspect(path):
    match=re.search(r'AutoCad(20\d\d)-(False|True)',path.name)
    require(match is not None,'Missing profile/transport marker')
    year=int(match[1]); binary=match[2]=='True'
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Wrong transport')
    doc=ezdxf.readfile(path)
    require(doc.dxfversion==PROFILES[year],'Wrong profile')
    audit=doc.audit(); require(not audit.errors and not audit.fixes,f'ezdxf audit errors/repairs: {audit.errors}, {audit.fixes}')
    styles=[r for r in records(path,year) if r[0].value=='STYLE']
    for record in styles:
        require(values(record,100)==['AcDbSymbolTableRecord','AcDbTextStyleTableRecord'],'STYLE subclass envelope')
    if '-wire-' in path.name:
        height=None if '-absent.' in path.name else 0.0 if '-zero.' in path.name else -2.75
        style=named(styles,'WIRE')
        shapes=[r for r in styles if values(r,3)==['wire-shapes.shx']]
        require(len(shapes)==1,'Shape record missing/duplicated')
        for record,flags in ((style,0x4074),(shapes[0],0x4075)):
            scalar(record,70,flags); scalar(record,71,-16362); scalar(record,42,height)
            scalar(record,40,1.25); scalar(record,41,0.75); scalar(record,50,12.5)
            require(xdata(record,'STYLE_EXTERNAL')==[(1000,'after stored STYLE fields'),(1070,31)],'STYLE external XData changed')
        points=list(doc.modelspace().query('POINT'))
        require(len(points)==1 and points[0].dxf.location.isclose((123,456,789)),'Following entity framing')
    elif '-producer-' in path.name:
        for suffix in ('','_COPY'):
            rich=named(styles,'QA_RICH'+suffix)
            scalar(rich,3,'Arial.ttf'); scalar(rich,70,0x4074); scalar(rich,71,0x4006); scalar(rich,40,1.25); scalar(rich,42,9.75)
            require(xdata(rich,'ACAD')==[(1000,'Independent 青'),(1071,FONT_FLAGS),(1002,'{'),(1000,'unrelated suffix'),(1071,73),(1004,b'\0\xff\x19'),(1002,'}')],'Complete ACAD prefix/suffix changed')
            require(xdata(rich,'STYLE_QA')==[(1000,'external style data'),(1070,19)],'External STYLE data changed')
            family, italic, bold = doc.styles.get('QA_RICH'+suffix).get_extended_font_data()
            require((decode(family),italic,bold)==('Independent 青',True,True),'Independent font interpretation')
            shx=named(styles,'QA_SHX'+suffix)
            scalar(shx,3,'asia.shx'); scalar(shx,4,'bigfont.shx'); scalar(shx,42,0.0); scalar(shx,70,128); scalar(shx,71,8)
            empty=named(styles,'QA_EMPTY'+suffix)
            scalar(empty,3,''); scalar(empty,42,0.0); require(xdata(empty,'ACAD')==[(1000,''),(1071,0)],'Explicit empty font prefix lost')
            absent=named(styles,'QA_ABSENT'+suffix)
            scalar(absent,42,None); scalar(absent,3,'extensionless')
            require(xdata(absent,'ACAD')==[(1002,'{'),(1000,'not a font'),(1071,1234),(1002,'}')],'Nonfont prefix changed')
        shapes=[r for r in styles if values(r,3)==['qa-shapes.shx']]
        require(len(shapes)==1,'Producer shape STYLE count')
        scalar(shapes[0],70,0x4175); scalar(shapes[0],71,0x4016); scalar(shapes[0],42,3.125)
        require([e.dxf.style for e in doc.modelspace().query('TEXT')]==['QA_RICH'],'TEXT reference changed')
        require([e.dxf.style for e in doc.modelspace().query('MTEXT')]==['QA_SHX'],'MTEXT reference changed')
    else:
        require('-api-' in path.name,'Unexpected artifact type')
        style=named(styles,'API')
        scalar(style,3,'fonts\\U+0042青\n.shx'); scalar(style,4,'big\\U+0043青\r.shx'); scalar(style,42,0.0)
        require(xdata(style,'ACAD')==[(1000,LITERAL),(1071,FONT_FLAGS),(1002,'{'),(1000,LITERAL),(1002,'}')],'Literal font or unrelated XData changed')

def main():
    parser=argparse.ArgumentParser(description=__doc__); parser.add_argument('directory',type=Path); args=parser.parse_args()
    sources = Path(__file__).resolve().parents[1] / 'tests' / 'fixtures' / 'style-fidelity'
    manifest = json.loads((sources / 'manifest.json').read_text())
    expected_sources = {f'ezdxf-style-R{year}-{binary}.dxf' for year in YEARS for binary in (False,True)}
    require(manifest['producer'] == 'ezdxf 1.4.4' and set(manifest['files']) == expected_sources, 'Independent fixture provenance')
    for name, digest in manifest['files'].items():
        source = sources / name
        require(hashlib.sha256(source.read_bytes()).hexdigest() == digest, 'Independent fixture hash changed: ' + name)
        year = int(re.search(r'R(20\d\d)',name)[1])
        require(source.read_bytes().startswith(b'AutoCAD Binary DXF') == name.endswith('-True.dxf'), 'Independent source transport')
        require(ezdxf.readfile(source).dxfversion == PROFILES[year], 'Independent source profile')
    expected=set()
    for year in YEARS:
        for binary in (False,True):
            prefix=f'AutoCad{year}-{binary}'
            expected.update(f'style-fidelity-wire-{prefix}-{case}.dxf' for case in ('absent','zero','negative'))
            expected.update(f'style-fidelity-producer-{prefix}-{cycle}.dxf' for cycle in (0,1))
            expected.add(f'style-fidelity-api-{prefix}.dxf')
    actual={p.name for p in args.directory.glob('style-fidelity-*.dxf')}
    require(actual==expected,f'STYLE artifact corpus mismatch: missing={sorted(expected-actual)}, unexpected={sorted(actual-expected)}')
    for name in sorted(actual): inspect(args.directory/name)
    print(f'PASS: {len(actual)} STYLE drawings; six profiles, both transports, exact stored font data and zero ezdxf audit errors/repairs.')

if __name__=='__main__': main()
