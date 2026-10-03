#!/usr/bin/env python3
"""Check actual R12 polygon/polyface output against full independent packets and geometry."""
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject, audit_signature

NORMALS = ((0., 0., 1.), (1., 0., 0.), (0., 0., -1.))
VERTICES = ((-0.,0.,1.), (4.,0.,2.), (4.,3.,3.), (0.,3.,4.), (2.,1.,5.))
FACES = ((1,-2,3,0), (-1,3,-4,5), (2,3,4,0))


def point(code, xyz):
    return [(code,float(xyz[0])), (code+10,float(xyz[1])), (code+20,float(xyz[2]))]


def grid(u,v):
    return (-0. if u == 0 and v == 0 else u*10., v*20., u+v*.5)


def expected(normal):
    tags = [(0,'SECTION'), (2,'HEADER'), (9,'$ACADVER'), (1,'AC1009'),
            (9,'$DWGCODEPAGE'), (3,'ANSI_1252'), (9,'$HANDSEED'), (5,'113'), (0,'ENDSEC'),
            (0,'SECTION'), (2,'TABLES'), (0,'TABLE'), (2,'LTYPE'), (70,1),
            (0,'LTYPE'), (2,'CONTINUOUS'), (70,0), (3,'Solid line'), (72,65), (73,0), (40,0.),
            (0,'ENDTAB'), (0,'TABLE'), (2,'LAYER'), (70,3),
            (0,'LAYER'), (2,'0'), (70,0), (62,7), (6,'CONTINUOUS'),
            (0,'LAYER'), (2,'MESHES'), (70,0), (62,2), (6,'CONTINUOUS'),
            (0,'LAYER'), (2,'FACES'), (70,0), (62,-1), (6,'CONTINUOUS'),
            (0,'ENDTAB'), (0,'ENDSEC'), (0,'SECTION'), (2,'BLOCKS'), (0,'ENDSEC'),
            (0,'SECTION'), (2,'ENTITIES'),
            (0,'POLYLINE'), (5,'100'), (8,'MESHES'), (6,'BYLAYER'), (62,3), (66,1)]
    tags += point(10,(0,0,0)) + [(70,49), (71,2), (72,3), (73,5), (74,7), (75,0)] + point(210,NORMALS[normal])
    for u,v in itertools.product(range(2),range(3)):
        tags += [(0,'VERTEX'), (5,f'{257+u*3+v:X}'), (8,'MESHES')] + point(10,grid(u,v)) + [(70,64)]
    tags += [(0,'SEQEND'), (5,'107'), (8,'MESHES'),
             (0,'POLYLINE'), (5,'108'), (8,'MESHES'), (6,'BYLAYER'), (62,4), (66,1)]
    tags += point(10,(0,0,0)) + [(70,64), (71,5), (72,3)] + point(210,NORMALS[normal])
    for i,p in enumerate(VERTICES):
        tags += [(0,'VERTEX'), (5,f'{265+i:X}'), (8,'MESHES')] + point(10,p) + [(70,192)]
    for i,indices in enumerate(FACES):
        tags += [(0,'VERTEX'), (5,f'{270+i:X}'), (8,'FACES' if i==1 else 'MESHES'), (62,(4,6,0)[i])]
        tags += point(10,(0,0,0)) + [(70,128)] + [(71+j,value) for j,value in enumerate(indices)]
    tags += [(0,'SEQEND'), (5,'111'), (8,'MESHES'),
             (0,'LINE'), (5,'112'), (8,'MESHES'), (6,'BYLAYER'), (62,256)]
    tags += point(10,(10,20,30)) + point(11,(40,50,60)) + [(39,0.)] + point(210,(0,0,1))
    return tags + [(0,'ENDSEC'), (0,'EOF')]


def same(wanted, observed):
    require(list(map(key,wanted)) == list(map(key,observed)), 'R12 mesh physical packet mismatch')


def bits_point(value):
    return tuple(struct.pack('>d',v) for v in value)


