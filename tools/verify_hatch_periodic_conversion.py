#!/usr/bin/env python3
"""Check actual HATCH/standalone periodic packets and emitted evaluator samples."""
from pathlib import Path
import argparse,copy,hashlib,json,math
from fractions import Fraction
from functools import lru_cache
import numpy as np
import ezdxf
from ezdxf.math import BSpline,OCS,Vec3
from verify_datatable import records,first,check,audit
from verify_hatch_spline_relations import scalar
ROOT=Path(__file__).resolve().parents[1]

def packet(tags,year):
    i=next(i for i,t in enumerate(tags) if t==(72,4))+1
    def take(code):
        nonlocal i
        check(tags[i][0]==code,'Periodic spline packet field/order');value=tags[i][1];i+=1;return value
    degree=take(94);rational=take(73);periodic=take(74);knots_count=take(95);controls_count=take(96)
    knots=[take(40) for _ in range(knots_count)];controls=[]
    for _ in range(controls_count):
        xy=take(10);weight=take(42) if tags[i][0]==42 else 1.;controls.append([xy[0],xy[1],weight])
    fits=[];start=end=None
    if year>=2010:
        fits=[list(take(11))[:2] for _ in range(take(97))]
        if tags[i][0]==12:start=list(take(12))[:2]
        if tags[i][0]==13:end=list(take(13))[:2]
    check(tags[i][0]==97,'Periodic source-reference boundary')
    return dict(degree=degree,rational=rational,periodic=periodic,knots=knots,controls=controls,fits=fits,start=start,end=end)

def near(expected,actual,message):
    expected=np.asarray(expected);actual=np.asarray(actual);error=np.linalg.norm(expected-actual,axis=-1);scale=np.maximum(1,np.maximum(np.linalg.norm(expected,axis=-1),np.linalg.norm(actual,axis=-1)))
    check(np.isfinite(actual).all() and np.all(error<=2e-10*scale),message);return float(error.max())

def world(ocs,point,elevation=0):return np.array(ocs.to_wcs(Vec3(point[0],point[1],elevation)))

def check_output(data,numeric,source,compact,plane,link,year):
    wire=records(data);hatches={first(t,8):t for t in wire.values() if t[0]==(0,'HATCH')};splines={first(t,8):t for t in wire.values() if t[0]==(0,'SPLINE')}
    check(hatches.keys()==source.keys()==splines.keys()==numeric.keys(),'Periodic object inventory');ocs=OCS((0,0,1) if plane==0 else (1,2,3));maximum=0;count=0
    for name,original in source.items():
        before=packet(original,year);after=packet(hatches[name],year);expected=copy.deepcopy(before)
        if compact:expected['controls']=expected['controls'][expected['degree']:]
        check(scalar(expected)==scalar(after),'Original HATCH stored packet changed '+name)
        tags=splines[name];degree=first(tags,71);check(degree==before['degree'],'Standalone degree');check(first(tags,70)&7==7,'Standalone rational/closed/periodic convention')
        controls=[v for c,v in tags if c==10];weights=[v for c,v in tags if c==41];knots=[v for c,v in tags if c==40]
        check(scalar(knots)==scalar(before['knots']),'Stored knot vector regenerated');check(len(controls)==len(before['controls']),'Standalone expanded count')
        check(scalar(weights)==scalar([p[2] for p in before['controls']]),'Stored weights changed')
        for expected_point,actual_point in zip(before['controls'],controls):near(world(ocs,expected_point,4),actual_point,'Standalone world control')
        fits=[v for c,v in tags if c==11];check(len(fits)==len(before['fits']),'Standalone fit count')
        for expected_point,actual_point in zip(before['fits'],fits):near(world(ocs,expected_point,4),actual_point,'Standalone world fit')
        for field,code in (('start',12),('end',13)):
            values=[v for c,v in tags if c==code];check(len(values)==(0 if before[field] is None else 1),'Standalone tangent presence')
            if values:near(world(ocs,before[field]),values[0],'Standalone world tangent')
        curve=BSpline([p[:2] for p in before['controls']],degree+1,before['knots'],[p[2] for p in before['controls']])
        scale=(curve.knots()[-1]-curve.knots()[0])/(before['knots'][-1]-before['knots'][0]);shift=curve.knots()[0]-scale*before['knots'][0];start=before['knots'][degree];end=before['knots'][len(before['controls'])]
        check(np.allclose(np.array(before['knots'])*scale+shift,curve.knots(),rtol=1e-13,atol=0),'Independent evaluator parameter gauge')
        wanted=[world(ocs,curve.point((start+(end-start)*i/64)*scale+shift),4) for i in range(64)]
        check(len(numeric[name])==64,'Actual evaluator sample count');maximum=max(maximum,near(wanted,numeric[name],'Periodic evaluator world samples'));count+=1
        check(first(hatches[name],71)==int(link),'Boundary association state')
        # The only source reference follows the outer path's last group 97.
        htags=hatches[name];at=[i for i,t in enumerate(htags) if t[0]==97][-1];check(htags[at][1]==int(link),'Boundary source count')
        if link:check(htags[at+1]==(330,first(tags,5)),'Boundary source identity')
    return count,maximum

