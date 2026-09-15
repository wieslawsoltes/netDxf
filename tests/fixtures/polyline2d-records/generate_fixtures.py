#!/usr/bin/env python3
"""Unmodified ezdxf 1.4.4 ordinary legacy POLYLINE producer outputs."""
import hashlib, io, json
from pathlib import Path
import ezdxf
ROOT = Path(__file__).resolve().parent

def drawing(year):
    doc = ezdxf.new(f'R{year}')
    doc.appids.new('LEGACY2D_META')
    for name in ('VERTEX_ONLY', 'END_ONLY'): doc.layers.new(name)
    doc.linetypes.new('VERTEX_DASH', dxfattribs={'description':'legacy child resource','pattern':[0.75,0.5,-0.25]})
    rich = doc.modelspace().add_polyline2d([(1,2),(4,7),(8,12),(13,19)], dxfattribs={'flags':129,'default_start_width':0.75,'default_end_width':1.25,'elevation':(0,0,3),'extrusion':(0,0,1),'thickness':0.5})
    for i,v in enumerate(rich.vertices): v.dxf.vertex_identifier=17+i
    rich.vertices[0].dxf.start_width=0; rich.vertices[0].dxf.end_width=2.5; rich.vertices[0].dxf.bulge=0.5
    rich.vertices[1].dxf.start_width=1.5; rich.vertices[1].dxf.bulge=-0.25
    rich.vertices[2].dxf.end_width=0; rich.vertices[2].dxf.bulge=0
    first=rich.vertices[0]; first.dxf.layer='VERTEX_ONLY'; first.dxf.linetype='VERTEX_DASH'; first.dxf.color=0
    first.dxf.ltscale=1.75; first.dxf.lineweight=35; first.dxf.invisible=1
    if year>=2004: first.dxf.true_color=0x123456; first.dxf.color_name='legacy vertex'; first.dxf.transparency=0x020000FE
    first.set_xdata('LEGACY2D_META',[(1000,'vertex metadata'),(1004,bytes([0,1,127,255])),(1005,rich.vertices[1].dxf.handle)])
    first.set_reactors([rich.vertices[1].dxf.handle,rich.seqend.dxf.handle]); rich.vertices[1].set_reactors([first.dxf.handle])
    xr=rich.vertices[2].new_extension_dict().add_xrecord('VERTEX_DATA');xr.reset([(1,'owned vertex'),(330,first.dxf.handle),(310,bytes([5,0,255]))])
    end=rich.seqend;end.dxf.layer='END_ONLY';end.dxf.color=0;end.set_xdata('LEGACY2D_META',[(1000,'sequence metadata'),(1004,bytes([255,0,4]))])
    ex=end.new_extension_dict().add_xrecord('SEQEND_DATA');ex.reset([(1,'owned terminator'),(330,end.dxf.handle)])
    plain=doc.modelspace().add_polyline2d([(101,102),(104,107),(108,112),(113,119)], dxfattribs={'default_start_width':2.0,'default_end_width':3.0})
    plain.vertices[0].set_xdata('LEGACY2D_META',[(1000,'clone vertex'),(1004,bytes([1,2,255]))]);plain.vertices[0].dxf.layer='VERTEX_ONLY'
    doc.update_all();doc.classes.classes=dict(sorted(doc.classes.classes.items()))
    return doc, {'polyline':rich.dxf.handle,'vertices':[v.dxf.handle for v in rich.vertices],'seqend':end.dxf.handle,'vertex_xrecord':xr.dxf.handle,'seqend_xrecord':ex.dxf.handle,'block_record':doc.modelspace().block_record_handle,'plain_polyline':plain.dxf.handle,'plain_vertices':[v.dxf.handle for v in plain.vertices],'plain_seqend':plain.seqend.dxf.handle}

def main():
    assert ezdxf.__version__=='1.4.4'
    ezdxf.options.write_fixed_meta_data_for_testing=True
    fixtures=[]
    for year in (2000,2004,2007,2010,2013,2018):
        for binary in (False,True):
            doc,handles=drawing(year);stream=io.BytesIO() if binary else io.StringIO();doc.write(stream,fmt='bin' if binary else 'asc')
            data=stream.getvalue() if binary else stream.getvalue().encode(doc.output_encoding,errors='dxfreplace')
            path=ROOT/f"producer-R{year}-{'binary' if binary else 'ascii'}.dxf";path.write_bytes(data)
            audit=ezdxf.readfile(path).audit();assert not audit.errors and not audit.fixes
            fixtures.append({'file':path.name,'year':year,'binary':binary,'sha256':hashlib.sha256(data).hexdigest(),'handles':handles})
    (ROOT/'manifest.json').write_text(json.dumps({'producer':'ezdxf 1.4.4','source':'https://github.com/mozman/ezdxf/tree/v1.4.4','unchanged_producer_bytes':True,'native_application_execution':False,'owner_form':'VERTEX -> actual containing BLOCK_RECORD; SEQEND -> POLYLINE','fixtures':fixtures},indent=2)+'\n')
if __name__=='__main__':main()
