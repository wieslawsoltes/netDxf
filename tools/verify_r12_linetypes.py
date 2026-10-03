#!/usr/bin/env python3
"""Check actual R12 linetypes, resource references and complete output packets."""
import io
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from verify_raw_line_geometry import key, require, reject, audit_signature

PATTERNS = {
    'CONTINUOUS': ('Solid line', ()),
    'DASH': ('Long dash', (1.5, -.5)),
    'CENTER': ('Center line', (1.25, -.25, .25, -.25)),
    'DOT': ('Café dot', (0., -.25)),
}


def point(code, xyz):
    return [(code, float(xyz[0])), (code+10, float(xyz[1])), (code+20, float(xyz[2]))]


def expected():
    tags = [(0,'SECTION'), (2,'HEADER'), (9,'$ACADVER'), (1,'AC1009'),
            (9,'$DWGCODEPAGE'), (3,'ANSI_1252'), (9,'$HANDSEED'), (5,'108'), (0,'ENDSEC'),
            (0,'SECTION'), (2,'TABLES'), (0,'TABLE'), (2,'LTYPE'), (70,4)]
    for name, (description, pattern) in PATTERNS.items():
        tags += [(0,'LTYPE'), (2,name), (70,0), (3,description), (72,65),
                 (73,len(pattern)), (40,float(sum(abs(v) for v in pattern)))]
        tags += [(49,value) for value in pattern]
    tags += [(0,'ENDTAB'), (0,'TABLE'), (2,'LAYER'), (70,3)]
    for name, color, pattern in (('0',7,'CONTINUOUS'), ('STROKES',2,'DASH'), ('FACES',3,'DOT')):
        tags += [(0,'LAYER'), (2,name), (70,0), (62,color), (6,pattern)]
    tags += [(0,'ENDTAB'), (0,'ENDSEC'), (0,'SECTION'), (2,'BLOCKS'), (0,'ENDSEC'), (0,'SECTION'), (2,'ENTITIES')]
    for i, start, end, pattern, color in ((0,(0,1,2),(3,4,5),'BYLAYER',256), (1,(6,7,8),(9,10,11),'CENTER',3)):
        tags += [(0,'LINE'), (5,f'{256+i:X}'), (8,'STROKES'), (6,pattern), (62,color)]
        tags += point(10,start) + point(11,end) + [(39,0.)] + point(210,(0,0,1))
    tags += [(0,'POLYLINE'), (5,'102'), (8,'STROKES'), (6,'BYLAYER'), (62,256), (66,1)]
    tags += point(10,(0,0,0)) + [(70,64), (71,3), (72,1)] + point(210,(0,0,1))
    for i, value in enumerate(((0,0,0),(2,0,0),(0,2,0))):
        tags += [(0,'VERTEX'), (5,f'{259+i:X}'), (8,'STROKES')] + point(10,value) + [(70,192)]
    tags += [(0,'VERTEX'), (5,'106'), (8,'FACES'), (62,0)] + point(10,(0,0,0))
    tags += [(70,128), (71,1), (72,-2), (73,3), (74,0), (0,'SEQEND'), (5,'107'), (8,'STROKES'), (0,'ENDSEC'), (0,'EOF')]
    return tags


def load(path):
    data=path.read_bytes()
    source=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('cp1252'), newline=None))
    return [(t.code,cast_tag_value(t.code,t.value)) for t in source]


def compare(wanted, actual):
    require(list(map(key,wanted))==list(map(key,actual)), 'R12 linetype/resource packet mismatch')


def independent(path):
    doc=ezdxf.readfile(path)
    require(doc.dxfversion=='AC1009','Independent version')
    for name,(description,pattern) in PATTERNS.items():
        resource=doc.linetypes.get(name)
        require(resource.dxf.description==description,'Independent description/code-page text')
        stored=resource.pattern_tags.tags
        values=[t.value for t in stored if t.code==49]
        require(values==list(pattern),'Independent signed pattern elements')
        require(stored.get_first_value(40)==sum(abs(v) for v in pattern),'Independent total pattern length')
        require(not resource.pattern_tags.is_complex_type(),'Unexpected complex pattern')
    require(doc.layers.get('STROKES').dxf.linetype=='DASH' and doc.layers.get('FACES').dxf.linetype=='DOT','Independent layer pattern references')
    a,b,mesh=list(doc.modelspace())
    require(a.dxftype()==b.dxftype()=='LINE' and mesh.is_poly_face_mesh,'Independent carrier inventory')
    require((a.dxf.handle,b.dxf.handle,mesh.dxf.handle)==('100','101','102'),'Independent entity identities')
    require(a.dxf.linetype=='BYLAYER' and b.dxf.linetype=='CENTER' and mesh.dxf.linetype=='BYLAYER','Independent entity pattern references')
    require(tuple(a.dxf.start)==(0.,1.,2.) and tuple(a.dxf.end)==(3.,4.,5.)
            and tuple(b.dxf.start)==(6.,7.,8.) and tuple(b.dxf.end)==(9.,10.,11.),'Independent geometry')
    vertices,faces=mesh.indexed_faces();faces=list(faces)
    require(len(vertices)==3 and len(faces)==1 and faces[0].face_record.dxf.layer=='FACES','Independent face-only layer')
    require(faces[0].face_record.dxf.color==0 and mesh.seqend.dxf.handle=='107','Independent face styling/terminator')
    require(not any(any(v.values()) for v in audit_signature(doc)),'Independent linetype audit errors/repairs')


def main(directory):
    names={f'r12-linetypes-{transport}-{stage}.dxf' for transport,stage in itertools.product(('text','binary'),('source','output','resave'))}
    def inventory(actual): require(actual==names,'Missing/extra linetype drawings')
    inventory({p.name for p in directory.glob('r12-linetypes-*.dxf')})
    rejected=reject(lambda:inventory(names-{next(iter(names))}))+reject(lambda:inventory(names|{'extra.dxf'}))
    wanted=expected()
    for transport,stage in itertools.product(('text','binary'),('source','output','resave')):
        path=directory/f'r12-linetypes-{transport}-{stage}.dxf';data=path.read_bytes()
        binary=(transport=='binary')!=(stage=='output')
        require(data.startswith(b'AutoCAD Binary DXF')==binary,'Linetype transport mismatch')
        if binary: require(data[:31]==b'AutoCAD Binary DXF\r\n\x1a\0\0SECTION\0','Classic binary framing')
        actual=load(path);compare(wanted,actual);independent(path)
        for i,(code,value) in enumerate(actual):
            for mode in ('remove','duplicate','change'):
                bad=list(actual)
                if mode=='remove':bad.pop(i)
                elif mode=='duplicate':bad.insert(i,bad[i])
                else:bad[i]=(code,value+'_bad' if isinstance(value,str) else value+1)
                rejected+=reject(lambda:compare(wanted,bad))
    print(f'PASS: {len(names)} drawings / 24 LTYPE definitions; {rejected} altered-packet/inventory controls rejected; '
          'exact pattern elements, names, references and zero independent audit errors/repairs.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_r12_linetypes.py ARTIFACTS');main(Path(sys.argv[1]))
