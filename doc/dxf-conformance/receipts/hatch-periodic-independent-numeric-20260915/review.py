"""Independent exact rational periodic-curve oracle, with binary64 input values."""
import argparse
import json
import math
from fractions import Fraction as F
from functools import lru_cache
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('cases',type=Path)
parser.add_argument('outputs',type=Path)
parser.add_argument('report',type=Path)
args=parser.parse_args()
cases=json.loads(args.cases.read_text());outputs=json.loads(args.outputs.read_text())
issues=[];rejections=[];reverse_issues=[];sample_count=0;constant_count=0
for c,o in zip(cases,outputs):
    assert c['name']==o['name']
    if o['error']:
        rejections.append({'name':c['name'],'error':o['error']})
        continue
    p=c['degree'];controls=c['controls'][-p:]+c['controls'];weights=c['weights'][-p:]+c['weights']
    knots=list(map(F,c['knots']));u0=c['knots'][p];delta=(c['knots'][-p-1]-u0)/c['precision']
    @lru_cache(None)
    def basis(i,p,u):
        if p==0:return F(int(knots[i]<=u<knots[i+1]))
        return ((u-knots[i])/(knots[i+p]-knots[i])*basis(i,p-1,u)
                +(knots[i+p+1]-u)/(knots[i+p+1]-knots[i+1])*basis(i+1,p-1,u))
    for j,actual in enumerate(o['samples']):
        sample_count+=1
        # Evaluate at the actual binary64 sample parameter emitted by the API.
        u=F(u0+delta*j);coefficients=[basis(i,p,u)*F(w) for i,w in enumerate(weights)]
        denominator=sum(coefficients)
        expected=[float(sum(bi*F(ctrl[k]) for bi,ctrl in zip(coefficients,controls))/denominator) for k in range(2)]
        for k,(a,e) in enumerate(zip(actual,expected)):
            diff=abs(a-e);relative=diff/abs(e) if e else diff
            constant=all(ctrl[k]==controls[0][k] for ctrl in controls)
            if constant:constant_count+=1
            if relative>1e-10 and diff>2*math.ulp(e) or constant and abs(e)<2.2250738585072014e-308 and a!=e:
                issues.append({'name':c['name'],'sample':j,'axis':k,'actual':a,'expected':e,'relative':relative,'ulps':diff/math.ulp(e),'constant':constant})
        for k in range(2):
            a=o['samples'][(len(o['samples'])-j)%len(o['samples'])][k];b=o['reversed'][j][k]
            if abs(a-b)>max(abs(a),abs(b))*1e-9 and abs(a-b)>math.ulp(a)*4:
                reverse_issues.append({'name':c['name'],'sample':j,'axis':k,'forward':a,'reverse':b})
report={'cases':len(cases),'samples':sample_count,'constant_coordinates_checked':constant_count,'issues':issues,'rejections':rejections,'reverse_issues':reverse_issues}
args.report.write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({k:(len(v) if isinstance(v,list) else v) for k,v in report.items()}))
