#!/usr/bin/env python3
"""Check complete selected conversion packets, grid incidence and independent audits.

The fixed 4x5 WCS grid is constructed here, not read from a netDxf result file.
This checks unsmoothed wire geometry; smoothed conversion uses model regressions.
Identity values are excluded but their framing and the full graph are checked.
"""
import io
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

PROFILES = dict(zip(('AutoCad2000','AutoCad2004','AutoCad2007','AutoCad2010','AutoCad2013','AutoCad2018'),
                    ('AC1015','AC1018','AC1021','AC1024','AC1027','AC1032')))
POINTS = [(i%4,i//4,i*.125) for i in range(20)]
COMMON = [(100,'AcDbEntity'),(67,0),(8,'GRID_CONVERSION'),(62,3),(6,'ByLayer'),(370,35),(48,1.75),(60,1)]
SUFFIX = [(1001,'GRID_CONVERSION'),(1000,'independent')]


def require(ok, message):
    if not ok: raise ValueError(message)


def expected(closure, mesh):
    # A cell connects cyclic row/column neighbours. Closing both dimensions
    # includes the bottom-right toroidal seam cell, not merely the two strips.
    rows=range(5 if closure&2 else 4); cols=range(4 if closure&1 else 3)
    faces=[(r*4+c,r*4+(c+1)%4,((r+1)%5)*4+(c+1)%4,((r+1)%5)*4+c) for r in rows for c in cols]
    if mesh:
        packet=[(0,'MESH')]+COMMON+[(100,'AcDbSubDMesh'),(71,2),(72,0),(91,0),(92,20)]
        for point in POINTS: packet.extend(zip((10,20,30),point))
        packet.append((93,len(faces)*5))
        for face in faces: packet.extend((90,i) for i in (4,*face))
        packet.extend([(94,0),(95,0),(90,0)]+SUFFIX)
        return [packet]
    packets=[]
    for face in faces:
        packet=[(0,'3DFACE')]+COMMON+[(100,'AcDbFace')]
        for corner,index in enumerate(face):packet.extend(zip((10+corner,20+corner,30+corner),POINTS[index]))
        packets.append(packet+[(70,0)]+SUFFIX)
    return packets


def records(data):
    loader=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    records=[]; current=[]
    for tag in loader:
        if tag.code==0:
            if current and current[0][1] in ('MESH','3DFACE'):records.append(current)
            current=[]
        current.append((tag.code,cast_tag_value(tag.code,tag.value)))
    result=[]
    for record in records:
        identities=[(c,v) for c,v in record if c in (5,330)]
        require([c for c,_ in identities]==[5,330] and all(int(v,16)>0 for _,v in identities),'Identity framing')
        result.append([(c,v) for c,v in record if c not in (5,330)])
    return result


def validate(actual, wanted):
    require(actual==wanted,'Conversion packet geometry, seam, order or appearance differs')


def rejected(actual, wanted):
    try: validate(actual,wanted)
    except ValueError:return 1
    raise AssertionError('Corrupted conversion passed positive validator')


def main():
    directory=Path(sys.argv[1]); inventory={}
    for version,binary,closure,mesh in itertools.product(PROFILES,(False,True),range(4),(False,True)):
        if mesh and version in ('AutoCad2000','AutoCad2004','AutoCad2007'):continue
        inventory[f'grid-conversion-{version}-{binary}-{closure}-{mesh}.dxf']=(version,binary,closure,mesh)
    require({p.name for p in directory.glob('grid-conversion-*')}==set(inventory),'Exact conversion inventory')
    controls=0; cells=0
    for name,(version,binary,closure,mesh) in inventory.items():
        data=(directory/name).read_bytes();require(data.startswith(b'AutoCAD Binary DXF')==binary,'Transport')
        packets=records(data);wanted=expected(closure,mesh);validate(packets,wanted)
        cells+=(4 if closure&1 else 3)*(5 if closure&2 else 4)
        for r,packet in enumerate(packets):
            controls+=rejected(packets[:r]+packets[r+1:],wanted)
            controls+=rejected(packets+[packet],wanted)
            for i,(code,value) in enumerate(packet):
                changed=[list(p) for p in packets]
                changed[r][i]=(code,value+.125 if isinstance(value,(int,float)) else str(value)+'!')
                controls+=rejected(changed,wanted)
                changed=[list(p) for p in packets]; del changed[r][i]
                controls+=rejected(changed,wanted)
        doc=ezdxf.readfile(directory/name);require(doc.dxfversion==PROFILES[version],'Profile')
        audit=doc.audit();require(not audit.errors and not audit.fixes,'Graph requires errors/repairs')
    print(f'PASS {len(inventory)} drawings / {cells} grid cells, {controls} actual-packet corruptions rejected; zero audit errors/repairs')


if __name__=='__main__':main()
