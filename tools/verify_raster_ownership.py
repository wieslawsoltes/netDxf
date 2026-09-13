#!/usr/bin/env python3
"""Independently audit retained raster-owner DXFs with the optional ezdxf package.

Example: python tools/verify_raster_ownership.py artifacts/conformance --output audit.json
Development-only dependency: pip install ezdxf==1.4.4 (the version used for this audit).
No image files are loaded and no drawing is modified or saved by this script.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        import ezdxf
    except ImportError:
        parser.error("Install the optional development package ezdxf to run this independent audit.")
    files = sorted(args.directory.glob("raster-owner-*.dxf"))
    if not files:
        parser.error("No raster-owner-*.dxf fixtures found; run the conformance tests first.")

    rows = []
    for path in files:
        try:
            doc = ezdxf.readfile(path)
            root = doc.rootdict
            raster = root["ACAD_IMAGE_VARS"]
            owner = raster.dxf.owner  # Check before audit can repair the in-memory graph.
            root_handle = root.dxf.handle
            auditor = doc.audit()
            errors = [{"code": int(e.code), "message": e.message} for e in auditor.errors]
            repairs = [{"code": int(e.code), "message": e.message} for e in auditor.fixes]
            rows.append({"file": path.name, "version": doc.dxfversion,
                         "owner_before_audit": owner, "root_handle": root_handle,
                         "errors": errors, "repairs": repairs,
                         "passed": owner == root_handle and not errors and not repairs})
        except Exception as exc:
            rows.append({"file": path.name, "passed": False,
                         "exception": f"{type(exc).__name__}: {exc}"})
    report = {"independent_reader": f"ezdxf {ezdxf.__version__}", "results": rows}
    text = json.dumps(report, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    else:
        print(text)
    return 0 if all(row["passed"] for row in rows) else 1


if __name__ == "__main__":
    raise SystemExit(main())
