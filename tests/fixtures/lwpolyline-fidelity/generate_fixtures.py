#!/usr/bin/env python3
"""Produce independent LWPOLYLINE packets with exact optional field presence.

ezdxf authors the document, then this producer replaces only the AcDbPolyline
packet because ezdxf's model does not retain per-component width presence or91.
The original document and the patched file are independently loaded/audited.

Wire fields: Autodesk GUID-748FC305-F3F2-4F74-825A-61F04D757A50.
Effective width policy: ezdxf1.4.4 render/trace.py TraceBuilder.from_polyline.
"""
from pathlib import Path
import hashlib
import io
import json
import ezdxf
from ezdxf.render import TraceBuilder

ROOT = Path(__file__).resolve().parent
VERTICES = [
    {"x": 0.0, "y": 0.0, "start": None, "end": -0.0, "bulge": 0.5, "id": 0},
    {"x": 6.0, "y": 0.0, "start": 2.0, "end": None, "bulge": 0.0, "id": -17},
    {"x": 7.0, "y": 5.0, "start": 0.0, "end": 3.0, "bulge": -0.25, "id": 2147483647},
    {"x": -2.0, "y": 4.0, "start": None, "end": None, "bulge": 0.0, "id": 42},
]


def build(year, mode):
    doc = ezdxf.new(f"R{year}")
    doc.appids.add("QA_LW_FIDELITY")
    line = doc.modelspace().add_lwpolyline([(v["x"], v["y"]) for v in VERTICES], close=True)
    line.set_xdata("QA_LW_FIDELITY", [(1000, mode), (1004, bytes((0, 1, 255)))])
    doc.modelspace().add_line((20, 30, 40), (50, 60, 70))
    stream = io.StringIO()
    doc.write(stream)
    lines = stream.getvalue().splitlines()
    tags = [(int(lines[i]), lines[i + 1]) for i in range(0, len(lines), 2)]
    records = []
    for tag in tags:
        if tag[0] == 0 or not records:
            records.append([])
        records[-1].append(tag)
    const = {"absent": None, "zero": 0.0, "positive": 3.25}[mode]
    vertices = [dict(v, id=v["id"] if year == 2018 else None) for v in VERTICES]
    for record in records:
        if record[0] != (0, "LWPOLYLINE"):
            continue
        start = record.index((100, "AcDbPolyline"))
        suffix = next((i for i, tag in enumerate(record) if tag[0] == 1001), len(record))
        body = [(100, "AcDbPolyline"), (90, 4), (70, 1)]
        if const is not None:
            body.append((43, const))
        body.extend(((38, 2.75), (39, 0.5)))
        for vertex in vertices:
            body.extend(((10, vertex["x"]), (20, vertex["y"])))
            if vertex["start"] is not None:
                body.append((40, vertex["start"]))
            if vertex["end"] is not None:
                body.append((41, vertex["end"]))
            body.append((42, vertex["bulge"]))
            if vertex["id"] is not None:
                body.append((91, vertex["id"]))
        record[:] = record[:start] + body + record[suffix:]
    text = "".join(f"{code:3d}\n{value}\n" for record in records for code, value in record)
    return text, {"version": doc.dxfversion, "polyline": line.dxf.handle,
                  "constant_width": const, "vertices": vertices, "closed": True,
                  "elevation": 2.75, "thickness": 0.5}


def main():
    manifest = {"producer": "ezdxf " + ezdxf.__version__ + " with explicit field-presence patch",
                "fixtures": []}
    for year in (2000, 2018):
        for mode in ("absent", "zero", "positive"):
            text, semantics = build(year, mode)
            path = ROOT / f"independent-lw-fidelity-R{year}-{mode}.dxf"
            path.write_text(text)
            doc = ezdxf.readfile(path)
            audit = doc.audit()
            assert not audit.errors and not audit.fixes, (audit.errors, audit.fixes)
            polyline = doc.modelspace().query("LWPOLYLINE")[0]
            assert len(polyline) == 4 and polyline.closed
            geometry = TraceBuilder.from_polyline(polyline)
            assert list(geometry.virtual_entities())
            manifest["fixtures"].append({"file": path.name,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), **semantics})
            print("PASS", path.name, "field-presence patch; zero audit errors/repairs")
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
