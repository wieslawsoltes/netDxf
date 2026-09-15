#!/usr/bin/env python3
"""Inspect UCS-record79/346 storage independently of VIEW/VPORT references.

Ezdxf's UCS model omits these fields. Raw tags qualify the new relationship;
its typed reader supplies ancillary audits only. No native CAD or UCS evaluation.
"""
from pathlib import Path
import argparse, copy, hashlib, json, importlib.util
import ezdxf
from ezdxf.audit import AuditError
from verify_datatable import records, first, check
ROOT=Path(__file__).resolve().parents[1]
YEARS={2000:'AC1015',2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}

def relationship(tags, wire):
    start=next(i for i,tag in enumerate(tags) if tag==(100,'AcDbUCSTableRecord'))
    payload=tags[start+1:]
    if any(code==1001 for code,value in payload):payload=payload[:next(i for i,t in enumerate(payload) if t[0]==1001)]
    types=[value for code,value in payload if code==79];bases=[value for code,value in payload if code==346]
    check(len(types)==1 and 0<=types[0]<=6,'UCS known orthographic type grammar')
    check(len(bases)<=1 and (types[0]!=0 or not bases),'UCS conditional base grammar')
    if bases and int(bases[0],16)!=0:
        check(bases[0] in wire and wire[bases[0]][0]==(0,'UCS'),'UCS base must be a real physical UCS identity')
    return types[0],bases

def read(path,year,binary):
    data=path.read_bytes();check(data.startswith(b'AutoCAD Binary DXF')==binary,'UCS transport differs')
    doc=ezdxf.readfile(path);check(doc.dxfversion==YEARS[year],'UCS profile differs')
    wire=records(data);ucss={first(tags,2):tags for tags in wire.values() if tags[0]==(0,'UCS')}
    for tags in ucss.values():relationship(tags,wire)
    result=doc.audit();check(not result.errors,'Unexpected ancillary audit errors')
    expected=0 if year==2000 else 1
    check(len(result.fixes)==expected,'Unexpected ancillary audit repair count')
    for repair in result.fixes:
        check(repair.code==AuditError.INVALID_TRANSPARENCY and repair.entity.dxftype()=='LINE' and repair.entity.dxf.handle=='20' and first(wire['20'],440)==0,'Unexpected ancillary repair beyond pinned producer LINE20 transparency440=0')
    return doc,wire,ucss,len(result.fixes)

def verify_values(wire,ucss,null=False):
    check(set(ucss)==({'LEFT_FROM_SURVEY','SURVEY_BASE','BOTTOM_FROM_WORLD','NULL_COPY'} if null else {'LEFT_FROM_SURVEY','SURVEY_BASE','BOTTOM_FROM_WORLD'}),'UCS record inventory differs')
    child=ucss['LEFT_FROM_SURVEY'];parent=ucss['SURVEY_BASE'];world=ucss['BOTTOM_FROM_WORLD']
    check(first(child,5)=='E' and first(parent,5)=='F' and first(world,5)=='10','Independent producer identities changed')
    check(relationship(child,wire)==(5,['0'] if null else ['F']),'Stored base relation differs')
    check(relationship(parent,wire)==(0,[]) and relationship(world,wire)==(2,[]),'Normal or WORLD relation differs')
    check(first(child,71)==6 and tuple(first(child,13))==(7,8,9),'Distinct origin override changed')
    check(first(child,146)==-8.5 and tuple(first(child,10))==(100,200,300),'Child origin/elevation changed')
    if null:check(relationship(ucss['NULL_COPY'],wire)==(5,['0']),'Clone lost physical null base presence')

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args();count=0;ancillary_repairs=0
    fixture=ROOT/'tests/fixtures/ucs-record-base';manifest=json.loads((fixture/'manifest.json').read_text())
    check(len(manifest['files'])==12,'Independent producer fixture inventory differs')
    for entry in manifest['files']:
        data=(fixture/entry['file']).read_bytes();check(hashlib.sha256(data).hexdigest()==entry['sha256'],'Producer fixture hash differs')
        wire=records(data);ucss={first(tags,2):tags for tags in wire.values() if tags[0]==(0,'UCS')};verify_values(wire,ucss)
    spec=importlib.util.spec_from_file_location('ucs_carriers',fixture/'prepare_carriers.py');carrier_tool=importlib.util.module_from_spec(spec);spec.loader.exec_module(carrier_tool)
    carriers=json.loads((fixture/'carriers/manifest.json').read_text())['files'];check(len(carriers)==12,'Carrier count differs')
    for entry in carriers:
        data=(fixture/'carriers'/entry['file']).read_bytes();expected,removed,orphan_style=carrier_tool.prepare((fixture/entry['source']).read_bytes())
        check(data==expected and len(removed)==10 and orphan_style==entry['removed_orphan_style1071'] and hashlib.sha256(data).hexdigest()==entry['sha256'],'Carrier contains changes beyond the disclosed DIMSTYLE/STYLE default repairs')
    for year in YEARS:
        for binary in (False,True):
            for null in (False,True):
                doc,wire,ucss,repairs=read(args.artifacts/f'ucs-base-{"null-" if null else ""}AutoCad{year}-{binary}.dxf',year,binary);ancillary_repairs+=repairs
                verify_values(wire,ucss,null);check(len(list(doc.modelspace()))==1,'Following LINE inventory differs');line=next(iter(doc.modelspace()));check(tuple(line.dxf.start)==(1,2,3) and tuple(line.dxf.end)==(4,5,6),'Following LINE changed');count+=1
    controls=0
    normal=ucss['LEFT_FROM_SURVEY']
    for code,value in ((79,7),(79,0),(346,'ABCDEF')):
        corrupt=copy.deepcopy(normal);index=next(i for i,tag in enumerate(corrupt) if tag[0]==code);corrupt[index]=(code,value)
        try:relationship(corrupt,wire)
        except ValueError:controls+=1
        else:raise ValueError('UCS invalid relationship control accepted')
    check(count==24 and controls==3 and ancillary_repairs==20,'Mandatory output/control/ancillary observation count differs')
    print(json.dumps({'outputs':count,'independent_producer_inputs':12,'exact_ucs_carriers':12,'negative_controls':controls,'audit_errors':0,'ucs_audit_fixes':0,'ancillary_line_transparency_repairs':ancillary_repairs,'native_cad_execution':False,'coordinate_evaluation':False},sort_keys=True))
if __name__=='__main__':main()
