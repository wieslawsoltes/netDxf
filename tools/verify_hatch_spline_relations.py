#!/usr/bin/env python3
"""Inspect stored HATCH spline packets without evaluating their curves."""
from pathlib import Path
import argparse, copy, hashlib, json, math, struct
import ezdxf
from verify_datatable import records, first, check, audit

ROOT=Path(__file__).resolve().parents[1]
VERSIONS={2000:'AC1015',2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}

def scalar(value):
    if isinstance(value,float):return struct.pack('>d',value).hex()
    if isinstance(value,(list,tuple)):return [scalar(item) for item in value]
    if isinstance(value,dict):return {key:scalar(item) for key,item in value.items()}
    return value

def validate(edge):
    degree=edge['degree'];controls=edge['controls'];knots=edge['knots']
    check(degree>0,'Spline degree must be positive')
    check(all(math.isfinite(v) for v in knots) and all(math.isfinite(v) for p in controls for v in p),'Nonfinite spline data')
    check(all(a<=b for a,b in zip(knots,knots[1:])),'Spline knots must be nondecreasing')
    if not edge['periodic']:
        check(len(controls)>=degree+1 and len(knots)==len(controls)+degree+1,'Nonperiodic knot/control/degree relationship differs')

def packet(tags,year):
    # records() compiles coordinate triplets into vectors but keeps each control's
    # immediately following optional weight separate, so omitted defaults stay indexed.
    i=next(i for i,t in enumerate(tags) if t==(72,4))+1;header={}
    while i<len(tags) and tags[i][0] in (94,73,74,95,96):
        code,value=tags[i];check(code not in header,'Duplicate spline header');header[code]=value;i+=1
    check(set(header)=={94,73,74,95,96},'Incomplete spline header')
    check(header[73] in (0,1) and header[74] in (0,1) and header[95]>=0 and header[96]>=0,'Invalid spline flags/counts')
    knots=[];controls=[];fits=[];start=None;end=None
    for _ in range(header[95]):
        check(tags[i][0]==40,'Counted knot packet differs');knots.append(float(tags[i][1]));i+=1
    for _ in range(header[96]):
        check(tags[i][0]==10,'Counted control packet differs');point=tags[i][1];i+=1;weight=1.0
        if tags[i][0]==42:weight=float(tags[i][1]);i+=1
        controls.append([float(point[0]),float(point[1]),weight])
    if year>=2010:
        check(tags[i][0]==97 and tags[i][1]>=0,'Missing fit count');count=tags[i][1];i+=1
        for _ in range(count):
            check(tags[i][0]==11,'Counted fit packet differs');fits.append([float(v) for v in tuple(tags[i][1])[:2]]);i+=1
        while tags[i][0] in (12,13):
            code,value=tags[i];point=[float(v) for v in tuple(value)[:2]];i+=1
            if code==12:check(start is None,'Repeated start tangent');start=point
            else:check(end is None,'Repeated end tangent');end=point
    check(tags[i]==(72,1),'Independent closing LINE edge was lost')
    edge={'degree':header[94],'rational':header[73],'periodic':header[74],'knots':knots,'controls':controls,'fits':fits,'start':start,'end':end}
    validate(edge);return edge

def inventory(data,year):
    wire=records(data);result={}
    for tags in wire.values():
        if tags[0]!=(0,'HATCH'):continue
        name=first(tags,1000);check(name not in result,'Duplicate producer spline label');result[name]={'handle':first(tags,5),'edge':packet(tags,year)}
    check(set(result)=={'QUADRATIC','RATIONAL','NONUNIFORM','PERIODIC'},'Producer HATCH inventory differs')
    return result

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args()
    fixture=ROOT/'tests/fixtures/hatch-spline-relations';manifest=json.loads((fixture/'manifest.json').read_text());check(manifest['producer']=='ezdxf' and manifest['version']=='1.4.4' and len(manifest['files'])==12,'Pinned producer inventory differs')
    count=0;edges=0
    for source in manifest['files']:
        data=(fixture/source['file']).read_bytes();check(hashlib.sha256(data).hexdigest()==source['sha256'],'Producer source hash differs');year=source['year'];binary=source['binary'];expected=inventory(data,year)
        for variant in ('','weights-','rational-weights-'):
            path=args.artifacts/f'hatch-spline-relations-{variant}AutoCad{year}-{binary}.dxf';output=path.read_bytes();check(output.startswith(b'AutoCAD Binary DXF')==binary,'Output transport differs')
            doc=ezdxf.readfile(path);check(doc.dxfversion==VERSIONS[year],'Output profile differs');audit(doc)
            actual=inventory(output,year);wanted=copy.deepcopy(expected)
            if variant=='weights-':
                for point,weight in zip(wanted['QUADRATIC']['edge']['controls'],(1.0,2.5,-0.0)):point[2]=weight
            elif variant=='rational-weights-':
                for point,weight in zip(wanted['RATIONAL']['edge']['controls'],(1.0,-2.5,1.0,-0.0)):point[2]=weight
            check(scalar(actual)==scalar(wanted),'Stored spline flags/counts/knots/controls/weights/fit data/tangents or identities changed')
            lines=list(doc.modelspace().query('LINE'));check(len(lines)==1 and tuple(lines[0].dxf.start)==(1,2,3) and tuple(lines[0].dxf.end)==(4,5,6),'Following geometry changed');count+=1;edges+=len(actual)
    controls=0;sample=wanted['RATIONAL']['edge']
    for defect in ('degree','count','descending','weight-loss','rational-flag'):
        bad=copy.deepcopy(sample)
        if defect=='degree':bad['degree']=0
        elif defect=='count':bad['knots'].pop()
        elif defect=='descending':bad['knots'][1]=100.0
        elif defect=='weight-loss':bad['controls'][1][2]=1.0
        else:bad['rational']=0
        try:validate(bad);check(scalar(bad)==scalar(sample),'Stored spline corruption control differs')
        except ValueError:controls+=1
        else:raise ValueError('Spline corruption control was accepted')
    check(count==36 and edges==144 and controls==5,'Mandatory output/edge/control count differs')
    print(json.dumps({'outputs':count,'spline_packets':edges,'producer_inputs':12,'negative_controls':controls,'audit_errors':0,'audit_fixes':0,'native_cad_execution':False,'periodic_shape_evaluation':False},sort_keys=True))
if __name__=='__main__':main()
