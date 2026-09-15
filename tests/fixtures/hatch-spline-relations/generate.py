from pathlib import Path
import ezdxf,hashlib,io,json,inspect
from ezdxf.entities.boundary_paths import SplineEdge
ROOT=Path(__file__).resolve().parent;out=ROOT
manifest={'producer':'ezdxf','version':ezdxf.__version__,'files':[], 'producer_boundary_module_sha256':hashlib.sha256(Path(inspect.getfile(SplineEdge)).read_bytes()).hexdigest(), 'producer_boundary_module':'ezdxf/entities/boundary_paths.py', 'scope':'Actual independent producer output and stored spline packets, not native CAD or periodic shape evaluation.'}
for year in (2000,2004,2007,2010,2013,2018):
 for binary in (False,True):
  doc=ezdxf.new('R'+str(year));doc.appids.new('HATCH_SPLINE_REL')
  for name,controls,knots,weights,degree,periodic in [
   ('QUADRATIC',[(0,0),(5,10),(10,0)],[0,0,0,1,1,1],None,2,0),
   ('RATIONAL',[(0,0),(3,9),(8,-2),(10,0)],[0,0,0,0,1,1,1,1],[.5,2,1.25,1],3,0),
   ('NONUNIFORM',[(0,0),(2,9),(5,5),(8,-3),(10,0)],[-2,-2,-2,-.5,1,4,4,4],None,2,0),
   ('PERIODIC',[(0,0),(5,10),(10,0),(0,0),(5,10)],[-2,-1,0,1,2,3,4,5],None,2,1)]:
   hatch=doc.modelspace().add_hatch();path=hatch.paths.add_edge_path();kwargs={}
   if year>=2010 and name=='QUADRATIC':kwargs={'fit_points':[(0,0),(5,5),(10,0)],'start_tangent':(10,20),'end_tangent':(10,-20)}
   edge=path.add_spline(control_points=controls,knot_values=knots,weights=weights,degree=degree,periodic=periodic,**kwargs)
   path.add_line(controls[-1],controls[0]);hatch.set_xdata('HATCH_SPLINE_REL',[(1000,name)])
  doc.modelspace().add_line((1,2,3),(4,5,6))
  file=out/f'ezdxf-hatch-spline-R{year}-{"binary" if binary else "ascii"}.dxf';stream=io.BytesIO() if binary else io.StringIO();doc.write(stream,fmt='bin' if binary else 'asc');data=stream.getvalue();data=data if binary else data.encode('ascii');file.write_bytes(data)
  check=ezdxf.readfile(file);audit=check.audit();assert not audit.errors and not audit.fixes,(file,audit.errors,audit.fixes)
  manifest['files'].append({'file':file.name,'year':year,'binary':binary,'sha256':hashlib.sha256(data).hexdigest(),'hatches':4,'audit_errors':0,'audit_fixes':0})
(ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n');print('Generated and reloaded12 producer drawings,48 spline edges,zero audits/repairs.')
