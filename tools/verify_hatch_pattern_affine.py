#!/usr/bin/env python3
"""Verify actual affine HATCH pattern packets, phases, signed dashes and WCS Origin."""
from pathlib import Path
import argparse, copy, hashlib, json, math, struct
import numpy as np
import ezdxf
from verify_datatable import records, first, check, audit
from verify_hatch_conic import basis, world
from verify_hatch_spline_relations import packet as spline_packet, scalar
ROOT=Path(__file__).resolve().parents[1]

def matrix(operation):
    if operation==0:return np.eye(3)
    if operation==1:
        c,s=math.cos(.43),math.sin(.43);return np.array([[c,-s,0],[s,c,0],[0,0,1]])
    if operation==2:return np.diag([2,3,.5])
    if operation==3:return np.array([[1,.75,-.2],[0,1,.5],[0,0,1]])
    if operation==4:return np.diag([-1,1,1])
    return np.diag([2,3,0] if operation==5 else [100,.1,2])

def families(tags):
    at=next(i for i,t in enumerate(tags) if t[0]==78);count=tags[at][1];index=at+1;result=[]
    def take(code):
        nonlocal index
        check(tags[index][0]==code,'Pattern packet order/count');value=tags[index][1];index+=1;return value
    for _ in range(count):
        angle=take(53);base=np.array([take(43),take(44)]);offset=np.array([take(45),take(46)])
        dashes=[take(49) for _ in range(take(79))];radians=math.radians(angle)
        result.append({'angle':angle,'base':base,'offset':offset,'direction':np.array([math.cos(radians),math.sin(radians)]),'dashes':dashes})
    return result

def acad(tags):
    at=next(i for i,t in enumerate(tags) if t==(1001,'ACAD'))
    end=next((i for i in range(at+1,len(tags)) if tags[i][0]==1001),len(tags));return tags[at+1:end]

def samples(line):
    breaks=[0.];period=0.
    for dash in line['dashes']:
        breaks.extend([period+abs(dash)/2,period+abs(dash)]);period+=abs(dash)
    return np.array([line['base']+n*line['offset']+(m*period+t)*line['direction']
                     for n in (-3,-1,0,2,5) for m in (-2,0,3) for t in breaks])

def near(expected,observed,message):
    error=np.linalg.norm(expected-observed,axis=-1);scale=np.maximum(1,np.maximum(np.linalg.norm(expected,axis=-1),np.linalg.norm(observed,axis=-1)))
    check(np.isfinite(observed).all() and np.all(error<=2e-10*scale),message)
    return float(np.max(error))

def check_output(data,source,operation,plane):
    actual={first(t,8):t for t in records(data).values() if t[0]==(0,'HATCH')};check(actual.keys()==source.keys(),'HATCH inventory')
    old=basis((0,0,1) if plane==0 else (1,2,3));a=matrix(operation);translation=np.array([7,-11,0]);count=points=0;maximum=0
    for name,tags in actual.items():
        original=source[name];before=families(original);after=families(tags);check(len(before)==len(after),'Pattern family inventory')
        for code in (2,70,75,76,77):check(first(tags,code)==first(original,code),'Pattern header changed '+str(code))
        ocs=basis(first(tags,210));elevation=first(tags,10)[2]
        for wanted,line in zip(before,after):
            direction=np.append(wanted['direction'],0)@old.T@a.T;stretch=np.linalg.norm(direction)
            near(direction/stretch,np.append(line['direction'],0)@ocs.T,'Pattern world direction')
            check(len(wanted['dashes'])==len(line['dashes']),'Dash count')
            for prior,value in zip(wanted['dashes'],line['dashes']):
                check(math.copysign(1,prior)==math.copysign(1,value),'Dash/gap/signed dot sign')
                check((prior==0)==(value==0),'Dot changed to or from dash')
                check(abs(prior*stretch-value)<=2e-10*max(1,abs(prior*stretch)),'Signed dash length')
                if prior==0:check(struct.pack('>d',prior)==struct.pack('>d',value),'Signed zero storage')
            expected=world(old,samples(wanted),4)@a.T+translation;observed=world(ocs,samples(line),elevation)
            maximum=max(maximum,near(expected,observed,'Pattern family phase and dash world points'));points+=len(expected);count+=1
            near(world(old,np.array([wanted['offset']]),0)@a.T,world(ocs,np.array([line['offset']]),0),'Pattern longitudinal/perpendicular offset vector')
        prior_acad=acad(original);actual_acad=acad(tags);check(len(prior_acad)==len(actual_acad),'ACAD record inventory')
        origin_at=next(i for i,t in enumerate(prior_acad) if t[0]==1010)
        expected_origin=np.array(prior_acad[origin_at][1])@a.T+translation
        near(expected_origin,np.array(actual_acad[origin_at][1]),'Separate stored WCS Point2d Origin')
        check(actual_acad[origin_at][1][2]==0,'Stored Origin Z must remain zero')
        check(prior_acad[:origin_at]+prior_acad[origin_at+1:]==actual_acad[:origin_at]+actual_acad[origin_at+1:],'Unrelated ACAD payload changed')
    return count,points,maximum

