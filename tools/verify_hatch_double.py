#!/usr/bin/env python3
"""Read all retained HATCH double-flag fixtures with optional ezdxf (tested: 1.4.4).
Usage: python tools/verify_hatch_double.py artifacts/conformance
Development-only dependency; this verifier never saves a drawing or opens external resources.
"""
from __future__ import annotations
import argparse
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    import ezdxf
    versions = {"2000": "AC1015", "2004": "AC1018", "2007": "AC1021",
                "2010": "AC1024", "2013": "AC1027", "2018": "AC1032"}
    for year, expected_version in versions.items():
        for binary in ("False", "True"):
            path = args.artifacts / f"hatch-double-AutoCad{year}-{binary}.dxf"
            doc = ezdxf.readfile(path)
            if doc.dxfversion != expected_version:
                raise ValueError(f"{path.name}: incorrect version")
            hatches = list(doc.modelspace().query("HATCH"))
            if len(hatches) != 2:
                raise ValueError(f"{path.name}: expected original and cloned HATCH")
            for hatch in hatches:
                if hatch.dxf.pattern_double != 1 or hatch.dxf.pattern_type != 0 or hatch.dxf.solid_fill != 0:
                    raise ValueError(f"{path.name}: lost double flag or changed fill/type")
                if len(hatch.pattern.lines) != 1 or tuple(hatch.pattern.lines[0].offset) != (0.0, 0.125):
                    raise ValueError(f"{path.name}: pattern lines changed")
                if tuple(hatch.dxf.elevation) != (0.0, 0.0, 2.5) or len(hatch.paths) != 1:
                    raise ValueError(f"{path.name}: geometry changed")
                if list(hatch.get_xdata("DOUBLE_TEST")) != [(1000, "after pattern")]:
                    raise ValueError(f"{path.name}: XData changed")
            auditor = doc.audit()
            if auditor.errors or auditor.fixes:
                raise ValueError(f"{path.name}: {len(auditor.errors)} errors, {len(auditor.fixes)} repairs")
            print(f"PASS {path.name}: double flag, single authored line, geometry, XData; no audit repairs")
    print(f"PASS independent ezdxf {ezdxf.__version__}: 12 files; no AutoCAD execution claim")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
