#!/usr/bin/env python3
"""Verify raw-preservation fixtures with the optional independent ezdxf reader.

Run the C# conformance program first, then:
    python tools/verify_raw_preservation.py artifacts/conformance --output report.json
Tested with ezdxf 1.4.4; development-only dependency, no CAD resources are loaded.
Unknown-record fixtures are compared at tag level, not asserted to be valid CAD graphs.
Known LINE fixtures additionally undergo ezdxf's document audit without writing repairs.
"""
from __future__ import annotations
import argparse
import io
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
        from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
        from ezdxf.lldxf.types import cast_tag_value, BINARY_DATA
    except ImportError:
        parser.error("Install optional development dependency ezdxf to run this verifier.")
    versions = ("2000", "2004", "2007", "2010", "2013", "2018")
    results = []
    for year in versions:
        for binary in (False, True):
            name = f"raw-preservation-AutoCad{year}-{binary}.dxf"
            try:
                expected = json.loads((args.directory / f"raw-preservation-AutoCad{year}.json").read_text())
                data = (args.directory / name).read_bytes()
                if binary:
                    tags = list(binary_tags_loader(data, errors="strict"))
                else:
                    encoding = "utf-8-sig" if int(year) >= 2007 else "cp1252"
                    tags = list(ascii_tags_loader(io.StringIO(data.decode(encoding), newline=None), skip_comments=False))
                if len(expected) != len(tags):
                    raise AssertionError(f"tag count: {len(expected)} != {len(tags)}")
                for index, (wanted, tag) in enumerate(zip(expected, tags)):
                    if wanted["code"] != tag.code:
                        raise AssertionError(f"group code mismatch at tag {index}")
                    value = tag.value
                    if not binary:
                        value = bytes.fromhex(value) if tag.code in BINARY_DATA else cast_tag_value(tag.code, value)
                    if wanted["kind"] == "Double":
                        value = struct.pack(">d", value).hex().upper()
                    elif isinstance(value, bytes):
                        value = value.hex().upper()
                    else:
                        value = str(value)
                    if value != wanted["value"]:
                        raise AssertionError(f"value mismatch at tag {index}, code {tag.code}: {value!r} != {wanted['value']!r}")
                results.append({"file": name, "passed": True, "scope": "ordered primitive tags", "tags": len(tags)})
            except Exception as exc:
                results.append({"file": name, "passed": False, "error": f"{type(exc).__name__}: {exc}"})
            name = f"raw-known-AutoCad{year}-{binary}.dxf"
            try:
                doc = ezdxf.readfile(args.directory / name)
                lines = list(doc.modelspace().query("LINE"))
                if len(lines) != 1:
                    raise AssertionError("expected exactly one LINE")
                line = lines[0]
                if struct.pack(">d", line.dxf.start.x) != struct.pack(">d", 1e-20):
                    raise AssertionError("untouched coordinate changed")
                if struct.pack(">d", line.dxf.end.x) != struct.pack(">d", 7.123456789012345):
                    raise AssertionError("edited coordinate changed")
                audit = doc.audit()
                if audit.errors or audit.fixes:
                    raise AssertionError(f"audit has {len(audit.errors)} errors and {len(audit.fixes)} repairs")
                results.append({"file": name, "passed": True, "scope": "LINE bits and document audit", "errors": 0, "repairs": 0})
            except Exception as exc:
                results.append({"file": name, "passed": False, "error": f"{type(exc).__name__}: {exc}"})
    report = {"reader": f"ezdxf {ezdxf.__version__}", "results": results}
    text = json.dumps(report, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    else:
        print(text)
    return 0 if all(row["passed"] for row in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
