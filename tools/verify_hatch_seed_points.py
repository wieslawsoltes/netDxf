#!/usr/bin/env python3
"""Independently read emitted HATCH seed fixtures using optional ezdxf 1.4.4.

Usage: python tools/verify_hatch_seed_points.py <conformance-artifact-directory>
No file is modified. This verifies stored points, not flood-fill evaluation.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import struct


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    import ezdxf
    expected = [(2.5, 3.75), (-1e-20, 5e-324), (2.5, 3.75)]
    for version in ("2000", "2004", "2007", "2010", "2013", "2018"):
        for binary in ("False", "True"):
            path = args.artifacts / f"hatch-seeds-AutoCad{version}-{binary}.dxf"
            document = ezdxf.readfile(path)
            hatches = list(document.modelspace().query("HATCH"))
            if len(hatches) != 1:
                raise ValueError(f"{path.name}: wrong HATCH count")
            hatch = hatches[0]
            actual = list(hatch.seeds)
            if len(actual) != len(expected):
                raise ValueError(f"{path.name}: wrong seed count")
            for a, b in zip(expected, actual):
                if struct.pack("<dd", *a) != struct.pack("<dd", *b):
                    raise ValueError(f"{path.name}: seed order or coordinate bits changed")
            if hatch.dxf.elevation.z != 2.5 or len(hatch.paths) != 1:
                raise ValueError(f"{path.name}: seeds changed boundary/elevation")
            if list(hatch.get_xdata("DOUBLE_TEST"))[0].value != "after pattern":
                raise ValueError(f"{path.name}: seeds changed XData")
            auditor = document.audit()
            if auditor.errors or auditor.fixes:
                raise ValueError(f"{path.name}: {len(auditor.errors)} errors / {len(auditor.fixes)} repairs")
            print(f"PASS {path.name}: 3 exact ordered seeds, unchanged boundary/metadata, zero audit repairs")
    print(f"PASS independent ezdxf {ezdxf.__version__}; no AutoCAD or flood-fill evaluation claim")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
