#!/usr/bin/env python3
"""Audit all typed MTEXT column exports with the independent optional ezdxf reader."""
import argparse
import io
import re
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

def check(value, message):
    if not value: raise ValueError(message)

def common_owners(path):
    data = path.read_bytes()
    tags = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None))
    result = {}; handle = None; common = False
    for tag in tags:
        if tag.code == 0: handle = None; common = tag.value == "MTEXT"
        elif common and tag.code == 100: common = False
        elif common and tag.code == 5: handle = tag.value
        elif common and tag.code == 330 and handle: result[handle] = tag.value
    return result

def inspect(path):
    mode = int(path.stem.rsplit("-", 1)[1])
    doc = ezdxf.readfile(path)
    profile = re.search(r"AutoCad(20\d\d)-(False|True)-[012]\.dxf$", path.name)
    versions = {"2000":"AC1015", "2004":"AC1018", "2007":"AC1021", "2010":"AC1024", "2013":"AC1027", "2018":"AC1032"}
    check(profile is not None and doc.dxfversion == versions[profile[1]], "DXF header version does not match fixture profile")
    with path.open("rb") as stream:
        check(stream.read(22).startswith(b"AutoCAD Binary DXF") == (profile[2] == "True"), "Transport does not match fixture profile")
    texts = list(doc.modelspace().query("MTEXT"))
    check(len(texts) == 1, "Expected one primary MTEXT with resolved subordinate columns")
    text = texts[0]
    c = text.columns
    check(c is not None, "Typed column metadata missing")
    check(int(c.column_type) == (1 if mode == 0 else 2), "Column type changed")
    check(c.count == 3 and c.auto_height == (mode == 1), "Column count/automatic height changed")
    check(c.reversed_column_flow, "Reversed-flow flag lost")
    check(c.width == 12.5 and c.gutter_width == 1.75 and c.total_width == 41, "Column dimensions changed")
    check(c.defined_height == (0 if mode == 2 else 20.25), "Defined height changed")
    check(c.total_height == (28.75 if mode == 2 else 20.25), "Total height changed")
    check(c.heights == ([20.25, 28.75, 0] if mode == 2 else []), "Ordered column heights changed")
    check(tuple(text.dxf.insert) == (17, -11, 3) and text.dxf.char_height == 2.5, "Primary text placement/height changed")
    if doc.dxfversion < "AC1032":
        linked = c.linked_columns
        check(text.text == "FIRST" and [e.text for e in linked] == ["SECOND", "THIRD"], "Linked text order changed")
        check(len({text.dxf.handle, *(e.dxf.handle for e in linked)}) == 3, "Column handles are not distinct")
        # ezdxf detaches linked MTEXT from modelspace and clears owner in memory;
        # qualify the original wire owners before that normalization instead.
        owners = common_owners(path)
        check(all(owners[e.dxf.handle] == owners[text.dxf.handle] for e in linked), "Column block ownership changed")
        check([tuple(e.dxf.insert) for e in linked] == [(2.75, -11, 3), (-11.5, -11, 3)], "Reversed linked positions changed")
    else:
        check(text.text == "FIRSTSECONDTHIRD" and not c.linked_columns, "Embedded content/link model changed")
    audit = doc.audit()
    check(not audit.errors and not audit.fixes, f"{len(audit.errors)} errors/{len(audit.fixes)} repairs")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    names = {f"mtext-columns-AutoCad{year}-{binary}-{mode}.dxf" for year in (2000,2004,2007,2010,2013,2018)
             for binary in (False,True) for mode in (0,1,2)}
    paths = sorted(args.directory.glob("mtext-columns-AutoCad*.dxf"))
    check({p.name for p in paths} == names, "Expected all36 MTEXT column fixtures")
    for path in paths:
        inspect(path)
        print("PASS " + path.name)
    print(f"PASS ezdxf {ezdxf.__version__}: 36 column fixtures; zero audit errors/repairs")

if __name__ == "__main__": main()
