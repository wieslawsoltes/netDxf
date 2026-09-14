#!/usr/bin/env python3
"""Audit VPORT physical records, duplicate names and camera/UCS state with ezdxf."""
import argparse
import struct
import re
from pathlib import Path
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode

def check(value, message):
    if not value: raise ValueError(message)

def inspect(path):
    doc = ezdxf.readfile(path)
    profile = re.search(r"AutoCad(20\d\d)-(text|binary)\.dxf$", path.name)
    versions = {"2000":"AC1015", "2004":"AC1018", "2007":"AC1021", "2010":"AC1024", "2013":"AC1027", "2018":"AC1032"}
    check(profile is not None and doc.dxfversion == versions[profile[1]], "DXF header version does not match fixture profile")
    with path.open("rb") as stream:
        check(stream.read(22).startswith(b"AutoCAD Binary DXF") == (profile[2] == "binary"), "Transport does not match fixture profile")
    records = list(doc.viewports)
    check(len(records) == 4, "Physical VPORT records collapsed")
    check([r.dxf.handle for r in records] == ["100","101","102","103"], "VPORT handle/order changed")
    check([decode_dxf_unicode(r.dxf.name) for r in records] == ["*ACTIVE","*aCtIvE","Plan Żółć","Plan Żółć"], "Repeated or Unicode names changed")
    scalars = {"aspect_ratio":1.75,"focal_length":85.5,"front_clipping":-2,"back_clipping":500,
               "snap_rotation":13.75,"view_twist":-42.5,"flags":64,"view_mode":31,"circle_sides":1234,
               "fast_zoom":0,"ucs_icon":2,"snap_on":1,"grid_on":0,"snap_style":1,"snap_isopair":2,
               "render_mode":6,"ucs_vp":1,"ucs_ortho_type":3,"elevation":5.25}
    vectors = {"snap_base":(.125,-.25,0),"snap_spacing":(1.25,2.5,0),"grid_spacing":(5,7.5,0),
               "target":(-5,6,-7),"ucs_origin":(1,2,3),"ucs_xaxis":(0,1,0),"ucs_yaxis":(-1,0,0)}
    for i, record in enumerate(records):
        check(record.dxf.owner == "A", "VPORT table ownership changed")
        for name, expected in scalars.items(): check(record.dxf.get(name) == expected, "VPORT scalar changed: " + name)
        for name, expected in vectors.items(): check(tuple(record.dxf.get(name)) == expected, "VPORT vector changed: " + name)
        check(struct.pack("<d",record.dxf.height) == struct.pack("<d",12.500000000000002+i), "View height lost precision")
        check(tuple(record.dxf.center) == (7.25+i,-3.5-i,0), "View center changed")
        check(tuple(record.dxf.direction) == (2+i,3,4), "View direction magnitude changed")
        check(tuple(record.dxf.lower_left) == (i%2*.5,0,0) and tuple(record.dxf.upper_right) == ((i%2+1)*.5,1,0), "Tile rectangle changed")
        check([(t.code,t.value) for t in record.get_xdata("VPORT_TEST")] == [(1000,f"tile {i}"),(1004,bytes([i,0,255]))], "Per-record XData changed")
    check([(t.code,t.value) for t in doc.viewports.head.get_xdata("VPORT_TEST")] == [(1000,"table metadata")], "Table metadata changed")
    lines = list(doc.modelspace().query("LINE"))
    check(len(lines)==1 and tuple(lines[0].dxf.start)==(20,30,40) and tuple(lines[0].dxf.end)==(50,60,70), "Following LINE changed")
    audit=doc.audit()
    check(not audit.errors and not audit.fixes,f"{len(audit.errors)} errors/{len(audit.fixes)} repairs")

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory",type=Path)
    args=parser.parse_args()
    names={f"vport-AutoCad{year}-{kind}.dxf" for year in (2000,2004,2007,2010,2013,2018) for kind in ("text","binary")}
    paths=sorted(args.directory.glob("vport-AutoCad*.dxf"))
    check({p.name for p in paths}==names,"Expected all 12 VPORT fixtures")
    for path in paths:
        inspect(path)
        print("PASS "+path.name)
    print(f"PASS ezdxf {ezdxf.__version__}: 12 VPORT fixtures; zero audit errors/repairs")

if __name__ == "__main__": main()