def independent(path,normal):
    doc=ezdxf.readfile(path)
    require(doc.dxfversion=='AC1009','Independent format')
    g,p,line=list(doc.modelspace())
    require(g.is_polygon_mesh and p.is_poly_face_mesh,'Independent mesh types')
    require((g.dxf.handle,p.dxf.handle,line.dxf.handle)==('100','108','112'),'Independent parent identities')
    require(g.dxf.flags==49 and g.dxf.m_count==2 and g.dxf.n_count==3,'Independent grid shape/closure')
    require(g.dxf.m_smooth_density==5 and g.dxf.n_smooth_density==7 and g.dxf.smooth_type==0,'Independent density hints')
    require(tuple(g.dxf.extrusion)==tuple(p.dxf.extrusion)==NORMALS[normal],'Independent mesh normals')
    for u,v in itertools.product(range(2),range(3)):
        vertex=g.get_mesh_vertex((u,v))
        require(bits_point(vertex.dxf.location)==bits_point(grid(u,v)) and vertex.dxf.flags==64,'Independent grid point/index order')
    points,faces=p.indexed_faces(); faces=list(faces)
    require(len(points)==5 and len(faces)==3,'Independent polyface inventory')
    for i,vertex in enumerate(points):
        require(bits_point(vertex.dxf.location)==bits_point(VERTICES[i]) and vertex.dxf.flags==192,'Independent polyface coordinates')
    for i,face in enumerate(faces):
        indices=tuple(v for v in FACES[i] if v!=0)
        require(list(map(bits_point,face.points()))==[bits_point(VERTICES[abs(v)-1]) for v in indices],'Independent dereferenced face geometry')
        require([face.is_edge_visible(j) for j in range(len(indices))]==[v>0 for v in indices],'Independent edge visibility')
        require(face.face_record.dxf.layer==('FACES' if i==1 else 'MESHES') and face.face_record.dxf.color==(4,6,0)[i],'Independent face styling')
    require(g.seqend.dxf.handle=='107' and p.seqend.dxf.handle=='111','Independent sequence terminators')
    require(tuple(line.dxf.start)==(10.,20.,30.) and tuple(line.dxf.end)==(40.,50.,60.),'Following LINE geometry')
    require(not any(any(v.values()) for v in audit_signature(doc)),'Independent mesh audit errors/repairs')


def main(directory):
    names={f'r12-meshes-{n}-{t}-{s}.dxf' for n,t,s in itertools.product(range(3),('text','binary'),('source','output','resave'))}
    def inventory(actual): require(actual==names,'Missing/extra mesh drawings')
    inventory({p.name for p in directory.glob('r12-meshes-*.dxf')})
    rejected=reject(lambda:inventory(names-{next(iter(names))}))+reject(lambda:inventory(names|{'extra.dxf'}))
    for n,t,s in itertools.product(range(3),('text','binary'),('source','output','resave')):
        path=directory/f'r12-meshes-{n}-{t}-{s}.dxf';data=path.read_bytes();binary=(t=='binary')!=(s=='output')
        require(data.startswith(b'AutoCAD Binary DXF')==binary,'Wrong mesh transport')
        if binary: require(data[:31]==b'AutoCAD Binary DXF\r\n\x1a\0\0SECTION\0','Classic binary framing')
        wanted,actual=expected(n),load_tags(path);same(wanted,actual);independent(path,n)
        for i,(code,value) in enumerate(actual):
            for mode in ('remove','duplicate','change'):
                bad=list(actual)
                if mode=='remove':bad.pop(i)
                elif mode=='duplicate':bad.insert(i,bad[i])
                else:bad[i]=(code,value+'_bad' if isinstance(value,str) else value+1)
                rejected+=reject(lambda:same(wanted,bad))
    print(f'PASS: {len(names)} drawings / 36 meshes / 198 coordinate vertices / 54 face records / 36 terminators; '
          f'{rejected} packet/inventory corruptions rejected; exact topology/styles and zero independent audit errors/repairs.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_r12_meshes.py ARTIFACTS');main(Path(sys.argv[1]))
