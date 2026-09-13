#!/usr/bin/env python3
"""Check retained record-edit fixtures with the optional independent ezdxf package.

Run the .NET conformance suite, then:
  python tools/verify_raw_record_edits.py artifacts/conformance --output audit.json
The audit used ezdxf 1.4.4. This script neither saves drawings nor loads external resources.
"""
from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        import ezdxf
    except ImportError:
        parser.error("Install the optional development dependency ezdxf to run this audit.")
    paths = sorted(args.directory.glob("raw-record-edit-*.dxf"))
    if not paths:
        parser.error("No raw-record-edit-*.dxf fixtures were found.")

    def exact(actual: float, expected: float) -> bool:
        return struct.pack("<d", actual) == struct.pack("<d", expected)

    rows = []
    for path in paths:
        try:
            doc = ezdxf.readfile(path)
            lines = list(doc.modelspace().query("LINE"))
            circles = list(doc.modelspace().query("CIRCLE"))
            geometry = len(lines) == len(circles) == 1
            if geometry:
                line, circle = lines[0], circles[0]
                geometry = (all(exact(a, b) for a, b in zip(line.dxf.start, (1e-20, 2.0, 3.0)))
                            and all(exact(a, b) for a, b in zip(line.dxf.end, (7.123456789012345, 5.0, 6.0)))
                            and all(exact(a, b) for a, b in zip(circle.dxf.center, (7.0, 8.0, 9.0)))
                            and exact(circle.dxf.radius, 1.25))
            auditor = doc.audit()
            rows.append({"file": path.name, "version": doc.dxfversion,
                         "geometry_exact": geometry, "errors": len(auditor.errors),
                         "repairs": len(auditor.fixes),
                         "passed": geometry and not auditor.errors and not auditor.fixes})
        except Exception as error:
            rows.append({"file": path.name, "passed": False,
                         "exception": f"{type(error).__name__}: {error}"})
    report = {"independent_reader": f"ezdxf {ezdxf.__version__}", "results": rows}
    text = json.dumps(report, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text, encoding="utf-8")
    else:
        print(text, end="")
    return 0 if all(row["passed"] for row in rows) else 1


if __name__ == "__main__":
    raise SystemExit(main())
