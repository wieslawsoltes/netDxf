#!/usr/bin/env python3
"""Produce independent ezdxf1.4.4 named/embedded plot settings and wipeout variables.

Autodesk PLOTSETTINGS corner labels are contradictory; ezdxf's explicit x1/y1/x2/y2
schema maps48/49/140/141. Its exporter omits declared shade_plot_handle333.
For2007+ this producer adds that optional reference at the documented subclass
boundary after initial serialization, targeting a real independent VISUALSTYLE.
No ezdxf reload/resave follows that augmentation.
"""
from pathlib import Path
import hashlib,json
import ezdxf
from ezdxf.lldxf.tagwriter import TagCollector
ROOT=Path(__file__).resolve().parent
YEARS=(2000,2004,2007,2010,2013,2018)
FIELDS={"plot_configuration_file":"Independent printer.pc3","paper_size":"ISO custom","plot_view_name":"",
 "left_margin":1.25,"bottom_margin":2.5,"right_margin":3.75,"top_margin":4.125,
 "paper_width":310.5,"paper_height":207.25,"plot_origin_x_offset":-7.75,"plot_origin_y_offset":8.125,
 "plot_window_x1":-11.25,"plot_window_y1":-22.5,"plot_window_x2":33.75,"plot_window_y2":44.125,
 "scale_numerator":2.5,"scale_denominator":17.75,"plot_layout_flags":687,"plot_paper_units":1,
 "plot_rotation":3,"plot_type":4,"current_style_sheet":"Independent.ctb","shade_plot_mode":2,
 "shade_plot_resolution_level":5,"shade_plot_custom_dpi":777,"unit_factor":0.03125,
 "paper_image_origin_x":-0.625,"paper_image_origin_y":1.875}

def build(year):
 d=ezdxf.new(f'R{year}');d.appids.add('QA_OUTPUT_SETTINGS');d.modelspace().add_line((1,2,3),(4,5,6));d.set_wipeout_variables(frame=int(year in (2004,2010,2018)))
 collection=d.rootdict['ACAD_PLOTSETTINGS'];plots=[]
 for code in range(33):
  name=f'Independent-{code:02d}';attrs=dict(FIELDS,page_setup_name=name,standard_scale_type=code,owner=collection.dxf.handle)
  plot=d.objects.add_dxf_object_with_reactor('PLOTSETTINGS',attrs);collection.add(name,plot)
  plot.set_xdata('QA_OUTPUT_SETTINGS',[(1000,f'page {code}'),(1005,collection.dxf.handle)])
  plots.append(plot)
 layout=d.layouts.new('IndependentSheet');layout.dxf.update(dict(FIELDS,page_setup_name='Embedded independent',standard_scale_type=25))
 layout.dxf_layout.set_xdata('QA_OUTPUT_SETTINGS',[(1000,'embedded layout')])
 style=None
 if year>=2007:
  styles=d.rootdict['ACAD_VISUALSTYLE'];style=d.objects.add_dxf_object_with_reactor('VISUALSTYLE',{'owner':styles.dxf.handle,'description':'Independent plot style','style_type':6,'face_lighting_model':0,'face_lighting_quality':0,'face_color_mode':1,'face_modifiers':0,'face_opacity_level':1.0,'face_specular_level':0.0,'edge_style_model':1,'internal_use_only_flag':0});styles.add('QA_PLOT_STYLE',style)
 return d,plots,layout,style

def append333(text, handles, target):
 lines=text.splitlines();records=[];current=[]
 for index in range(0,len(lines),2):
  code=int(lines[index]);value=lines[index+1]
  if code==0 and current:records.append(current);current=[]
  current.append((code,value))
 if current:records.append(current)
 for record in records:
  handle=next((value for code,value in record if code==5),None)
  if handle not in handles:continue
  start=next(i for i,(code,value) in enumerate(record) if code==100 and value=='AcDbPlotSettings')
  end=next((i for i in range(start+1,len(record)) if record[i][0] in(100,1001)),len(record))
  record.insert(end,(333,target))
 return ''.join(f'{code:3}\n{value}\n' for record in records for code,value in record)

def main():
 manifest={'producer':'ezdxf '+ezdxf.__version__,'fields':FIELDS,'fixtures':[],'limitations':['Autodesk48/49/140/141 descriptions contain duplicated corner labels; mapping follows independently explicit ezdxf schema.','ezdxf omits333 from PlotSettings.export_entity; producer inserts333 at the documented boundary for2007+ using a real VISUALSTYLE target.','No print-driver or native CAD rendering qualification.']}
 for year in YEARS:
  doc,plots,layout,style=build(year);path=ROOT/f'independent-output-settings-R{year}.dxf';doc.saveas(path)
  if style is not None:path.write_text(append333(path.read_text(encoding='utf-8'),{plots[7].dxf.handle,layout.dxf.handle},style.dxf.handle),encoding='utf-8')
  loaded=ezdxf.readfile(path);audit=loaded.audit();assert not audit.errors and not audit.fixes,(year,audit.errors,audit.fixes)
  manifest['fixtures'].append({'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'version':doc.dxfversion,'plot_handles':[p.dxf.handle for p in plots],'layout_handle':layout.dxf.handle,'shade_handle':style.dxf.handle if style else None,'frame':doc.objects.get_wipeout_frame_setting()})
  print('PASS',path.name,'33 named settings, embedded layout, zero audit changes')
 (ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
if __name__=='__main__':main()
