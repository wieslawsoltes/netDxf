#!/usr/bin/env python3
"""Check actual transformed HATCH wire conics with independent OCS and curve math."""
from pathlib import Path
import argparse, copy, hashlib, json, math
import numpy as np
import ezdxf
from verify_datatable import records, first, check, audit
from verify_hatch_affine import transform
ROOT=Path(__file__).resolve().parents[1]
TAU=2*math.pi

def matrix(operation):
    return transform(operation) if operation<5 else np.diag([.2,7,1] if operation==5 else [100,.1,2])

def basis(normal):
    n=np.array(normal,dtype=float);n/=np.linalg.norm(n)
    x=np.cross([0,1,0] if abs(n[0])<1/64 and abs(n[1])<1/64 else [0,0,1],n);x/=np.linalg.norm(x)
    return np.column_stack([x,np.cross(n,x),n])

def paths(tags):
    check(first(tags,91)==1,'Expected one geometric path')
    at=next(i for i,t in enumerate(tags) if t[0]==92); flags=tags[at][1]
    stop=next(i for i in range(at+1,len(tags)) if tags[i][0]==97);data=tags[at+1:stop]
    if flags&2:
        closed=bool(first(data,73)); count=first(data,93);points=[]
        for code,value in data:
            if code==10:points.append([*value[:2],0.])
            elif code==42:points[-1][2]=value
        check(len(points)==count,'Polyline vertex count')
        edges=[]
        for i in range(count if closed else max(0,count-1)):
            a=np.array(points[i][:2]);b=np.array(points[(i+1)%count][:2]);bulge=points[i][2]
            if bulge==0:edges.append({'kind':1,'start':a,'end':b});continue
            chord=b-a;center=(a+b)/2+np.array([-chord[1],chord[0]])*((1/bulge-bulge)/4)
            radius=np.linalg.norm(a-center);sign=1 if bulge>0 else -1
            edges.append({'kind':2,'center':center,'major':np.array([radius,0]),'ratio':1.,'start':sign*math.degrees(math.atan2(*(a-center)[::-1])),'end':sign*math.degrees(math.atan2(*(b-center)[::-1])),'ccw':bulge>0})
        return flags,edges
    beginnings=[i for i,t in enumerate(data) if t[0]==72];check(len(beginnings)==first(data,93),'Edge count')
    edges=[]
    for index,begin in enumerate(beginnings):
        packet=data[begin:beginnings[index+1] if index+1<len(beginnings) else len(data)];kind=packet[0][1]
        if kind==1:edges.append({'kind':1,'start':np.array(first(packet,10)),'end':np.array(first(packet,11))});continue
        check(kind in (2,3),'Expected conic edge')
        edges.append({'kind':kind,'center':np.array(first(packet,10)), 'major':np.array([first(packet,40),0] if kind==2 else first(packet,11)),
          'ratio':1. if kind==2 else first(packet,40),'start':first(packet,50),'end':first(packet,51),'ccw':bool(first(packet,73))})
    return flags,edges

def points(edge):
    fractions=np.linspace(0,1,65)
    if edge['kind']==1:return edge['start']+(edge['end']-edge['start'])*fractions[:,None]
    sign=1 if edge['ccw'] else -1;ratio=edge['ratio'];start=edge['start'];end=edge['end']
    def parameter(angle):
        value=math.radians(sign*angle);return math.atan2(math.sin(value)/ratio,math.cos(value))
    first=parameter(start);last=parameter(end)
    sweep=0 if start==end else sign*TAU if abs(abs(end-start)-360)<1e-10 else sign*((sign*(last-first))%TAU)
    values=first+sweep*fractions;axis=edge['major'];perp=np.array([-axis[1],axis[0]])
    return edge['center']+np.cos(values)[:,None]*axis+np.sin(values)[:,None]*(ratio*perp)

def world(ocs,xy,elevation):
    return np.column_stack([xy,np.full(len(xy),elevation)])@ocs.T

def check_output(data,source,operation,plane):
    actual={first(t,8):t for t in records(data).values() if t[0]==(0,'HATCH')}
    check(actual.keys()==source.keys(),'Actual HATCH inventory')
    old=basis((0,0,1) if plane==0 else (1,2,3));a=matrix(operation);translation=np.array([7,-11,13]);maximum=0;count=0
    for name,tags in actual.items():
        source_tags=source[name];flags,edges=paths(tags);old_flags,old_edges=paths(source_tags)
        check((flags&~2)==(old_flags&~2),'Path classification bits changed')
        check(len(edges)==len(old_edges),'Path acquired/dropped a segment')
        check(first(tags,47)==.0625,'Pixel size changed')
        normal=first(tags,210);ocs=basis(normal);elevation=first(tags,10)[2]
        for wanted,edge in zip(old_edges,edges):
            expected=world(old,points(wanted),4)@a.T+translation;observed=world(ocs,points(edge),elevation)
            error=np.linalg.norm(expected-observed,axis=1);scales=np.maximum(1,np.maximum(np.linalg.norm(expected,axis=1),np.linalg.norm(observed,axis=1)))
            check(np.isfinite(observed).all() and np.all(error<=2e-10*scales),'World curve differs '+name)
            maximum=max(maximum,float(error.max()));count+=1
            if wanted['kind'] in (2,3):
                zero=wanted['start']==wanted['end'];full=abs(wanted['end']-wanted['start'])==360
                check((edge['start']==edge['end'])==zero,'Zero interval changed')
                check((abs(abs(edge['end']-edge['start'])-360)<1e-10)==full,'Full interval changed')
                if edge['kind']==3:check(0<edge['ratio']<=1,'Ellipse is not canonically representable')
        seed_at=next(i for i,t in enumerate(tags) if t[0]==98);check(tags[seed_at][1]==1,'Seed count')
        expected=world(old,np.array([[1,2]]),4)@a.T+translation;observed=world(ocs,np.array([tags[seed_at+1][1]]),elevation)
        check(np.linalg.norm(expected-observed)<=2e-10*max(1,np.linalg.norm(expected)),'Seed world point')
    return count,maximum

