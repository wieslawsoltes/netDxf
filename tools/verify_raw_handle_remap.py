#!/usr/bin/env python3
"""Validate remapped drawing geometry/identities using independent ezdxf."""
import argparse
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args()
    files=sorted(args.artifacts.glob('handle-remap-*.dxf'))
    expected={f'handle-remap-AutoCad{v}-{b}.dxf' for v in (2000,2004,2007,2010,2013,2018) for b in ('False','True')}
    if {p.name for p in files}!=expected:raise ValueError('Expected exactly 12 remapped drawings')
    for path in files:
        doc=ezdxf.readfile(path);lines=list(doc.modelspace().query('LINE'));circles=list(doc.modelspace().query('CIRCLE'))
        if len(lines)!=1 or len(circles)!=1:raise ValueError(f'{path}: entity count')
        if tuple(lines[0].dxf.start)!=(1,2,3) or tuple(lines[0].dxf.end)!=(4,5,6) or tuple(circles[0].dxf.center)!=(7,8,9) or circles[0].dxf.radius!=2.5:raise ValueError(f'{path}: geometry changed')
        # ezdxf creates optional default objects during load, starting AT the
        # original HANDSEED, without rewriting the header yet. Check authored
        # wire identities, not the augmented object database's maximum handle.
        data=path.read_bytes()
        if data.startswith(b'AutoCAD Binary DXF'):
            tags=list(binary_tags_loader(data))
        else:
            with path.open(encoding='utf-8') as source:tags=list(ascii_tags_loader(source))
        handles=[];section=None;seed=None
        for i,tag in enumerate(tags):
            if tag.code==0 and tag.value=='SECTION':section=tags[i+1].value
            elif tag.code==0 and tag.value=='ENDSEC':section=None
            elif section=='HEADER' and tag.code==9 and tag.value=='$HANDSEED':seed=int(tags[i+1].value,16)
            elif section in ('TABLES','BLOCKS','ENTITIES','OBJECTS') and tag.code in (5,105):handles.append(int(tag.value,16))
        if not handles or min(handles)<4096 or len(handles)!=len(set(handles)):raise ValueError(f'{path}: identities not remapped uniquely')
        if seed is None or seed<=max(handles):raise ValueError(f'{path}: unsafe authored HANDSEED')
        loaded={int(e.dxf.handle,16) for e in doc.entitydb.values() if e.dxf.hasattr('handle')}
        if not set(handles).issubset(loaded):raise ValueError(f'{path}: authored identity dropped by semantic reader')
        audit=doc.audit()
        if audit.errors or audit.fixes:raise ValueError(f'{path}: {len(audit.errors)} errors/{len(audit.fixes)} repairs')
        print('PASS',path.name)
    print(f'PASS ezdxf {ezdxf.__version__}: 12 remapped drawings; zero audit errors/repairs')
if __name__=='__main__':main()
