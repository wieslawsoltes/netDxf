#!/usr/bin/env python3
"""Independently check HATCH XData preservation with optional ezdxf 1.4.4.

Usage: python tools/verify_hatch_xdata_preservation.py <conformance-artifacts>
No files are modified. This is a fixture-specific audit, not native AutoCAD execution.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import struct


def normalize(tags: object) -> list[tuple[int, object]]:
    result = []
    for tag in tags:
        value = tag.value
        if tag.code == 1010:
            value = tuple(struct.pack("<d", component) for component in value)
        result.append((tag.code, value))
    return result


def point(x: float, y: float) -> tuple[int, object]:
    return 1010, tuple(struct.pack("<d", component) for component in (x, y, 0.0))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    import ezdxf

    expected_acad = [point(21.0, 34.0), (1000, "NESTED"), (1002, "{"),
                     point(99.0, 88.0), (1002, "}"), (1000, "unrelated tail"),
                     (1070, 17), (1004, b"\x00\xff\x80\x7f")]
    checked = 0
    for year in (2000, 2004, 2007, 2010, 2013, 2018):
        for binary in ("False", "True"):
            path = args.artifacts / f"hatch-xdata-AutoCad{year}-{binary}.dxf"
            document = ezdxf.readfile(path)
            hatches = list(document.modelspace().query("HATCH"))
            if len(hatches) != 1:
                raise ValueError(f"{path.name}: expected one HATCH")
            hatch = hatches[0]
            if normalize(hatch.get_xdata("ACAD")) != expected_acad:
                raise ValueError(f"{path.name}: origin/nested/opaque ACAD data changed")
            for app, index in (("GradientColor1ACI", 1), ("GradientColor2ACI", 5)):
                if normalize(hatch.get_xdata(app)) != [(1070, index), (1000, "gradient tail")]:
                    raise ValueError(f"{path.name}: managed gradient value or opaque suffix changed")
            if normalize(hatch.get_xdata("DOUBLE_TEST")) != [(1000, "after pattern")]:
                raise ValueError(f"{path.name}: unrelated APPID changed")
            if hatch.dxf.elevation.z != 2.5 or len(hatch.paths) != 1 or len(hatch.seeds) != 3:
                raise ValueError(f"{path.name}: independent HATCH geometry metadata changed")
            # Check values first: a repairing audit must not conceal the bug.
            audit = document.audit()
            if audit.errors or audit.fixes:
                raise ValueError(f"{path.name}: {len(audit.errors)} errors / {len(audit.fixes)} repairs")
            checked += 1
            print(f"PASS {path.name}: exact ordered XData and zero audit errors/repairs")
    print(f"PASS independent ezdxf {ezdxf.__version__}: {checked} fixtures")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
