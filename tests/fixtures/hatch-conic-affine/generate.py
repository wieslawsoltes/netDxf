"""Generate independent producer packets; no third-party implementation is copied."""
from pathlib import Path
import hashlib, io, json
import ezdxf
from ezdxf.lldxf.tagwriter import TagCollector
ROOT = Path(__file__).resolve().parent
manifest = {'producer': 'ezdxf', 'version': ezdxf.__version__, 'files': [], 'cases': [],
 'scope': 'Actual ASCII and binary producer output; raw endpoint angles distinguish zero and full turns. No native CAD execution.'}
assert ezdxf.__version__ == '1.4.4'
for year in (2000, 2004, 2007, 2010, 2013, 2018):
 for binary in (False, True):
  doc = ezdxf.new('R'+str(year))
  def hatch(name):
   doc.layers.new(name)
   return doc.modelspace().add_hatch(dxfattribs={'layer': name})
  cases = []
  for kind in ('ARC','ELLIPSE'):
   for ccw in (True, False):
    for label, start, end in (('SHORT',30,150),('LONG',150,390),('ZERO',0,0),('FULL',30,390),('CARDINAL',90,270)):
     name = f'{kind}_{int(ccw)}_{label}'
     path = hatch(name).paths.add_edge_path(flags=25)
     edge = path.add_arc((2,3),5,start,end,ccw) if kind=='ARC' else path.add_ellipse((2,3),(3,4),.4,start,end,ccw)
     writer = TagCollector(dxfversion=doc.dxfversion); edge.export_dxf(writer)
     cases.append({'name':name,'kind':kind,'producer_start':start,'producer_end':end,'producer_ccw':ccw,
       'raw_tags':[[t.code,list(t.value) if isinstance(t.value,tuple) else t.value] for t in writer.tags]})
  for label, bulge, closed in (('POS',.25,True),('NEG',-.25,True),('LONG',2.,False),('LONG_NEG',-2.,False),('SMALL',.001,False)):
   name = 'BULGE_'+label
   hatch(name).paths.add_polyline_path([(0,0,bulge),(10,0,0),(10,5,0),(0,5,0)],is_closed=closed,flags=25)
   cases.append({'name':name,'kind':'POLYLINE','bulge':bulge,'closed':closed})
  doc.modelspace().add_line((1,2,3),(4,5,6))
  stream = io.BytesIO() if binary else io.StringIO(); doc.write(stream,fmt='bin' if binary else 'asc')
  data=stream.getvalue(); data=data if binary else data.encode('ascii')
  file=ROOT/f'ezdxf-hatch-conic-R{year}-{"binary" if binary else "ascii"}.dxf'; file.write_bytes(data)
  loaded=ezdxf.readfile(file); audit=loaded.audit(); assert not audit.errors and not audit.fixes
  manifest['files'].append({'file':file.name,'year':year,'binary':binary,'sha256':hashlib.sha256(data).hexdigest(),'hatches':len(cases),'audit_errors':0,'audit_fixes':0})
  if not manifest['cases']: manifest['cases']=cases
(ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Generated 12 drawings, 300 HATCH packets, zero audit errors or repairs.')
