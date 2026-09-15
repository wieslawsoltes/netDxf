#!/usr/bin/env python3
"""Compare transformed stored HATCH geometry with independent world-coordinate math."""
from pathlib import Path
import argparse, copy, hashlib, json, math, tempfile
import numpy as np
import ezdxf
from ezdxf.math import OCS
from verify_datatable import records, first, check, audit
from verify_hatch_spline_relations import packet, scalar, VERSIONS
ROOT=Path(__file__).resolve().parents[1]

def transform(operation):
    if operation==0:return np.eye(3)
    if operation==1:
        a,b=.4,-.3
        return np.array([[1,0,0],[0,math.cos(a),-math.sin(a)],[0,math.sin(a),math.cos(a)]]) @ np.array([[math.cos(b),0,math.sin(b)],[0,1,0],[-math.sin(b),0,math.cos(b)]])
    if operation==2:return np.diag([2,3,.5])
    if operation==3:return np.array([[1,.75,-.2],[0,1,.5],[.3,0,1]])
    return np.diag([-1,1,1])

def near(a,b,what):
    a,b=np.array(a,dtype=float),np.array(b,dtype=float)
    check(np.isfinite(a).all() and np.isfinite(b).all() and np.linalg.norm(a-b)<=2e-10*max(1,np.linalg.norm(a),np.linalg.norm(b)),what)

def world(ocs,point,elevation=0):return np.array(tuple(ocs.to_wcs((point[0],point[1],elevation))))

def check_output(data, expected, year, operation, plane):
    actual={}
    for tags in records(data).values():
        if tags[0]!=(0,'HATCH'):continue
        name=first(tags,1000);check(name not in actual,'Duplicate HATCH label');actual[name]=tags
    check(set(actual)==set(expected),'HATCH inventory changed')
    matrix=transform(operation);translation=np.array([7,-11,13]);old_ocs=OCS((0,0,1) if plane==0 else (1,2,3))
    for name,source in expected.items():
        tags=actual[name];edge=packet(tags,year);wanted=copy.deepcopy(packet(source,year))
        if not wanted['rational']:wanted['controls'][0][2]=-2.5
        for field in ('degree','rational','periodic','knots'):check(scalar(edge[field])==scalar(wanted[field]),'Stored '+field+' changed')
        check(first(tags,5)==first(source,5),'HATCH source identity changed')
        check(first(tags,92)==first(source,92),'Boundary path flags changed')
        check(first(tags,71)==0,'Transformed HATCH retained sources')
        ocs=OCS(first(tags,210));elevation=first(tags,10)[2]
        check(len(edge['controls'])==len(wanted['controls']) and len(edge['fits'])==len(wanted['fits']),'Spline counts changed')
        for a,b in zip(wanted['controls'],edge['controls']):
            check(scalar(a[2])==scalar(b[2]),'Stored weight changed')
            near(matrix@world(old_ocs,a,4)+translation,world(ocs,b,elevation),'World control differs')
        for a,b in zip(wanted['fits'],edge['fits']):near(matrix@world(old_ocs,a,4)+translation,world(ocs,b,elevation),'World fit point differs')
        for field in ('start','end'):
            check((wanted[field] is None)==(edge[field] is None),'Tangent presence changed')
            if wanted[field] is not None:near(matrix@world(old_ocs,wanted[field]),world(ocs,edge[field]),'World tangent differs')
        # The closing LINE follows the spline and has independent start/end points.
        si=source.index((72,1));ai=tags.index((72,1))
        for code in (10,11):near(matrix@world(old_ocs,first(source[si:],code),4)+translation,world(ocs,first(tags[ai:],code),elevation),'Closing LINE geometry differs')
        si=next(i for i,t in enumerate(source) if t[0]==98);ai=next(i for i,t in enumerate(tags) if t[0]==98)
        check(source[si][1]==tags[ai][1],'Seed count changed')
        for offset in range(source[si][1]):near(matrix@world(old_ocs,source[si+offset+1][1],4)+translation,world(ocs,tags[ai+offset+1][1],elevation),'Seed world position differs')
    return len(actual)