def corrupt(data,kind):
    text=data.decode().splitlines();pairs=[(int(text[i]),text[i+1]) for i in range(0,len(text),2)]
    at=next(i for i,p in enumerate(pairs) if p==(0,'SPLINE'));code={'control':10,'weight':41,'knot':40,'periodic':70}[kind]
    index=next(i for i in range(at+1,len(pairs)) if pairs[i][0]==code);value=float(pairs[index][1]);value=int(value)&~2 if kind=='periodic' else value+.125
    pairs[index]=(code,str(value));return ''.join(f'{c}\n{v}\n' for c,v in pairs).encode()

def check_local_weights(directory):
    """Use exact rational arithmetic for the degree-one mixed-scale samples."""
    files=list(directory.glob('hatch-periodic-local-weight-*.json'))
    check(len(files)==4,'Local periodic weight-scale output inventory');count=0
    for file in files:
        data=json.loads(file.read_text());controls=data['controls'];weights=data['weights'];knots=data['knots']
        controls=controls[-1:]+controls;weights=weights[-1:]+weights
        start=knots[1];end=knots[-2];delta=(end-start)/len(data['samples'])
        for index,actual in enumerate(data['samples']):
            parameter=start+delta*index
            span=next(i for i in range(1,len(controls)) if knots[i]<=parameter<knots[i+1])
            alpha=(Fraction(parameter)-Fraction(knots[span]))/(Fraction(knots[span+1])-Fraction(knots[span]))
            left=(1-alpha)*Fraction(weights[span-1]);right=alpha*Fraction(weights[span]);denominator=left+right
            expected=[float((left*Fraction(controls[span-1][axis])+right*Fraction(controls[span][axis]))/denominator) for axis in range(3)]
            if data['extreme']:
                check(np.allclose(expected,actual,rtol=2e-10,atol=0),'Exact-rational subnormal coordinate contribution')
            else:near(expected,actual,'Exact-rational local periodic sample')
            count+=1
    return count