def corrupt(data,defect):
    lines=data.decode().splitlines();pairs=[(int(lines[i]),lines[i+1]) for i in range(0,len(lines),2)]
    at=next(i for i,p in enumerate(pairs) if p==(8,'ELLIPSE_0_FULL' if defect=='full' else 'ELLIPSE_0_SHORT'))
    code={'ratio':40,'direction':73,'angle':50,'full':51,'normal':210,'axis':11}[defect]
    i=next(i for i in range(at+1,len(pairs)) if pairs[i][0]==code)
    if defect=='full':value=next(v for c,v in pairs[at:i] if c==50)
    elif defect=='direction':value=str(1-int(pairs[i][1]))
    else:value=str(float(pairs[i][1])+.1)
    pairs[i]=(code,value);return ''.join(f'{c}\n{v}\n' for c,v in pairs).encode()

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args()
    fixture=ROOT/'tests/fixtures/hatch-conic-affine';manifest=json.loads((fixture/'manifest.json').read_text());check(manifest['producer']=='ezdxf' and manifest['version']=='1.4.4','Producer pin')
    outputs=edges=negative=0;maximum=0;sample=None
    for item in manifest['files']:
        data=(fixture/item['file']).read_bytes();check(hashlib.sha256(data).hexdigest()==item['sha256'],'Pinned producer changed')
        source={first(t,8):t for t in records(data).values() if t[0]==(0,'HATCH')};check(len(source)==25,'Producer HATCH inventory')
        # Assert raw angles/direction against the producer's separately captured writer packets.
        for case in manifest['cases']:
            if case['kind']=='POLYLINE':continue
            tags=source[case['name']];begin=next(i for i,t in enumerate(tags) if t==(72,2 if case['kind']=='ARC' else 3));packet=tags[begin:]
            for code,value in case['raw_tags']:
                if code in (50,51,73):check(first(packet,code)==value,'Producer raw endpoint convention changed')
        for operation in range(7):
            for plane in range(2):
                file=args.artifacts/f'hatch-conic-AutoCad{item["year"]}-{item["binary"]}-{operation}-{plane}.dxf';output=file.read_bytes()
                check(output.startswith(b'AutoCAD Binary DXF')==item['binary'],'Output transport')
                count,error=check_output(output,source,operation,plane);edges+=count;maximum=max(maximum,error)
                doc=ezdxf.readfile(file);audit(doc);line=list(doc.modelspace().query('LINE'));check(len(line)==1 and tuple(line[0].dxf.start)==(1,2,3) and tuple(line[0].dxf.end)==(4,5,6),'Following LINE changed');outputs+=1
                if item['year']==2018 and not item['binary'] and operation==3 and plane==1:sample=(output,source,operation,plane)
    check(outputs==168 and edges==6216,'Mandatory actual-output inventory')
    for defect in ('ratio','direction','angle','full','normal','axis'):
        try:check_output(corrupt(sample[0],defect),*sample[1:])
        except (ValueError,AssertionError):negative+=1
        else:raise ValueError('Accepted actual-output corruption '+defect)
    conditioning=json.loads((args.artifacts/'hatch-conic-conditioning.json').read_text());check(len(conditioning)==56,'Conditioning probe inventory')
    conditioning_outputs=0
    for item in conditioning:
        if not item['accepted']:
            check(item['file'] is None,'Rejected conic unexpectedly produced a file');continue
        file=args.artifacts/item['file'];doc=ezdxf.readfile(file);audit(doc)
        tags=[t for t in records(file.read_bytes()).values() if t[0]==(0,'HATCH')];check(len(tags)==1,'Conditioning HATCH inventory')
        flags,curves=paths(tags[0]);check(len(curves)==1 and curves[0]['kind']==3,'Tiny nonzero bulge was flattened')
        endpoints=points(curves[0])[[0,-1]];ocs=basis(first(tags[0],210));elevation=first(tags[0],10)[2]
        expected=np.array([[0,0,0],[10,0,0]])@matrix(item['operation']).T+np.array([7,-11,13]);actual=world(ocs,endpoints,elevation)
        scale=max(1,float(np.abs(expected).max()));check(np.all(np.abs(actual-expected)<=2e-10*scale),'Conditioned conic endpoints changed')
        conditioning_outputs+=1
    check(0<conditioning_outputs<56,'Conditioning did not cover both outcomes')
    print(json.dumps({'outputs':outputs,'conditioning_outputs':conditioning_outputs,'conditioning_rejections':56-conditioning_outputs,'conic_and_line_segments':edges,'world_curve_probes':edges*65,'maximum_world_error':maximum,'actual_output_negative_controls':negative,'raw_producer_convention':True,'audit_errors':0,'audit_fixes':0,'native_cad_execution':False},sort_keys=True))
if __name__=='__main__':main()
