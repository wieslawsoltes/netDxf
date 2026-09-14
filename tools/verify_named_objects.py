#!/usr/bin/env python3
"""Independently audit typed named-object graphs, arbitrary payloads and extensions."""
import argparse
import io
import re
from pathlib import Path
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler

def check(value, message):
    if not value: raise ValueError(message)

def raw_records(path):
    data = path.read_bytes()
    tags = tag_compiler(binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF")
                        else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig", errors="replace"), newline=None)))
    records = []
    for tag in tags:
        if tag.code == 0: records.append([])
        if records: records[-1].append(tag)
    result = {}
    for record in records:
        handle = None
        for tag in record[1:]:
            if tag.code == 100: break
            if tag.code == 5: handle = tag.value; break
        if handle is not None: result[handle] = record
    return result

def inspect(path):
    doc = ezdxf.readfile(path)
    profile = re.search(r"AutoCad(20\d\d)-(False|True)\.dxf$", path.name)
    versions = {"2000":"AC1015", "2004":"AC1018", "2007":"AC1021", "2010":"AC1024", "2013":"AC1027", "2018":"AC1032"}
    check(profile is not None and doc.dxfversion == versions[profile[1]], "DXF header version does not match fixture profile")
    with path.open("rb") as stream:
        check(stream.read(22).startswith(b"AutoCAD Binary DXF") == (profile[2] == "True"), "Transport does not match fixture profile")
    records = raw_records(path)
    root = doc.rootdict["APP_DATA"]
    check(set(root.keys()) == {"PAYLOAD","ALIAS","VAR","DEFAULT"}, "Application dictionary keys changed")
    check(root.dxf.hard_owned == 1 and root.dxf.cloning == 2, "Dictionary policy changed")
    check(root["PAYLOAD"] is root["ALIAS"], "Aliases no longer share a record")
    edges = records[root.dxf.handle]
    edge_codes = {t.value: edges[i+1].code for i,t in enumerate(edges[:-1]) if t.code==3}
    check(edge_codes == {"PAYLOAD":360,"ALIAS":350,"VAR":360,"DEFAULT":360}, "Per-entry hard/soft owner codes changed")
    for _, child in root.items(): check(child.dxf.owner == root.dxf.handle, "Application object owner changed")
    record = root["PAYLOAD"]
    check(record.dxf.cloning == 4, "XRECORD clone flag changed")
    raw = records[record.dxf.handle]
    start = next(i for i,t in enumerate(raw) if t.code==100 and t.value=="AcDbXrecord")
    check(raw[start+1].code==280 and raw[start+1].value==4, "XRECORD header280 changed")
    # The high-level ezdxf XRecord.tags view truncates at an application100 tag.
    # Low-level parsing keeps the full payload independently of that limitation.
    payload = [(t.code,decode_dxf_unicode(t.value) if isinstance(t.value,str) else t.value) for t in raw[start+2:]]
    expected = [(1,"Zażółć 測試"),(100,"PayloadSubclass"),(102,"{PayloadGroup"),(280,7),(102,"}"),
                (310,bytes([0,1,255])),(160,9223372036854775806),(40,1.25),(290,1),(320,"FFABC"),(330,root["VAR"].dxf.handle)]
    check(payload==expected,"Complete ordered XRECORD payload changed")
    check(record.get_reactors()==[root["VAR"].dxf.handle],"Persistent reactor target changed")
    check(root["VAR"].dxf.schema==0 and decode_dxf_unicode(root["VAR"].dxf.value)=="Mode ✓","Dictionary variable changed")
    defaults=root["DEFAULT"]
    check(defaults.dxftype()=="ACDBDICTIONARYWDFLT" and defaults["missing"] is defaults["Fallback"],"Default resolution changed")
    check(defaults["Fallback"].dxf.owner==defaults.dxf.handle,"Fallback owner changed")
    extension=record.get_extension_dict()
    check(extension.dictionary.dxf.owner==record.dxf.handle and extension["Nested"].dxf.value=="extension","Object extension changed")
    line=list(doc.modelspace().query("LINE"))[0]
    check(tuple(line.dxf.start)==(0,0,0) and tuple(line.dxf.end)==(1,0,0),"Attached LINE geometry changed")
    check(line.get_extension_dict()["OnLine"].dxftype()=="XRECORD","LINE extension missing")
    layer=doc.layers.get("ObjectMetadata")
    check(layer.get_extension_dict()["OnLayer"].dxf.value=="layer extension","Layer extension changed")
    audit=doc.audit()
    check(not audit.errors and not audit.fixes,f"{len(audit.errors)} errors/{len(audit.fixes)} repairs")

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory",type=Path)
    args=parser.parse_args()
    names={f"objects-AutoCad{year}-{binary}.dxf" for year in (2000,2004,2007,2010,2013,2018) for binary in (False,True)}
    paths=sorted(args.directory.glob("objects-AutoCad*.dxf"))
    check({p.name for p in paths}==names,"Expected all12 named-object fixtures")
    for path in paths:
        inspect(path)
        print("PASS "+path.name)
    print(f"PASS ezdxf {ezdxf.__version__}: 12 named-object fixtures; zero audit errors/repairs")

if __name__ == "__main__": main()
