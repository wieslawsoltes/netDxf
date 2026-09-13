#!/usr/bin/env python3
"""Verify every ordered tag of pinned R13/R14 fixtures with independent ezdxf readers.

Usage: python tools/verify_legacy_raw_profiles.py <conformance-artifact-directory>
No AutoCAD process or historical schema certification is implied.
"""
import argparse
import io
import math
from pathlib import Path
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("artifacts", type=Path)
args = parser.parse_args()
source = Path(__file__).resolve().parents[1] / "tests" / "fixtures" / "legacy"
fixtures = {"small_r13.dxf": "cp932", "small_r14.dxf": "cp1252",
            "bin_dxf_r13.dxf": "cp1252", "bin_dxf_r14.dxf": "cp1252"}


def read(path: Path, encoding: str):
    data = path.read_bytes()
    tags = binary_tags_loader(data, errors="strict") if data.startswith(b"AutoCAD Binary DXF") else \
        ascii_tags_loader(io.StringIO(data.decode(encoding), newline=None), skip_comments=False)
    result = []
    record = None
    for code, value in tags:
        if code == 0:
            record = value
        if isinstance(value, str):
            if code in range(310, 320) or code == 1004:
                value = bytes.fromhex(value)
            else:
                value = cast_tag_value(code, value)
        # The netDxf reader intentionally normalizes real handle spelling, not DIMBLK names.
        if isinstance(value, str) and (code in (5, 105, 1005) or 320 <= code <= 369 or
                                       390 <= code <= 399 or code in (480, 481)):
            if not (code == 5 and record == "DIMSTYLE"):
                value = format(int(value, 16), "X")
        result.append((code, value))
    return result


def compare(expected, actual, path):
    if len(expected) != len(actual):
        raise SystemExit(f"Tag-count mismatch in {path}: {len(expected)} != {len(actual)}")
    for index, (left, right) in enumerate(zip(expected, actual)):
        if left != right:
            raise SystemExit(f"Tag mismatch in {path} at {index}: {left!r} != {right!r}")
        if isinstance(left[1], float) and left[1] == 0 and math.copysign(1, left[1]) != math.copysign(1, right[1]):
            raise SystemExit(f"Signed-zero mismatch in {path} at {index}")


for name, encoding in fixtures.items():
    original = read(source / name, encoding)
    normalized = args.artifacts / ("legacy-normalized-" + name)
    compare(original, read(normalized, encoding), normalized)
    edited = list(original)
    start = next(i for i, tag in enumerate(edited) if tag == (0, "LAYER"))
    end = next(i for i in range(start + 1, len(edited)) if edited[i][0] == 0)
    for i in range(start + 1, end):
        if edited[i][0] == 62:
            edited[i] = (62, 3)
    output = args.artifacts / ("legacy-edited-" + name)
    compare(edited, read(output, encoding), output)
    print(f"PASS {name}: {len(original)} ordered tags; normalized output and isolated layer-color edit")
