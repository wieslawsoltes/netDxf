#!/usr/bin/env python3
"""Verify pinned R12 files and normalized/edited outputs with ezdxf 1.4.4.

Usage: python tools/verify_r12_raw_profiles.py <conformance-artifact-directory>
The optional ezdxf dependency is development-only. No file is modified.
"""
from __future__ import annotations
import argparse
import io
from pathlib import Path
import struct


def read(path: Path) -> list[tuple[int, object]]:
    from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
    from ezdxf.lldxf.types import cast_tag_value
    data = path.read_bytes()
    tags = binary_tags_loader(data, errors="strict") if data.startswith(b"AutoCAD Binary DXF") else \
        ascii_tags_loader(io.StringIO(data.decode("cp1252"), newline=None), skip_comments=False)
    result = []
    record = ""
    for code, value in tags:
        if code == 0:
            record = value
        if isinstance(value, str):
            value = bytes.fromhex(value) if code == 1004 or 310 <= code <= 319 else cast_tag_value(code, value)
        if isinstance(value, str) and (code in (5, 105, 1005) or 320 <= code <= 369 or
                                      390 <= code <= 399 or code in (480, 481)):
            if not (code == 5 and record == "DIMSTYLE"):
                value = format(int(value, 16), "X")
        if isinstance(value, float):
            value = struct.pack("<d", value)
        result.append((code, value))
    return result


def check(expected: list, actual: list, label: str) -> None:
    if expected != actual:
        mismatch = next((i for i, pair in enumerate(zip(expected, actual)) if pair[0] != pair[1]),
                        min(len(expected), len(actual)))
        raise ValueError(f"{label}: ordered tag mismatch at {mismatch}, counts {len(expected)}/{len(actual)}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    import ezdxf
    source = Path(__file__).resolve().parents[1] / "tests" / "fixtures" / "legacy"
    for name in ("ASCII_R12.dxf", "bin_dxf_r12.dxf"):
        expected = read(source / name)
        for binary in ("False", "True"):
            path = args.artifacts / f"r12-normalized-{binary}-{name}"
            check(expected, read(path), str(path))
        edited = expected.copy()
        start = next(i for i, tag in enumerate(edited) if tag == (0, "LINE"))
        end = next(i for i in range(start + 1, len(edited)) if edited[i][0] == 0)
        for i in range(start, end):
            if edited[i][0] == 10:
                edited[i] = (10, struct.pack("<d", 12.345678901234567))
        path = args.artifacts / ("r12-edited-" + name)
        check(edited, read(path), str(path))
        print(f"PASS {name}: {len(expected)} exact ordered values; both transports and scoped edit")
    for binary in ("False", "True"):
        document = ezdxf.readfile(args.artifacts / f"r12-xdata-{binary}.dxf")
        line = document.modelspace().query("LINE").first
        if line is None or struct.pack("<d", line.dxf.start.x) != struct.pack("<d", 1e-20):
            raise ValueError("Authored R12 LINE coordinate changed")
        data = list(line.get_xdata("R12_TEST"))
        check([(1000, "AC1032 $ACADVER SECTION EOF"), (1002, "{"), (1004, b"\x00\xff\x01\xff\x00"),
               (1005, "FFFFFFFFFFFFFFFF"), (1040, 5e-324), (1070, -32768), (1071, -2147483648), (1002, "}")],
              [(t.code, t.value) for t in data], f"Authored R12 XData {binary}")
    print(f"PASS independent ezdxf {ezdxf.__version__}; no AutoCAD or whole-graph AUDIT claim")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
