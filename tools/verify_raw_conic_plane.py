#!/usr/bin/env python3
"""Independently verify complete raw CIRCLE/ARC plane edits and retained packets."""
from pathlib import Path
import itertools
import math
import sys
import ezdxf
from verify_raw_conic_geometry import PROFILES, load_tags, key, require, rejected, selected, one, diagnostics

NORMAL_CODES = (210,220,230)
FIELDS = (10,20,30,40,50,51)


def replacement(source, arc, center=(-8.,16.,32.), radius=3.75, first=350., last=35.):
    values = dict(zip(FIELDS,center+(radius,first,last)))
    target = []
    has_z = any(c==30 for c,_ in source)
    for code,value in source:
        if code in (39,210,220,230):
            continue
        target.append((code,values[code] if code in FIELDS and (arc or code not in (50,51)) else value))
        if code==20 and not has_z:
            target.append((30,center[2]))
        if code==40:
            target.extend(((39,-3.),(210,0.),(220,2.),(230,0.)))
    return target


def validate_packet(record,arc,center,radius,first,last,normal,thickness):
    fields=dict(zip((10,20,30),center));fields.update(zip(NORMAL_CODES,normal))
    fields.update({40:radius,39:thickness})
    if arc:fields.update({50:first,51:last})
    for code,wanted in fields.items():
        require(key((code,one(record,code)))==key((code,wanted)),f'Conic field {code} differs')
    at=next(i for i,t in enumerate(record) if t[0]==40)
    require([c for c,_ in record[at+1:at+5]]==[39,210,220,230], 'Thickness/extrusion vector not adjacent after radius')
    if arc and (100,'AcDbArc') in record:
        require(at+4<record.index((100,'AcDbArc')), 'Extrusion emitted outside circle subclass')


def check_pair(before,after,arc,missing):
    start,end=selected(before,arc);a,b=selected(after,arc)
    require(a==start,'Selected record moved')
    require(list(map(key,before[:start]))==list(map(key,after[:a])) and
            list(map(key,before[end:]))==list(map(key,after[b:])),'Other records changed')
    source=before[start:end];target=after[a:b]
    initial={10:1.25,20:-2.,30:0. if missing else 3.,40:7.5}
    if arc:initial.update({50:15.,51:270.})
    if missing:require(not any(c==30 for c,_ in source),'Absent source Z materialized')
    for code,value in initial.items():
        require(one(source,code,0. if code==30 and missing else None)==value,'Regenerated source geometry differs')
    for code,value in ((39,-2.5),(210,0.),(220,0.),(230,-1.)):
        fields=[t for t in source if t[0]==code]
        require(fields==([] if missing else [(code,value)]),'Source thickness/plane presence differs')
    require(list(map(key,target))==list(map(key,replacement(source,arc))), 'Unexpected complete edited packet')
    validate_packet(target,arc,(-8.,16.,32.),3.75,350.,35.,(0.,2.,0.),-3.)
    return a,b


def verify_entity(doc,arc,missing=False,edited=True,packet=False):
    entity=doc.entitydb['A']
    require(entity.dxftype()==('ARC' if arc else 'CIRCLE'),'Independent selected identity/type')
    if edited:
        center=(1.25,-2.,3.) if packet else (-8.,16.,32.)
        radius=7.5 if packet else 3.75
        normal=(0.,2.,0.);thickness=-3.
        first,last=(15.,270.) if packet else (350.,395.)
    else:
        center=(1.25,-2.,0. if missing else 3.);radius=7.5
        normal=(0.,0.,1. if missing else -1.);thickness=0. if missing else -2.5
        first,last=15.,270.
    require(tuple(entity.dxf.center)==center and entity.dxf.radius==radius,'Independent center/radius')
    require(tuple(entity.dxf.extrusion)==normal and entity.dxf.thickness==thickness,'Independent extrusion/thickness')
    if arc:
        require(entity.dxf.start_angle==first and entity.dxf.end_angle==(35. if edited and not packet else last),'Independent stored angles')
    else:first,last=0.,360.
    angles=[first+(last-first)*i/16 for i in range(17)]
    points=list(entity.vertices(angles));require(len(points)==17,'Independent sample count')
    x,y,z=center
    for angle,point in zip(angles,points):
        theta=math.radians(angle);dx=radius*math.cos(theta);dy=radius*math.sin(theta)
        # Explicit fixed OCS bases, not an OCS transform from either library.
        wanted=(-x-dx,z,y+dy) if edited else (x+dx,y+dy,z) if missing else (-x-dx,y+dy,-z)
        require(all(abs(point[i]-wanted[i])<=5e-13 for i in range(3)),'Independent WCS curve sample differs')