def corrupt(data,defect):
    lines=data.decode('utf-8').splitlines();pairs=[(int(lines[i]),lines[i+1]) for i in range(0,len(lines),2)]
    start=next(i for i,p in enumerate(pairs) if p==(100,'AcDbHatch'));spline=next(i for i in range(start,len(pairs)) if pairs[i][0]==94)
    code={'rational':73,'periodic':74,'weight':42,'knot':40,'normal':210,'control':10}[defect]
    begin=start if defect=='normal' else spline
    at=next(i for i in range(begin,len(pairs)) if pairs[i][0]==code)
    old=pairs[at][1];value=str(1-int(old)) if defect in ('rational','periodic') else str(float(old)+.25)
    pairs[at]=(code,value)
    return ''.join(f'{c}\n{v}\n' for c,v in pairs).encode()

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args()
    fixture=ROOT/'tests/fixtures/hatch-spline-relations';manifest=json.loads((fixture/'manifest.json').read_text());outputs=edges=controls=0;sample=None
    for source in manifest['files']:
        data=(fixture/source['file']).read_bytes();check(hashlib.sha256(data).hexdigest()==source['sha256'],'Pinned producer source changed');year=source['year'];binary=source['binary'];expected={first(t,1000):t for t in records(data).values() if t[0]==(0,'HATCH')}
        for operation in range(5):
            for plane in range(2):
                path=args.artifacts/f'hatch-affine-AutoCad{year}-{binary}-{operation}-{plane}.dxf';output=path.read_bytes();check(output.startswith(b'AutoCAD Binary DXF')==binary,'Actual output transport differs');edges+=check_output(output,expected,year,operation,plane)
                doc=ezdxf.readfile(path);check(doc.dxfversion==VERSIONS[year],'Output profile differs');audit(doc)
                lines=list(doc.modelspace().query('LINE'));check(len(lines)==1 and tuple(lines[0].dxf.start)==(1,2,3) and tuple(lines[0].dxf.end)==(4,5,6),'Following geometry changed');outputs+=1
                if not binary and year==2018 and operation==3 and plane==1:sample=(output,expected,year,operation,plane)
    check(outputs==120 and edges==480,'Mandatory spline geometry inventory differs')
    polyline_outputs=0
    for binary in (False,True):
        for operation in range(5):
            for plane in range(2):
                path=args.artifacts/f'hatch-affine-polyline-{binary}-{operation}-{plane}.dxf';data=path.read_bytes()
                check(data.startswith(b'AutoCAD Binary DXF')==binary,'Polyline output transport differs')
                doc=ezdxf.readfile(path);audit(doc);hatches=list(doc.modelspace().query('HATCH'));check(len(hatches)==1,'Polyline hatch count differs');hatch=hatches[0]
                check(hatch.dxf.pixel_size==.0625,'Stored pixel size changed');check(len(hatch.paths)==1,'Polyline path count differs');boundary=hatch.paths[0]
                check(boundary.is_closed and boundary.path_type_flags==7 and len(boundary.vertices)==4,'Stored polyline closure/flags/count differs')
                old_ocs=OCS((0,0,1) if plane==0 else (1,2,3));new_ocs=OCS(hatch.dxf.extrusion)
                for a,b in zip(((0,0),(10,0),(10,5),(0,5)),boundary.vertices):
                    near(transform(operation)@world(old_ocs,a,4)+np.array([7,-11,13]),world(new_ocs,b,hatch.dxf.elevation.z),'Polyline world vertex differs');check(b[2]==0,'Stored straight bulge changed')
                polyline_outputs+=1
    check(polyline_outputs==20,'Mandatory polyline inventory differs')
    for defect in ('rational','periodic','weight','knot','normal','control'):
        bad=corrupt(sample[0],defect)
        try:check_output(bad,*sample[1:])
        except (ValueError,AssertionError):controls+=1
        else:raise ValueError('Actual DXF corruption was accepted: '+defect)
    print(json.dumps({'outputs':outputs+polyline_outputs,'polyline_outputs':polyline_outputs,'spline_packets':edges,'actual_output_negative_controls':controls,'audit_errors':0,'audit_fixes':0,'source_producer':'ezdxf 1.4.4','native_cad_execution':False,'curve_evaluation':False},sort_keys=True))
if __name__=='__main__':main()
