#!/usr/bin/env python3
"""Check API-authored and independently produced output settings with ezdxf1.4.4."""
from pathlib import Path
import argparse,hashlib,json,struct
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from verify_typed_container_inputs import wire_records
VERSIONS={2000:'AC1015',2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}
ROOT=Path(__file__).resolve().parents[1]

def check(value,message):
 if not value:raise ValueError(message)

def body(record,marker='AcDbPlotSettings'):
 start=next(i for i,t in enumerate(record) if t.code==100 and t.value==marker)+1
 end=next((i for i in range(start,len(record)) if record[i].code in(100,1001)),len(record))
 return [(t.code,struct.pack('<d',t.value) if isinstance(t.value,float) else t.value) for t in record[start:end]]

def precheck(path,year,binary):
 check(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Actual transport differs')
 doc=ezdxf.readfile(path);check(doc.dxfversion==VERSIONS[year],'Actual version differs')
 audit=doc.audit();check(not audit.errors and not audit.fixes,'Independent audit changes: '+str(audit.errors+audit.fixes))
 for name,cpp,count in [('PLOTSETTINGS','AcDbPlotSettings',33),('WIPEOUTVARIABLES','AcDbWipeoutVariables',1)]:
  c=doc.classes.get(name);check(c.dxf.cpp_class_name==cpp and c.dxf.is_an_entity==0,'Object class declaration differs')
  check(c.dxf.instance_count==count if year>=2004 else not c.dxf.hasattr('instance_count'),'Class count/profile differs')
 return doc,wire_records(path)

def external(path,source,year,binary):
 output,wire=precheck(path,year,binary);original=ezdxf.readfile(source);before=wire_records(source)
 pages=output.rootdict['ACAD_PLOTSETTINGS'];old=original.rootdict['ACAD_PLOTSETTINGS'];check(set(pages.keys())==set(old.keys()) and len(pages)==33,'Page inventory differs')
 for name in old.keys():
  a,b=old[name],pages[name];check(a.dxf.handle==b.dxf.handle and b.dxf.owner==pages.dxf.handle,'Page identity/ownership changed')
  check(body(before[a.dxf.handle])==body(wire[b.dxf.handle]),name+': exact public plot fields changed')
  check(a.get_reactors()==b.get_reactors() and a.get_xdata('QA_OUTPUT_SETTINGS')==b.get_xdata('QA_OUTPUT_SETTINGS'),name+': common metadata changed')
 a,b=original.layouts.get('IndependentSheet').dxf_layout,output.layouts.get('IndependentSheet').dxf_layout
 check(a.dxf.handle==b.dxf.handle and a.dxf.owner==b.dxf.owner,'Embedded layout identity/ownership changed')
 check(body(before[a.dxf.handle])==body(wire[b.dxf.handle]),'Embedded layout plot fields changed')
 check(a.get_xdata('QA_OUTPUT_SETTINGS')==b.get_xdata('QA_OUTPUT_SETTINGS'),'Embedded XData changed')
 for entity in [pages['Independent-07'],b]:
  handle=entity.dxf.get('shade_plot_handle')
  if year>=2007:check(handle is not None and output.entitydb[handle].dxftype()=='VISUALSTYLE','Optional333 does not identify retained VISUALSTYLE')
  else:check(handle is None,'Older source acquired an unqualified shade reference')
 a,b=original.rootdict['ACAD_WIPEOUT_VARS'],output.rootdict['ACAD_WIPEOUT_VARS']
 check(a.dxf.handle==b.dxf.handle and a.dxf.frame==b.dxf.frame and b.dxf.owner==output.rootdict.dxf.handle,'Wipeout state/owner changed')
 check(body(before[a.dxf.handle],'AcDbWipeoutVariables')==body(wire[b.dxf.handle],'AcDbWipeoutVariables'),'Wipeout payload changed')
 lines=output.modelspace().query('LINE');check(len(output.modelspace())==1 and len(lines)==1 and tuple(lines[0].dxf.start)==(1,2,3) and tuple(lines[0].dxf.end)==(4,5,6),'Drawing geometry changed')

def authored(path,year,binary):
 doc,wire=precheck(path,year,binary);pages=doc.rootdict['ACAD_PLOTSETTINGS'];check(len(pages)==33,'Authored page inventory differs')
 for code in range(33):
  item=pages[f'User-{code:02d}'];p=item.dxf
  check(p.standard_scale_type==code and p.unit_factor==0.03125,'Stored75/147 was conflated')
  check((p.plot_window_x1,p.plot_window_y1,p.plot_window_x2,p.plot_window_y2)==(-11.25,-22.5,33.75,44.125),'Window coordinates transposed')
  check((p.scale_numerator,p.scale_denominator)==(2.5,17.75),'Custom scale changed')
  check((p.left_margin,p.bottom_margin,p.right_margin,p.top_margin)==(1.25,2.5,3.75,4.125),'Margins changed')
  check((p.paper_width,p.paper_height,p.plot_origin_x_offset,p.plot_origin_y_offset)==(310.5,207.25,-7.75,8.125),'Paper/origin changed')
  check((p.plot_layout_flags,p.plot_paper_units,p.plot_rotation,p.plot_type,p.shade_plot_mode,p.shade_plot_resolution_level,p.shade_plot_custom_dpi)==(687,1,3,4,2,5,777),'Plot flags or enumerations changed')
  check((p.paper_image_origin_x,p.paper_image_origin_y)==(-0.625,1.875),'Paper image origin changed')
  check(decode_dxf_unicode(p.plot_configuration_file)==r'Printer\U+0041 Żółć' and decode_dxf_unicode(p.paper_size)=='Custom 東京' and decode_dxf_unicode(p.current_style_sheet)==r'C:\plot\style.ctb','Literal plot strings changed')
  check(item.dxf.owner==pages.dxf.handle and item.get_reactors()==[pages.dxf.handle],'Page ownership/reactor differs')
 p=doc.layouts.get('UserSheet').dxf
 check(p.standard_scale_type==25 and p.unit_factor==0.03125 and (p.plot_window_x1,p.plot_window_y1,p.plot_window_x2,p.plot_window_y2)==(-11.25,-22.5,33.75,44.125),'Embedded plot settings differ')
 wipe=doc.rootdict['ACAD_WIPEOUT_VARS'];check(wipe.dxf.frame==1 and wipe.dxf.owner==doc.rootdict.dxf.handle,'Authored wipeout variables differ')

def main():
 parser=argparse.ArgumentParser();parser.add_argument('artifacts',type=Path);args=parser.parse_args();manifest=json.loads((ROOT/'tests/fixtures/output-settings/manifest.json').read_text())
 expected={f'{prefix}AutoCad{year}-{binary}.dxf' for prefix in('output-settings-','independent-output-settings-') for year in VERSIONS for binary in('False','True')}
 actual={p.name for pattern in('output-settings-*.dxf','independent-output-settings-*.dxf') for p in args.artifacts.glob(pattern)};check(actual==expected,'Missing/extra output-setting files: '+str(expected^actual))
 check(manifest['producer']=='ezdxf 1.4.4','Unexpected fixture producer')
 fixtures=manifest['fixtures'];check(len(fixtures)==6 and {(f['file'],f['version']) for f in fixtures}=={(f'independent-output-settings-R{year}.dxf',version) for year,version in VERSIONS.items()},'Missing/extra fixture filenames or profiles')
 for fixture in manifest['fixtures']:
  source=ROOT/'tests/fixtures/output-settings'/fixture['file'];check(hashlib.sha256(source.read_bytes()).hexdigest()==fixture['sha256'],'Producer fixture hash differs')
 for year in VERSIONS:
  for binary in(False,True):
   external_path=args.artifacts/f'independent-output-settings-AutoCad{year}-{binary}.dxf';external(external_path,ROOT/f'tests/fixtures/output-settings/independent-output-settings-R{year}.dxf',year,binary);print('PASS',external_path.name)
   authored_path=args.artifacts/f'output-settings-AutoCad{year}-{binary}.dxf';authored(authored_path,year,binary);print('PASS',authored_path.name)
 print('PASS ezdxf '+ezdxf.__version__+':24 output-settings drawings;792 named page setups;24 embedded layouts;exact fields,profiles,references;zero audit changes')
if __name__=='__main__':main()