def main(directory):
    specs=list(itertools.product(PROFILES,(False,True),(False,True),(False,True),(False,True),(False,True)))
    def stem(spec):return 'conic-plane-'+'-'.join(map(str,spec))
    pairs={stem(spec)+'-'+side+'.dxf' for spec in specs for side in ('before','after')}
    packets=list(itertools.product((False,True),range(4),(False,True)))
    packet_names={f'conic-plane-packet-{a}-{v}-{b}.dxf' for a,v,b in packets}
    def inventory(actual):require(actual==pairs|packet_names,'Missing or extra conic-plane fixture')
    inventory({p.name for p in directory.glob('conic-plane-*.dxf')})
    rejected(lambda:inventory((pairs|packet_names)-{min(pairs)}))
    rejected(lambda:inventory(pairs|packet_names|{'conic-plane-extra.dxf'}))
    corruptions=errors=repairs=0
    for spec in specs:
        version,arc,_,_,missing,binary=spec
        paths=[directory/(stem(spec)+'-'+side+'.dxf') for side in ('before','after')]
        before,after=map(load_tags,paths)
        for path,tags in zip(paths,(before,after)):
            data=path.read_bytes();require(data.startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
            at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,PROFILES[version]),'Physical profile changed')
            if binary:require((data[23]!=0)==(version=='AutoCad12'),'Historical binary framing changed')
        a,b=check_pair(before,after,arc,missing)
        for at in range(a+1,b):
            code,value=after[at]
            for operation in ('change','remove','duplicate'):
                damaged=list(after)
                if operation=='change':damaged[at]=(code,value+.5 if isinstance(value,(int,float)) else str(value)+'_changed')
                elif operation=='remove':del damaged[at]
                else:damaged.insert(at,damaged[at])
                corruptions+=rejected(lambda:check_pair(before,damaged,arc,missing))
        docs=[ezdxf.readfile(path) for path in paths]
        old,new=map(diagnostics,docs);require(old==new,'Added graph diagnostics')
        errors+=sum(new[0].values());repairs+=sum(new[1].values())
        verify_entity(docs[0],arc,missing,False);verify_entity(docs[1],arc)
    for arc,variant,binary in packets:
        path=directory/f'conic-plane-packet-{arc}-{variant}-{binary}.dxf';tags=load_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Packet transport')
        at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,'AC1032'),'Packet profile')
        a,b=selected(tags,arc);record=tags[a:b]
        def valid(r):validate_packet(r,arc,(1.25,-2.,3.),7.5,15.,270.,(0.,2.,0.),-3.)
        valid(record)
        require([v for c,v in record if c==60]==([0] if variant==1 else []),'Separator metadata changed')
        for code in (39,210,220,230):
            at=next(i for i,t in enumerate(record) if t[0]==code)
            for operation in ('change','remove','duplicate'):
                damaged=list(record)
                if operation=='change':damaged[at]=(code,float(damaged[at][1])+1)
                elif operation=='remove':del damaged[at]
                else:damaged.insert(at,damaged[at])
                corruptions+=rejected(lambda:valid(damaged))
        doc=ezdxf.readfile(path);diag=diagnostics(doc)
        require(not diag[0] and not diag[1],'Packet graph diagnostics')
        verify_entity(doc,arc,edited=True,packet=True)
    drawings=len(pairs)+len(packet_names)
    print(f'PASS: {len(specs)} complete source/edit pairs and {len(packet_names)} packet drawings / {drawings} drawings; '
          f'{drawings*17} independent WCS samples; {corruptions} actual-tag corruptions and two inventory controls rejected; '
          f'no added graph diagnostics, source errors={errors}, repairs={repairs}.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_raw_conic_plane.py ARTIFACTS')
    main(Path(sys.argv[1]))
