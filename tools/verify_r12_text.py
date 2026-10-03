#!/usr/bin/env python3
"""Check actual R12 TEXT/STYLE exports against complete independent packets."""
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.tools.text import caret_decode
from verify_raw_line_geometry import load_tags, key, require, reject, audit_signature
from verify_r12_primitives import point, ocs, NORMALS

STAGES = ('source', 'output', 'resave')
VALUE = 'Résumé %%d ^\t'
ENCODED = 'Résumé %%d ^ ^I'


def alignment(index):
    return (index % 3, 3-index//3) if index < 12 else (index-9, 0)


def expected(normal):
    tags = [(0,'SECTION'), (2,'HEADER'), (9,'$ACADVER'), (1,'AC1009'),
            (9,'$DWGCODEPAGE'), (3,'ANSI_1252'), (9,'$HANDSEED'), (5,'110'),
            (0,'ENDSEC'), (0,'SECTION'), (2,'TABLES'), (0,'TABLE'), (2,'LTYPE'), (70,1),
            (0,'LTYPE'), (2,'CONTINUOUS'), (70,0), (3,'Solid line'), (72,65), (73,0), (40,0.),
            (0,'ENDTAB'), (0,'TABLE'), (2,'LAYER'), (70,1),
            (0,'LAYER'), (2,'0'), (70,0), (62,7), (6,'CONTINUOUS'), (0,'ENDTAB'),
            (0,'TABLE'), (2,'STYLE'), (70,2),
            (0,'STYLE'), (2,'Standard'), (70,0), (40,0.), (41,1.), (50,0.), (71,0), (3,'simplex.shx'), (4,''),
            (0,'STYLE'), (2,'ANNOTATIONS'), (70,4), (40,0.), (41,.8), (50,12.), (71,6), (42,0.), (3,'romans.shx'), (4,'bigfont.shx'),
            (0,'ENDTAB'), (0,'ENDSEC'), (0,'SECTION'), (2,'BLOCKS'), (0,'ENDSEC'),
            (0,'SECTION'), (2,'ENTITIES')]
    first = ocs((10.,20.,30.), normal)
    for index in range(15):
        horizontal, vertical = alignment(index)
        second = (first[0]+8., first[1], first[2]) if horizontal in (3,5) else first
        tags += [(0,'TEXT'), (5,f'{256+index:X}'), (8,'0'), (6,'BYLAYER'), (62,256), (1,ENCODED)]
        tags += point(10, first) + [(40,2.5), (41,1.2), (50,0.), (51,-10.), (7,'ANNOTATIONS')]
        if horizontal or vertical: tags += point(11, second)
        tags += point(210, NORMALS[normal]) + [(71, (2 if index & 1 else 0) | (4 if index & 2 else 0)),
                                           (72,horizontal), (73,vertical)]
    tags += [(0,'LINE'), (5,'10F'), (8,'0'), (6,'BYLAYER'), (62,256)]
    tags += point(10,(1.,2.,3.)) + point(11,(4.,5.,6.)) + [(39,0.)] + point(210,(0.,0.,1.))
    return tags + [(0,'ENDSEC'), (0,'EOF')]


def check_packet(wanted, actual):
    require(list(map(key,wanted)) == list(map(key,actual)), 'Unexpected R12 TEXT/STYLE field, identity, order or value')


def independent(path):
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == 'AC1009', 'Independent TEXT version')
    entities = list(doc.modelspace())
    require(len(entities)==16 and all(e.dxftype()=='TEXT' for e in entities[:15])
            and entities[-1].dxftype()=='LINE', 'Independent entity inventory')
    style = doc.styles.get('ANNOTATIONS')
    require(style.dxf.font=='romans.shx' and style.dxf.bigfont=='bigfont.shx'
            and style.dxf.flags==4 and style.dxf.generation_flags==6 and style.dxf.last_height==0
            and style.dxf.width==.8 and style.dxf.oblique==12, 'Independent STYLE fields')
    for index, entity in enumerate(entities[:15]):
        horizontal, vertical = alignment(index)
        require(entity.dxf.handle==f'{256+index:X}' and entity.dxf.style=='ANNOTATIONS', 'Independent TEXT identity/style')
        require(entity.dxf.halign==horizontal and entity.dxf.valign==vertical, 'Independent alignment codes')
        require(entity.dxf.text==ENCODED and caret_decode(entity.dxf.text)==VALUE, 'Independent logical control decoding')
        require(entity.dxf.height==2.5 and entity.dxf.width==1.2 and entity.dxf.oblique==-10, 'Independent scalar text fields')
        require(entity.dxf.text_generation_flag==((2 if index&1 else 0)|(4 if index&2 else 0)), 'Independent mirror flags')
        first = entity.dxf.insert
        anchor = first if horizontal in (3,5) or not(horizontal or vertical) else entity.dxf.align_point
        require(tuple(entity.ocs().to_wcs(anchor))==(10.,20.,30.), 'Independent OCS/world placement')
        if horizontal in (3,5):
            require(tuple(entity.dxf.align_point-first)==(8.,0.,0.), 'Independent fit/aligned endpoints')
    require(entities[-1].dxf.handle=='10F' and tuple(entities[-1].dxf.start)==(1.,2.,3.)
            and tuple(entities[-1].dxf.end)==(4.,5.,6.), 'Following geometry')
    require(not any(any(values.values()) for values in audit_signature(doc)), 'Independent audit errors or repairs')


def main(directory):
    names = {f'r12-text-{normal}-{transport}-{stage}.dxf' for normal,transport,stage in
             itertools.product(range(3),('text','binary'),STAGES)}
    def inventory(actual): require(actual==names, 'Missing/extra TEXT drawings')
    inventory({p.name for p in directory.glob('r12-text-*.dxf')})
    rejected=reject(lambda:inventory(names-{next(iter(names))}))+reject(lambda:inventory(names|{'extra.dxf'}))
    for normal,transport,stage in itertools.product(range(3),('text','binary'),STAGES):
        path=directory/f'r12-text-{normal}-{transport}-{stage}.dxf'
        data=path.read_bytes(); binary=(transport=='binary') != (stage=='output')
        require(data.startswith(b'AutoCAD Binary DXF')==binary, 'TEXT transport')
        if binary: require(data[:31]==b'AutoCAD Binary DXF\r\n\x1a\0\0SECTION\0', 'Classic binary framing')
        wanted, actual=expected(normal),load_tags(path)
        check_packet(wanted,actual);independent(path)
        for index,(code,value) in enumerate(actual):
            for mode in ('remove','duplicate','change'):
                damaged=list(actual)
                if mode=='remove': damaged.pop(index)
                elif mode=='duplicate': damaged.insert(index,damaged[index])
                else: damaged[index]=(code,value+'_bad' if isinstance(value,str) else value+1)
                rejected+=reject(lambda:check_packet(wanted,damaged))
    print(f'PASS: {len(names)} drawings / 270 TEXT entities / 36 STYLE records; {rejected} corrupted packets/inventories rejected; '
          'exact identities, alignments, caret encoding and zero independent audit errors/repairs.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_r12_text.py ARTIFACTS'); main(Path(sys.argv[1]))
