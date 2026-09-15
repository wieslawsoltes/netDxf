#!/usr/bin/env python3
"""Independently inspect stored SUN packets and reciprocal owner slots.

The raw tag grammar is authoritative here: ezdxf 1.4.4's SUN shadow validator
admits only 0/1, while primary AcGi documentation and native packets admit 2.
No native CAD execution, sun-position calculation or rendering is claimed.
"""
from pathlib import Path
import argparse, copy, gzip, hashlib, json, math
import ezdxf
from verify_datatable import records, canonical, first, check, audit
ROOT = Path(__file__).resolve().parents[1]
YEARS = {2007:'AC1021', 2010:'AC1024', 2013:'AC1027', 2018:'AC1032'}

def packet(tags):
    start = next(i for i, tag in enumerate(tags) if tag == (100, 'AcDbSun'))
    payload = tags[start + 1:]
    if any(code == 1001 for code,value in payload): payload = payload[:next(i for i,t in enumerate(payload) if t[0]==1001)]
    codes = [90,290,63] + ([421] if any(c==421 for c,v in payload) else []) + [40,291,91,92,292,70,71,280]
    check([code for code,value in payload] == codes, 'SUN ordered public schema differs')
    values = dict(payload)
    check(values[90] == 1, 'SUN version differs')
    check(all(values[c] in (0,1) for c in (290,291,292)), 'SUN boolean range')
    check(0 <= values[63] <= 256 and 0 <= values.get(421,0) <= 0xffffff, 'SUN color range')
    check(math.isfinite(values[40]), 'SUN finite intensity')
    check(values[70] in (0,1,2), 'SUN shadow enum range')
    check(values[71] in (64,128,256,512,1024,2048,4096), 'SUN shadow-map range')
    check(0 <= values[280] <= 255, 'SUN unsigned softness')
    return values

def read(path, year, binary):
    data=path.read_bytes();check(data.startswith(b'AutoCAD Binary DXF')==binary,'SUN transport differs')
    doc=ezdxf.readfile(path);check(doc.dxfversion==YEARS[year],'SUN profile differs')
    wire=records(data);suns=[r for r in wire.values() if r[0]==(0,'SUN')]
    check(len(suns)==1,'SUN physical inventory differs')
    sun=suns[0];values=packet(sun);owner=first(sun,330);handle=first(sun,5)
    check(owner in wire and first(wire[owner],361)==handle,'SUN reciprocal owner missing')
    check(wire[owner][0][1] in ('VPORT','VIEW','VIEWPORT'),'SUN owner type differs')
    definition=doc.classes.get('SUN').dxf
    check((definition.cpp_class_name,definition.app_name,definition.flags,definition.instance_count,definition.is_an_entity)==('AcDbSun','SCENEOE',1153,1,0),'SUN CLASS metadata/count differs')
    audit(doc)
    return wire,sun,values

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args();count=0
    expected={90:1,290:1,63:3,421:0x123456,40:-2.125,291:0,91:2455826,92:54000000,292:1,70:2,71:4096,280:255}
    for year in YEARS:
        for binary in (False,True):
            for kind in (('VPORT','VIEWPORT') if year==2007 else ('VPORT','VIEW','VIEWPORT')):
                wire,sun,values=read(args.artifacts/f'sun-{kind}-AutoCad{year}-{binary}.dxf',year,binary)
                check(values==expected,'Exact authored SUN stored values differ')
                check(wire[first(sun,330)][0][1]==kind,'SUN owner family differs');count+=1
    inventory=json.loads((ROOT/'tools/table_oracle/fixtures.json').read_text())
    for file in ('acad_table_simple.dxf','acad_table_with_blk_ref.dxf'):
        data=gzip.decompress((ROOT/'tests/fixtures/table-oracle'/(file+'.gz')).read_bytes())
        pinned=next(item['sha256'] for item in inventory['files'] if item['file']==file)
        check(hashlib.sha256(data).hexdigest()==pinned,'Native SUN source hash differs')
        source=records(data,'cp1252');native=source['23'];check(first(native,330)=='22' and first(source['22'],361)=='23','Pinned native ownership differs')
        for binary in (False,True):
            wire,sun,values=read(args.artifacts/f'sun-native-{Path(file).stem}-{binary}.dxf',2007,binary)
            relocated=[(code,first(sun,5) if code==5 else first(sun,330) if code==330 else value) for code,value in native]
            check(canonical(sun)==canonical(relocated),'Exact native SUN packet differs beyond disclosed identity and external owner relocation')
            check(values[70]==2 and values[92]==54000000 and values[421]==0xffffff,'Native qualified values differ');count+=1
    controls=0
    for code,value in ((290,2),(71,100),(70,3),(280,256)):
        corrupt=copy.deepcopy(sun);slot=next(i for i,t in enumerate(corrupt) if t[0]==code);corrupt[slot]=(code,value)
        try:packet(corrupt)
        except ValueError:controls+=1
        else:raise ValueError('SUN malformed negative control accepted')
    check(count==26 and controls==4,'Mandatory SUN output/control inventory differs')
    print(json.dumps({'outputs':count,'native_packets':4,'native_source_files':2,'negative_controls':controls,'audit_errors':0,'audit_fixes':0,'native_cad_execution':False,'solar_or_render_evaluation':False},sort_keys=True))
if __name__=='__main__':main()
