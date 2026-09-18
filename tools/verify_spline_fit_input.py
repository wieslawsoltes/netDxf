#!/usr/bin/env python3
"""Verify consumed fit points and corresponding cubic-span endpoints in physical SPLINE packets."""
import io
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

PROFILES = dict(zip(('AutoCad2000','AutoCad2004','AutoCad2007','AutoCad2010','AutoCad2013','AutoCad2018'),
                    ('AC1015','AC1018','AC1021','AC1024','AC1027','AC1032')))
POINTS = [(i*2-5, i*i-3*i, i%3-1) for i in range(5)]
def require(test, message):
    if not test: raise ValueError(message)
def packet(data):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    records, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0][1] == 'SPLINE': records.append(current)
            current = []
        current.append((tag.code, tag.value))
    require(len(records) == 1, 'Expected exactly one SPLINE')
    return records[0]
def check(tags):
    for code, expected in ((71,3),):
        require([int(v) for c,v in tags if c == code] == [expected], 'Count/degree differs')
    for code, axis in ((11,0),(21,1),(31,2)):
        require([float(v) for c,v in tags if c == code] == [p[axis] for p in POINTS], 'Fit coordinate sequence differs')
    for code, axis in ((10,0),(20,1),(30,2)):
        values = [float(v) for c,v in tags if c == code]
        require(len(values) == 16, 'Control coordinate count')
        for i in range(4):
            require(values[4*i] == POINTS[i][axis] and values[4*i+3] == POINTS[i+1][axis], 'Span endpoint differs from captured fit point')
def main(directory):
    directory=Path(directory)
    names={f'spline-fit-input-{v}-{b}.dxf' for v in PROFILES for b in (False,True)}
    require({p.name for p in directory.glob('spline-fit-input-*.dxf')} == names, 'Fixture inventory differs')
    mutations=0
    for version, profile in PROFILES.items():
        for binary in (False,True):
            path=directory/f'spline-fit-input-{version}-{binary}.dxf'; data=path.read_bytes()
            require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
            tags=packet(data); check(tags)
            for at,(code,value) in enumerate(tags):
                if code not in (11,21,31): continue
                for operation in ('change','drop','duplicate'):
                    changed=list(tags)
                    if operation == 'change': changed[at]=(code,float(value)+1)
                    elif operation == 'drop': del changed[at]
                    else: changed.insert(at,changed[at])
                    try: check(changed)
                    except ValueError: mutations+=1
                    else: raise AssertionError('Corrupted fit point escaped validator')
            document=ezdxf.readfile(path); require(document.dxfversion == profile, 'Version differs')
            audit=document.audit(); require(not audit.errors and not audit.fixes, 'Graph repair required')
    print(f'PASS: {len(names)} fit-input drawings, {len(names)*len(POINTS)} retained fit points; {mutations} packet corruptions rejected; zero graph errors/repairs')
if __name__ == '__main__': main(sys.argv[1])
