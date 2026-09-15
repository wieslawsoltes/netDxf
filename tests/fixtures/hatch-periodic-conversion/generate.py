"""Actual public producer packets; no producer implementation is copied."""
from pathlib import Path
import hashlib,io,json,sys
import ezdxf
from ezdxf.math import closed_uniform_bspline,BSpline
ROOT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT.parents[2]/'tools'))
from verify_datatable import records,first
controls=[(0,0),(4,7),(10,1),(7,-5),(-3,-2)];weights=[.5,1.25,2,1,.75]
manifest={'producer':'ezdxf','version':ezdxf.__version__,'files':[],'cases':[]}
for year in (2000,2004,2007,2010,2013,2018):
 for binary in (False,True):
  doc=ezdxf.new('R'+str(year));doc.appids.new('PERIODIC_CASE')
  for degree in (1,2,3):
   for weighted in (False,True):
    for nonuniform in (False,True):
     curve=closed_uniform_bspline(controls,degree+1,weights if weighted else None)
     points=list(curve.control_points);knots=list(curve.knots());w=list(curve.weights())
     if nonuniform:
      spans=[.5,1.5,.75,2,1.25];knots=[-3.25]
      for i in range(len(points)+degree):knots.append(knots[-1]+spans[(i-degree)%5])
      curve=BSpline(points,degree+1,knots,w if weighted else None)
     name=f'P{degree}_W{int(weighted)}_N{int(nonuniform)}';h=doc.modelspace().add_hatch();h.dxf.layer=name
     path=h.paths.add_edge_path(flags=25);kw={}
     if year>=2010:kw={'fit_points':[(1,2),(3,5),(8,-1)],'start_tangent':(7,11),'end_tangent':(-3,2)}
     path.add_spline(control_points=points,knot_values=knots,weights=w if weighted else None,degree=degree,periodic=1,**kw)
     h.set_xdata('PERIODIC_CASE',[(1000,name)]);h.set_seed_points([(2,3)])
     if year==2018 and not binary:
      start,end=knots[degree],knots[len(points)];scale=(curve.knots()[-1]-curve.knots()[0])/(knots[-1]-knots[0]);shift=curve.knots()[0]-scale*knots[0]
      samples=[list(curve.point((start+(end-start)*i/64)*scale+shift)) for i in range(65)]
      manifest['cases'].append({'name':name,'degree':degree,'weighted':weighted,'nonuniform':nonuniform,'controls':[list(p) for p in points],'weights':w,'knots':knots,'domain':[start,end],'public_evaluator_knot_scale':scale,'public_evaluator_knot_shift':shift,'samples':samples})
  doc.modelspace().add_line((1,2,3),(4,5,6));p=ROOT/f'ezdxf-hatch-periodic-R{year}-{"binary" if binary else "ascii"}.dxf';stream=io.BytesIO() if binary else io.StringIO();doc.write(stream,fmt='bin' if binary else 'asc');data=stream.getvalue();data=data if binary else data.encode();p.write_bytes(data)
  check=ezdxf.readfile(p);a=check.audit();assert not a.errors and not a.fixes
  packets={first(t,8):t for t in records(data).values() if t[0]==(0,'HATCH')}
  manifest['files'].append({'file':p.name,'year':year,'binary':binary,'sha256':hashlib.sha256(data).hexdigest(),'hatches':len(packets),'audit_errors':0,'audit_fixes':0})
(ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Pinned12 drawings,144 actual periodic packets,12 public evaluator cases.')
