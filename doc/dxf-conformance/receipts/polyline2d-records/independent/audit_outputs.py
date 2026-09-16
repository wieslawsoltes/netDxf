#!/usr/bin/env python3
"""Independent structural/selected physical and geometry checks on probe outputs."""
import hashlib
import io
import json
import re
import sys
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler

directory=Path(sys.argv[1]); baseline=len(sys.argv)>2 and sys.argv[2]=="before"
assert ezdxf.__version__=="1.4.4"
rows=[];failed=0
for file in sorted(directory.glob("*.dxf")):
    row={"file":file.name,"sha256":hashlib.sha256(file.read_bytes()).hexdigest()}
    try:
        data=file.read_bytes();binary=data.startswith(b"AutoCAD Binary DXF")
        loader=binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"),newline=None))
        records={};current=[]
        def flush():
            if current:
                identity=[t.value for t in current if t.code==5]
                if identity:records[identity[0]]=list(current)
        for tag in tag_compiler(loader):
            if tag.code==0:flush();current=[]
            current.append(tag)
        flush()
        doc=ezdxf.readfile(file);before_live={h for h,e in doc.entitydb.items() if e.is_alive};audit=doc.audit();after_live={h for h,e in doc.entitydb.items() if e.is_alive}
        assert not audit.errors and not audit.fixes and before_live==after_live,"Structural audit changed document"
        row.update(audit_errors=0,audit_fixes=0,audit_removed_entities=0)
        point=re.search(r"-omitted-point-(edit|transform)-(False|True)\.dxf$",file.name)
        if "-omitted-point-transform.dxf" in file.name:point_kind="transform"
        else:point_kind=point.group(1) if point else None
        if point_kind:
            expected=7 if point_kind=="edit" else 10
            actual=doc.entitydb["3D"].dxf.elevation.z
            row.update(expected_elevation=expected,actual_elevation=actual)
            if not baseline:assert abs(actual-expected)<1e-10,"Independent reader lost edited elevation"
            row["expected_before_mismatch"]=baseline and actual!=expected
        degenerate=re.search(r"-degenerate-([01])-(stored|transform|lifecycle)",file.name)
        if degenerate:
            poly=next(e for e in doc.modelspace().query("POLYLINE") if e.seqend.dxf.handle=="3E");count=int(degenerate.group(1));assert len(poly.vertices)==count,"Independent reader changed degenerate point count"
            assert poly.seqend is not None,"Independent reader lost SEQEND"
            assert poly.dxf.elevation.z==(0 if degenerate.group(2)=="stored" else 7),"Degenerate plane changed"
            row.update(degenerate_vertices=count,degenerate_elevation=poly.dxf.elevation.z)
        width=re.search(r"-width-([0-3])\.dxf$",file.name)
        if width:
            variant=int(width.group(1));packet=records["3F"]
            for code,bit in [(40,1),(41,2)]:
                values=[t.value for t in packet if t.code==code]
                assert values==([0.0] if variant&bit else []),"Physical explicit-zero versus omitted width changed"
            parent=doc.entitydb["3D"];v=parent.vertices[0]
            start=v.dxf.start_width if v.dxf.hasattr("start_width") else parent.dxf.default_start_width
            end=v.dxf.end_width if v.dxf.hasattr("end_width") else parent.dxf.default_end_width
            assert start==(0 if variant&1 else 2) and end==(0 if variant&2 else 3),"Independent width inheritance differs"
            row.update(width_variant=variant,effective_start=start,effective_end=end)
        row["passed"]=True
    except Exception as error:
        failed+=1;row.update(passed=False,error=str(error))
    rows.append(row)
assert rows,"No DXF outputs"
result={"reader":"ezdxf1.4.4","baseline":baseline,"files":len(rows),"failed":failed,"native_cad_execution":False,"outputs":rows}
(directory/"audit.json").write_text(json.dumps(result,indent=2)+"\n")
print(json.dumps({k:v for k,v in result.items() if k!="outputs"}))
sys.exit(bool(failed))