def corrupt(data,defect):
    lines=data.decode().splitlines();pairs=[(int(lines[i]),lines[i+1]) for i in range(0,len(lines),2)]
    at=next(i for i,p in enumerate(pairs) if p==(8,'CUSTOM'))
    code={'angle':53,'base':43,'longitudinal-offset':45,'dash-sign':49,'dash-length':49,'dot':49,'origin':1010,'double':77}[defect]
    candidates=[i for i in range(at+1,len(pairs)) if pairs[i][0]==code]
    index=candidates[2] if defect=='dot' else candidates[0];value=float(pairs[index][1])
    if defect=='dash-sign':value=-value
    elif defect=='double':value=1
    else:value+=.125
    pairs[index]=(code,str(int(value)) if defect=='double' else str(value));return ''.join(f'{c}\n{v}\n' for c,v in pairs).encode()

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args()
    fixture=ROOT/'tests/fixtures/hatch-pattern-affine';manifest=json.loads((fixture/'manifest.json').read_text());check((manifest['producer'],manifest['version'])==('ezdxf','1.4.4'),'Producer pin')
    outputs=families_count=points=negative=0;maximum=0;sample=None
    for item in manifest['files']:
        data=(fixture/item['file']).read_bytes();check(hashlib.sha256(data).hexdigest()==item['sha256'],'Pinned producer changed')
        source={first(t,8):t for t in records(data).values() if t[0]==(0,'HATCH')};check(set(source)=={'PREDEFINED','CUSTOM'},'Producer inventory')
        for entry in item['packets']:
            tags=source[entry['label']];at=next(i for i,t in enumerate(tags) if t[0]==78)
            check([list(t) for t in tags[at:at+len(entry['raw_pattern_tags'])]]==entry['raw_pattern_tags'],'Actual producer wire packet')
        for operation in range(7):
            for plane in range(2):
                file=args.artifacts/f'hatch-pattern-affine-AutoCad{item["year"]}-{item["binary"]}-{operation}-{plane}.dxf';output=file.read_bytes()
                check(output.startswith(b'AutoCAD Binary DXF')==item['binary'],'Output transport')
                count,probes,error=check_output(output,source,operation,plane);families_count+=count;points+=probes;maximum=max(maximum,error)
                doc=ezdxf.readfile(file);audit(doc);check(doc.dxfversion=={2000:'AC1015',2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}[item['year']],'Output profile')
                line=list(doc.modelspace().query('LINE'));check(len(line)==1 and tuple(line[0].dxf.start)==(1,2,3) and tuple(line[0].dxf.end)==(4,5,6),'Following LINE changed');outputs+=1
                if item['year']==2018 and not item['binary'] and operation==3 and plane==1:sample=(output,source,operation,plane)
    check(outputs==168 and families_count==1008,'Mandatory actual-output inventory')
    for defect in ('angle','base','longitudinal-offset','dash-sign','dash-length','dot','origin','double'):
        try:check_output(corrupt(sample[0],defect),*sample[1:])
        except (ValueError,AssertionError):negative+=1
        else:raise ValueError('Accepted actual-output corruption '+defect)
    for binary in (False,True):
        for operation in range(3):
            file=args.artifacts/f'hatch-pattern-affine-origin-{binary}-{operation}.dxf';data=file.read_bytes();check(data.startswith(b'AutoCAD Binary DXF')==binary,'Origin output transport');audit(ezdxf.readfile(file))
            tags=[t for t in records(data).values() if t[0]==(0,'HATCH')];check(len(tags)==1,'Origin HATCH inventory')
            expected=np.array([4.2,4,0] if operation==0 else [11,0,0] if operation==1 else [.16,.2,0]);near(expected,np.array(first(acad(tags[0]),1010)),'Origin-specific map actual packet')
    mixed=0
    for binary in (False,True):
        file=args.artifacts/f'hatch-pattern-affine-mixed-{binary}.dxf';data=file.read_bytes();check(data.startswith(b'AutoCAD Binary DXF')==binary,'Mixed transport');audit(ezdxf.readfile(file))
        raw=(ROOT/'tests/fixtures/hatch-spline-relations'/f'ezdxf-hatch-spline-R2018-{"binary" if binary else "ascii"}.dxf').read_bytes()
        source={first(t,1000):t for t in records(raw).values() if t[0]==(0,'HATCH')};actual={first(t,1000):t for t in records(data).values() if t[0]==(0,'HATCH')};check(actual.keys()==source.keys(),'Mixed HATCH inventory')
        for name,tags in actual.items():
            prior=copy.deepcopy(spline_packet(source[name],2018));edge=spline_packet(tags,2018)
            if not prior['rational']:prior['controls'][0][2]=-2.5
            for field in ('degree','rational','periodic','knots'):check(scalar(prior[field])==scalar(edge[field]),'Mixed stored '+field)
            ocs=basis(first(tags,210));elevation=first(tags,10)[2]
            for field in ('controls','fits'):
                check(len(prior[field])==len(edge[field]),'Mixed '+field+' count')
                for wanted,point in zip(prior[field],edge[field]):
                    near(np.array([*wanted[:2],0.])@matrix(3).T+np.array([7,-11,0]),world(ocs,np.array([point[:2]]),elevation)[0],'Mixed world '+field)
                    if field=='controls':check(scalar(wanted[2])==scalar(point[2]),'Mixed stored weight')
            for field in ('start','end'):
                check((prior[field] is None)==(edge[field] is None),'Mixed tangent presence')
                if prior[field] is not None:near(np.array([*prior[field],0.])@matrix(3).T,world(ocs,np.array([edge[field]]),0)[0],'Mixed world tangent')
            check(len(families(tags))==1,'Mixed explicit family absent');mixed+=1
    check(mixed==8,'Mixed spline inventory')
    print(json.dumps({'mixed_stored_spline_packets':mixed,'outputs':outputs,'origin_specific_outputs':6,'pattern_family_packets':families_count,'world_pattern_probes':points,'maximum_world_error':maximum,'actual_output_negative_controls':negative,'audit_errors':0,'audit_fixes':0,'native_cad_execution':False},sort_keys=True))
if __name__=='__main__':main()
