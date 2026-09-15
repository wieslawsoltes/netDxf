"""Pinned actual producer packets for stored HATCH pattern-family affine mapping."""
from pathlib import Path
import hashlib, io, json, math, sys
import ezdxf
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parents[2]/'tools'))
from verify_datatable import records,first
assert ezdxf.__version__=='1.4.4'
def rotate(angle,value):
 c,s=math.cos(math.radians(angle)),math.sin(math.radians(angle));return [c*value[0]-s*value[1],s*value[0]+c*value[1]]
definitions=[
 [15,[1,2],rotate(15,[.5,3]),[2,-1,0.,-0.,-.5]],
 [95,[-4,1],rotate(95,[-.75,-2]),[]],
 [195,[2,-5],rotate(195,[1.25,0]),[1.5,-.25]],
 [35,[7,-3],rotate(35,[.25,1.75]),[0.,-1.25,-0.,-.5]]]
manifest={'producer':'ezdxf','version':ezdxf.__version__,'files':[],'scope':'Actual pattern packets, not native CAD execution. Definition helper rounding is part of the pinned source data; expectations derive from parsed packets. ACAD origin is separately authored compatibility data.'}
for year in (2000,2004,2007,2010,2013,2018):
 for binary in (False,True):
  doc=ezdxf.new('R'+str(year))
  for name,pattern,kind,angle,scale,origin,definition in [
   ('PREDEFINED','ANSI33',1,30,2,[11,13],None),
   ('CUSTOM','CUSTOM_AFFINE',2,27,.75,[-17,5],definitions)]:
   doc.layers.new(name);hatch=doc.modelspace().add_hatch(dxfattribs={'layer':name});hatch.paths.add_polyline_path([(0,0),(100,0),(100,50),(0,50)],is_closed=True,flags=25)
   hatch.set_pattern_fill(pattern,angle=angle,scale=scale,double=0,pattern_type=kind,definition=definition)
   hatch.set_seed_points([(20,20)])
   hatch.set_xdata('ACAD',[(1010,(*origin,0)),(1000,'unrelated suffix'),(1070,23),(1010,(101,202,303))])
  doc.modelspace().add_line((1,2,3),(4,5,6))
  stream=io.BytesIO() if binary else io.StringIO();doc.write(stream,fmt='bin' if binary else 'asc');data=stream.getvalue();data=data if binary else data.encode('ascii')
  file=ROOT/f'ezdxf-hatch-pattern-R{year}-{"binary" if binary else "ascii"}.dxf';file.write_bytes(data)
  parsed=[]
  for tags in records(data).values():
   if tags[0]!=(0,'HATCH'):continue
   begin=next(i for i,t in enumerate(tags) if t[0]==78);end=next(i for i in range(begin+1,len(tags)) if tags[i][0]==98)
   parsed.append({'label':first(tags,8),'type':first(tags,76),'angle':first(tags,52),'scale':first(tags,41),'double':first(tags,77),'raw_pattern_tags':tags[begin:end],'raw_acad_origin':first(tags,1010)})
  loaded=ezdxf.readfile(file);audit=loaded.audit();assert not audit.errors and not audit.fixes
  manifest['files'].append({'file':file.name,'year':year,'binary':binary,'sha256':hashlib.sha256(data).hexdigest(),'packets':parsed,'audit_errors':0,'audit_fixes':0})
(ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Generated 12 actual producer drawings, 24 HATCH patterns; zero audit errors or repairs.')
