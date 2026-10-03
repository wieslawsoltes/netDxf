#!/usr/bin/env python3
"""Check classic R12 2D/3D chains against independently specified physical packets."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject, audit_signature

NORMALS = ((0., 0., 1.), (1., 0., 0.), (0., 0., -1.))


def point(code, xyz):
    return [(code, float(xyz[0])), (code+10, float(xyz[1])), (code+20, float(xyz[2]))]


def expected(normal):
    tags = [(0,'SECTION'),(2,'HEADER'),(9,'$ACADVER'),(1,'AC1009'),
            (9,'$DWGCODEPAGE'),(3,'ANSI_1252'),(9,'$HANDSEED'),(5,'10C'),(0,'ENDSEC'),
            (0,'SECTION'),(2,'TABLES'),(0,'TABLE'),(2,'LTYPE'),(70,1),
            (0,'LTYPE'),(2,'CONTINUOUS'),(70,0),(3,'Solid line'),(72,65),(73,0),(40,0.),
            (0,'ENDTAB'),(0,'TABLE'),(2,'LAYER'),(70,2),
            (0,'LAYER'),(2,'0'),(70,0),(62,7),(6,'CONTINUOUS'),
            (0,'LAYER'),(2,'PATHS'),(70,0),(62,2),(6,'CONTINUOUS'),
            (0,'ENDTAB'),(0,'ENDSEC'),(0,'SECTION'),(2,'BLOCKS'),(0,'ENDSEC'),
            (0,'SECTION'),(2,'ENTITIES')]
    tags += [(0,'POLYLINE'),(5,'100'),(8,'PATHS'),(6,'BYLAYER'),(62,3),(66,1)]
    tags += point(10,(0,0,5)) + [(70,129),(39,-2.)] + point(210,NORMALS[normal])
    xy = ((-0.,0.), (4.,0.), (4.,3.), (0.,3.))
    bulges = (1.,-.5,0.,.25)
    widths = (((40,.5),(41,1.)),((41,0.),),((40,.25),),())
    for i in range(4):
        tags += [(0,'VERTEX'),(5,f'{257+i:X}'),(8,'PATHS')]
        tags += point(10,(*xy[i],0)) + [(70,0)] + list(widths[i]) + [(42,bulges[i])]
    tags += [(0,'SEQEND'),(5,'105'),(8,'PATHS'),
             (0,'POLYLINE'),(5,'106'),(8,'PATHS'),(6,'BYLAYER'),(62,4),(66,1)]
    tags += point(10,(0,0,0)) + [(70,136)] + point(210,NORMALS[normal])
    for i,xyz in enumerate(((1,2,3),(4,5,6),(-7,8,9))):
        tags += [(0,'VERTEX'),(5,f'{263+i:X}'),(8,'PATHS')] + point(10,xyz) + [(70,32)]
    tags += [(0,'SEQEND'),(5,'10A'),(8,'PATHS'),
             (0,'LINE'),(5,'10B'),(8,'PATHS'),(6,'BYLAYER'),(62,256)]
    tags += point(10,(10,20,30)) + point(11,(40,50,60)) + [(39,0.)] + point(210,(0,0,1))
    return tags + [(0,'ENDSEC'),(0,'EOF')]


def same(wanted, actual):
    require(list(map(key,wanted)) == list(map(key,actual)), 'R12 POLYLINE packet mismatch')


def independent(path, normal):
    doc=ezdxf.readfile(path)
    require(doc.dxfversion=='AC1009','Independent format family')
    p,q,line=list(doc.modelspace())
    require(p.is_2d_polyline and q.is_3d_polyline and line.dxftype()=='LINE','Independent parent types')
    require((p.dxf.handle,q.dxf.handle,line.dxf.handle)==('100','106','10B'),'Independent parent identities')
    require(p.is_closed and not q.is_closed,'Independent closure')
    require(p.dxf.flags==129 and q.dxf.flags==136,'Independent flags')
    require(tuple(p.dxf.elevation)==(0.,0.,5.) and p.dxf.thickness==-2.,'Independent planar elevation/thickness')
    require(tuple(p.dxf.extrusion)==tuple(q.dxf.extrusion)==NORMALS[normal],'Independent extrusion')
    xy=((-0.,0.,0.),(4.,0.,0.),(4.,3.,0.),(0.,3.,0.))
    widths=((.5,1.),(None,0.),(.25,None),(None,None))
    bulges=(1.,-.5,0.,.25)
    require(len(p.vertices)==4 and len(q.vertices)==3,'Independent vertex inventory')
    for i,v in enumerate(p.vertices):
        require(v.dxf.handle==f'{257+i:X}' and tuple(v.dxf.location)==xy[i],'Independent planar vertex')
        require(v.dxf.bulge==bulges[i] and v.dxf.flags==0,'Independent bulge/flags')
        for field,wanted in zip(('start_width','end_width'),widths[i]):
            require(v.dxf.hasattr(field)==(wanted is not None),'Optional width presence')
            if wanted is not None: require(v.dxf.get(field)==wanted,'Independent width')
    require(p.seqend.dxf.handle=='105' and q.seqend.dxf.handle=='10A','Independent terminators')
    require([tuple(v.dxf.location) for v in q.vertices]==[(1.,2.,3.),(4.,5.,6.),(-7.,8.,9.)],'Independent WCS vertices')
    require(all(v.dxf.flags==32 for v in q.vertices),'Independent 3D vertex flags')
    require(tuple(line.dxf.start)==(10.,20.,30.) and tuple(line.dxf.end)==(40.,50.,60.),'Following LINE')
    require(not any(any(c.values()) for c in audit_signature(doc)), 'Independent chain audit errors/repairs')


def main(directory):
    names={f'r12-polylines-{n}-{t}-{s}.dxf' for n,t,s in
           itertools.product(range(3),('text','binary'),('source','output','resave'))}
    def inventory(value): require(value==names,'Missing/extra polyline drawings')
    inventory({p.name for p in directory.glob('r12-polylines-*.dxf')})
    controls=reject(lambda:inventory(names-{next(iter(names))}))+reject(lambda:inventory(names|{'extra.dxf'}))
    for n,t,s in itertools.product(range(3),('text','binary'),('source','output','resave')):
        path=directory/f'r12-polylines-{n}-{t}-{s}.dxf'
        data=path.read_bytes();binary=(t=='binary')!=(s=='output')
        require(data.startswith(b'AutoCAD Binary DXF')==binary,'Polyline transport')
        if binary: require(data[:31]==b'AutoCAD Binary DXF\r\n\x1a\0\0SECTION\0','Classic binary framing')
        wanted,actual=expected(n),load_tags(path);same(wanted,actual);independent(path,n)
        for i,(code,value) in enumerate(actual):
            for mode in ('change','remove','duplicate'):
                bad=list(actual)
                if mode=='remove':bad.pop(i)
                elif mode=='duplicate':bad.insert(i,bad[i])
                else:bad[i]=(code,value+'_bad' if isinstance(value,str) else value+1)
                controls+=reject(lambda:same(wanted,bad))
    print(f'PASS: {len(names)} drawings / 36 polylines / 126 vertices / 36 terminators; '
          f'{controls} corrupted packets/inventory changes rejected; zero independent audit errors/repairs.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_r12_polylines.py ARTIFACTS');main(Path(sys.argv[1]))
