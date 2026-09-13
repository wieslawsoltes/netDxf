#!/usr/bin/env python3
"""Independent tag-level verification of retained DIMBLK fixtures (ezdxf 1.4.4).

This does not validate block-reference resolution or claim whole-drawing AUDIT success.
"""
import argparse
import io
from pathlib import Path
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("fixtures", type=Path)
args = parser.parse_args()
files = sorted(args.fixtures.glob("raw-dimblk-*.dxf"))
if len(files) != 12:
    raise SystemExit(f"Expected 12 fixtures, found {len(files)}")
for path in files:
    data = path.read_bytes()
    if data.startswith(b"AutoCAD Binary DXF"):
        tags = list(binary_tags_loader(data, errors="strict"))
    else:
        encoding = "cp1252" if any(v in path.name for v in ("2000", "2004")) else "utf-8"
        tags = list(ascii_tags_loader(io.StringIO(data.decode(encoding), newline=None), skip_comments=False))
    record = None
    names, handles, ends = [], [], []
    for code, value in tags:
        if code == 0:
            record = value
        elif record == "DIMSTYLE" and code == 5:
            names.append(value)
        elif record == "DIMSTYLE" and code == 105:
            handles.append(value)
        elif record == "LINE" and code in (11, 21, 31):
            ends.append(float(value))
    if names != ["00aB"] or handles != ["11"] or ends != [4.0, 5.0, 6.0]:
        raise SystemExit(f"Unexpected tags in {path}: {names!r}, {handles!r}, {ends!r}")
    print(f"PASS {path.name}: exact DIMBLK spelling, group 105 identity and LINE coordinates")
