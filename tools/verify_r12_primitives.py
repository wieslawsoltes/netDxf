#!/usr/bin/env python3
"""Independently verify typed R12 primitive exports, framing and exact physical packets."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject, audit_signature

KINDS = ('LINE', 'POINT', 'CIRCLE', 'ARC', '3DFACE', 'SOLID', 'TRACE')
STAGES = ('source', 'output', 'resave')
NORMALS = ((0., 0., 1.), (1., 0., 0.), (0., 0., -1.))


def point(code, xyz):
    return [(code, float(xyz[0])), (code+10, float(xyz[1])), (code+20, float(xyz[2]))]


def ocs(xyz, normal):
    # Independent exact bases for the three selected planes; not output-derived.
    x, y, z = xyz
    return (x, y, z) if normal == 0 else (y, z, x) if normal == 1 else (-x, y, -z)


def expected(normal):
    tags = [(0,'SECTION'), (2,'HEADER'), (9,'$ACADVER'), (1,'AC1009'),
            (9,'$DWGCODEPAGE'), (3,'ANSI_1252'), (9,'$HANDSEED'), (5,'107'),
            (0,'ENDSEC'), (0,'SECTION'), (2,'TABLES'), (0,'TABLE'), (2,'LTYPE'), (70,1),
            (0,'LTYPE'), (2,'CONTINUOUS'), (70,0), (3,'Solid line'), (72,65), (73,0), (40,0.),
            (0,'ENDTAB'), (0,'TABLE'), (2,'LAYER'), (70,2),
            (0,'LAYER'), (2,'0'), (70,0), (62,7), (6,'CONTINUOUS'),
            (0,'LAYER'), (2,'SHAPES'), (70,5), (62,2), (6,'CONTINUOUS'),
            (0,'ENDTAB'), (0,'ENDSEC'), (0,'SECTION'), (2,'BLOCKS'), (0,'ENDSEC'),
            (0,'SECTION'), (2,'ENTITIES')]
    for index, kind in enumerate(KINDS):
        tags += [(0,kind), (5,f'{256+index:X}'), (8,'SHAPES'),
                 (6,('BYLAYER','BYBLOCK','CONTINUOUS')[index%3]), (62,index+1)]
        plane = point(210, NORMALS[normal])
        if kind == 'LINE':
            tags += point(10,(-0.,2.,3.)) + point(11,(4.,5.,6.)) + [(39,-2.)] + plane
        elif kind == 'POINT':
            tags += point(10,(7.,8.,9.)) + [(39,1.5)] + plane + [(50,330.)]
        elif kind == 'CIRCLE':
            tags += point(10,ocs((10.,20.,30.),normal)) + [(40,2.), (39,-1.)] + plane
        elif kind == 'ARC':
            tags += point(10,ocs((11.,21.,31.),normal)) + [(40,3.), (39,2.)] + plane + [(50,15.), (51,275.)]
        elif kind == '3DFACE':
            for code, xyz in zip((10,11,12,13), ((0,0,0),(2,0,1),(2,3,2),(0,3,1))): tags += point(code,xyz)
            tags += [(70,5)]
        else:
            elevation, thickness = (4.,.5) if kind == 'SOLID' else (-2.,-.5)
            for code, xy in zip((10,11,12,13), ((0,0),(2,0),(0,3),(2,3))): tags += point(code,(*xy,elevation))
            tags += [(39,thickness)] + plane
    return tags + [(0,'ENDSEC'), (0,'EOF')]


def same(wanted, actual):
    require(list(map(key,wanted)) == list(map(key,actual)), 'R12 physical packet differs from independent specification')


def independent(path, normal):
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == 'AC1009', 'Independent version detection')
    entities = list(doc.modelspace())
    require(tuple(e.dxftype() for e in entities) == KINDS, 'Independent typed inventory')
    require([e.dxf.handle for e in entities] == [f'{256+i:X}' for i in range(7)], 'Independent identities')
    require(all(e.dxf.layer == 'SHAPES' and e.dxf.color == i+1 for i,e in enumerate(entities)), 'Independent common fields')
    require(tuple(entities[0].dxf.start) == (-0.,2.,3.) and tuple(entities[0].dxf.end) == (4.,5.,6.), 'Independent LINE')
    require(struct.pack('>d',entities[0].dxf.start.x) == struct.pack('>d',-0.), 'Independent signed zero')
    require(tuple(entities[1].dxf.location) == (7.,8.,9.) and entities[1].dxf.angle == 330., 'Independent POINT')
    for index, center, radius in ((2,(10.,20.,30.),2.), (3,(11.,21.,31.),3.)):
        require(tuple(entities[index].ocs().to_wcs(entities[index].dxf.center)) == center
                and entities[index].dxf.radius == radius, 'Independent OCS/WCS circle geometry')
    require(entities[3].dxf.start_angle == 15. and entities[3].dxf.end_angle == 275., 'Independent ARC angles')
    require(entities[4].dxf.invisible_edges == 5, 'Independent face edge flags')
    require(doc.layers.get('SHAPES').dxf.flags == 5 and doc.layers.get('SHAPES').dxf.color == 2, 'Independent layer settings')
    require(not any(any(counts.values()) for counts in audit_signature(doc)), 'Independent R12 audit errors or repairs')


def main(directory):
    inventory = {f'r12-primitives-{n}-{transport}-{stage}.dxf'
                 for n,transport,stage in itertools.product(range(3),('text','binary'),STAGES)}
    def check_inventory(actual): require(actual == inventory, 'Missing/extra R12 primitive drawings')
    check_inventory({p.name for p in directory.glob('r12-primitives-*.dxf')})
    rejected = reject(lambda:check_inventory(inventory - {next(iter(inventory))}))
    rejected += reject(lambda:check_inventory(inventory | {'extra.dxf'}))
    for normal, transport, stage in itertools.product(range(3),('text','binary'),STAGES):
        path = directory / f'r12-primitives-{normal}-{transport}-{stage}.dxf'
        binary = (transport == 'binary') != (stage == 'output')
        data = path.read_bytes()
        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'R12 transport mismatch')
        if binary:
            require(data[:22] == b'AutoCAD Binary DXF\r\n\x1a\0' and data[22:31] == b'\0SECTION\0', 'R12 one-byte group-code framing')
        wanted, actual = expected(normal), load_tags(path)
        same(wanted,actual); independent(path,normal)
        for index,(code,value) in enumerate(actual):
            for mode in ('change','remove','duplicate'):
                changed=list(actual)
                if mode == 'remove': changed.pop(index)
                elif mode == 'duplicate': changed.insert(index,changed[index])
                else: changed[index]=(code,value+'_bad' if isinstance(value,str) else value+1)
                rejected += reject(lambda:same(wanted,changed))
    print(f'PASS: {len(inventory)} R12 drawings / {len(inventory)*len(KINDS)} typed primitives; '
          f'{rejected} altered-packet/inventory controls rejected; exact framing, fields, identities and zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv)==2, 'Usage: verify_r12_primitives.py ARTIFACTS')
    main(Path(sys.argv[1]))
