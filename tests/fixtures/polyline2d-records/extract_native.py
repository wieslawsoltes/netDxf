#!/usr/bin/env python3
"""Two genuine R2000 legacy chains in a declared neutral carrier, not native CAD execution."""
import gzip, hashlib, importlib.util, io, json
from pathlib import Path
import ezdxf
from ezdxf.lldxf.types import DXFTag
ROOT=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('native_extractor',ROOT.parent/'dimassoc'/'extract_fixtures.py')
native=importlib.util.module_from_spec(spec);spec.loader.exec_module(native)
SOURCE_SHA='e74f03a383593ef96d5a349618390a393e8c5697e253014d4218fd40e3f37250'

def main():
    assert ezdxf.__version__=='1.4.4'
    original=gzip.decompress((ROOT/'native-Polyline2D-R2000.dxf.gz').read_bytes());assert hashlib.sha256(original).hexdigest()==SOURCE_SHA
    packets=native.packets(original);selected=[]
    for parent in ('1EF','1FF'):
        start=next(i for i,p in enumerate(packets) if native.identity(p)==parent)
        for packet in packets[start:]:
            selected.append(packet)
            if packet[0].value=='SEQEND':break
    assert len(selected)==8
    ezdxf.options.write_fixed_meta_data_for_testing=True
    doc=ezdxf.new('R2000');placeholder=doc.modelspace().add_line((0,0,0),(1,0,0)).dxf.handle;owner=doc.modelspace().block_record_handle
    mapped=[[DXFTag(330,owner) if packet[0].value=='POLYLINE' and tag.code==330 else tag for tag in packet] for packet in selected]
    stream=io.StringIO();doc.write(stream);carrier=[]
    for packet in native.packets(stream.getvalue().encode('utf8')):
        if native.identity(packet)==placeholder:carrier.extend(mapped);continue
        for i,tag in enumerate(packet[:-1]):
            if tag==DXFTag(9,'$HANDSEED'):packet[i+1]=DXFTag(5,'5000')
        carrier.append(packet)
    data=native.write(carrier,doc.dxfversion);path=ROOT/'native-R2000.dxf';path.write_bytes(data)
    actual={native.identity(p):p for p in native.packets(data) if native.identity(p)}
    for packet in mapped:assert native.exact(packet)==native.exact(actual[native.identity(packet)])
    audit=ezdxf.readfile(path).audit();assert not audit.errors and not audit.fixes
    source={'repository':'LibreDWG/libredwg','commit':'34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43','path':'test/test-data/2000/PolyLine2D.dxf','git_blob':'514f945a48e8a9854797b317764ce5781a9e20c7','source_sha256':SOURCE_SHA,'gzip_file':'native-Polyline2D-R2000.dxf.gz'}
    manifest={'contract':'Eight complete native packets; only the two ordinary parent owners map from native BLOCK_RECORDs into the declared carrier. Common tables and surrounding drawing are synthetic. Two R2000 chains are separate from six-profile producer evidence.','native_application_executed':False,'source':source,'file':path.name,'sha256':hashlib.sha256(data).hexdigest(),'carrier_owner':owner,'chains':[{'parent':'1EF','vertices':['1F0','1F1'],'seqend':'1F2','native_owner':'1EC','default_start_width':0.15,'default_end_width':0.15,'closed':False},{'parent':'1FF','vertices':['201','202'],'seqend':'203','native_owner':'1FC','default_start_width':0.5,'default_end_width':0.5,'closed':True}]}
    (ROOT/'native-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
if __name__=='__main__':main()