def check_scaled_arithmetic(directory):
    constants=list(directory.glob('hatch-periodic-constant-*.json'))
    check(len(constants)==50,'Constant periodic output inventory');constant_samples=0
    for file in constants:
        data=json.loads(file.read_text());coordinate=data['coordinate']
        for actual in data['samples']:
            check(actual==[coordinate,-coordinate,0],'Exact constant periodic coordinate');constant_samples+=1
    files=list(directory.glob('hatch-periodic-numeric-*.json'))
    check(len(files)==43,'Scaled periodic arithmetic output inventory');samples=0
    for file in files:
        data=json.loads(file.read_text());degree=data['degree'];controls=data['controls'];weights=data['weights'];knots=data['knots']
        controls=controls[-degree:]+controls;weights=weights[-degree:]+weights;exact_knots=[Fraction(k) for k in knots]
        start=knots[degree];delta=(knots[-degree-1]-start)/len(data['samples'])
        for index,actual in enumerate(data['samples']):
            parameter=Fraction(start+delta*index)
            @lru_cache(None)
            def basis(i,p):
                if p==0:return Fraction(int(exact_knots[i]<=parameter<exact_knots[i+1]))
                return ((parameter-exact_knots[i])/(exact_knots[i+p]-exact_knots[i])*basis(i,p-1)
                    +(exact_knots[i+p+1]-parameter)/(exact_knots[i+p+1]-exact_knots[i+1])*basis(i+1,p-1))
            coefficients=[basis(i,degree)*Fraction(weights[i]) for i in range(len(controls))]
            denominator=sum(coefficients)
            for axis in range(3):
                expected=float(sum(c*Fraction(p[axis]) for c,p in zip(coefficients,controls))/denominator)
                tolerance=max(4*math.ulp(expected),abs(expected)*2e-12)
                check(math.isfinite(actual[axis]) and abs(expected-actual[axis])<=tolerance,
                    'Exact-rational scaled periodic coordinate '+file.name+f' sample {index} axis {axis}')
            samples+=1
    return {'constant_samples':constant_samples,'exact_rational_scaled_samples':samples}

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args();fixture=ROOT/'tests/fixtures/hatch-periodic-conversion'
    manifest=json.loads((fixture/'manifest.json').read_text());check((manifest['producer'],manifest['version'])==('ezdxf','1.4.4'),'Producer pin');outputs=curves=0;maximum=0;sample=None
    for item in manifest['files']:
        data=(fixture/item['file']).read_bytes();check(hashlib.sha256(data).hexdigest()==item['sha256'],'Producer hash');source={first(t,8):t for t in records(data).values() if t[0]==(0,'HATCH')};check(len(source)==12,'Producer packet inventory')
        for compact in (False,True):
            for plane in (0,1):
                for link in (False,True):
                    stem=f'hatch-periodic-AutoCad{item["year"]}-{item["binary"]}-{compact}-{plane}-{link}';file=args.artifacts/(stem+'.dxf');output=file.read_bytes();numeric=json.loads((args.artifacts/(stem+'.json')).read_text())
                    check(output.startswith(b'AutoCAD Binary DXF')==item['binary'],'Output transport');count,error=check_output(output,numeric,source,compact,plane,link,item["year"]);curves+=count;maximum=max(maximum,error);outputs+=1
                    source_doc=ezdxf.readfile(file)
                    for h in source_doc.modelspace().query('HATCH'):
                        s=next(s for s in source_doc.modelspace().query('SPLINE') if s.dxf.layer==h.dxf.layer);check((h.dxf.handle in s.get_reactors())==link,'Created source reactor')
                    if compact:
                        valid_file=args.artifacts/(stem+'-curves.dxf');valid_data=valid_file.read_bytes();check(valid_data.startswith(b'AutoCAD Binary DXF')==item['binary'],'Canonical curve output transport')
                        original_curves={first(t,8):t for t in records(output).values() if t[0]==(0,'SPLINE')};valid_curves={first(t,8):t for t in records(valid_data).values() if t[0]==(0,'SPLINE')};check(original_curves.keys()==valid_curves.keys(),'Canonical curve inventory')
                        for name,tags in valid_curves.items():
                            for code in (10,11,12,13,40,41,70,71):check(scalar([v for c,v in tags if c==code])==scalar([v for c,v in original_curves[name] if c==code]),'Canonical conversion drawing changed stored SPLINE data')
                        doc=ezdxf.readfile(valid_file);check(not list(doc.modelspace().query('HATCH')),'Compatibility HATCH leaked into canonical curve drawing')
                    else:doc=source_doc
                    check(doc.dxfversion=={2000:'AC1015',2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}[item['year']],'Canonical output profile')
                    audit(doc);lines=list(doc.modelspace().query('LINE'));check(len(lines)==1 and tuple(lines[0].dxf.start)==(1,2,3) and tuple(lines[0].dxf.end)==(4,5,6),'Following LINE')
                    if item['year']==2018 and not item['binary'] and not compact and plane==1 and link:sample=(output,numeric,source,compact,plane,link,item["year"])
    check(outputs==96 and curves==1152,'Mandatory periodic output inventory');negative=0
    for kind in ('control','weight','knot','periodic','numeric'):
        candidate=list(sample)
        if kind=='numeric':candidate[1]=copy.deepcopy(candidate[1]);candidate[1][next(iter(candidate[1]))][0][0]+=.125
        else:candidate[0]=corrupt(candidate[0],kind)
        try:check_output(*candidate)
        except (ValueError,AssertionError):negative+=1
        else:raise ValueError('Accepted actual-output corruption '+kind)
    storage=list(args.artifacts.glob('hatch-periodic-storage-*.dxf'));check(len(storage)==26,'Unsupported conversion storage inventory')
    for file in storage:
        year={'AC1015':2000,'AC1018':2004,'AC1021':2007,'AC1024':2010,'AC1027':2013,'AC1032':2018}[ezdxf.readfile(file).dxfversion]
        hatches=[t for t in records(file.read_bytes()).values() if t[0]==(0,'HATCH')];check(len(hatches)==1 and packet(hatches[0],year)['periodic'],'Unsupported stored periodic flag lost')
    local_samples=check_local_weights(args.artifacts)
    arithmetic=check_scaled_arithmetic(args.artifacts)
    print(json.dumps({'outputs':outputs,'canonical_audited_drawings':96,'compact_compatibility_drawings':48,'additional_canonical_curve_drawings':48,'periodic_curves':curves,'actual_evaluator_world_samples':curves*64,'exact_rational_local_weight_samples':local_samples,**arithmetic,'maximum_world_error':maximum,'storage_only_outputs':len(storage),'actual_output_negative_controls':negative,'audit_errors':0,'audit_fixes':0,'native_cad_execution':False},sort_keys=True))
if __name__=='__main__':main()
