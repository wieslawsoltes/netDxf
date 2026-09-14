"""Verify incoming or netDxf-round-tripped UCS fixtures using ezdxf 1.4.4.

Accepts files and/or directories. Directories are searched recursively for DXF.
The verifier works with ASCII or binary and compares resolved references, not
source handle numbers. It checks data before auditing to avoid hiding repair.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

import ezdxf

APP = "UCS_QA"
PROFILES = {"AC1015", "AC1018", "AC1021", "AC1024", "AC1027", "AC1032"}


def equal(actual, expected, label):
    if isinstance(expected, tuple):
        actual = tuple(actual)
        assert len(actual) == len(expected), (label, actual, expected)
        assert actual == expected, (label, actual, expected)
    elif isinstance(expected, float):
        assert actual == expected, (label, actual, expected)
    else:
        assert actual == expected, (label, actual, expected)


def xdata(entity):
    equal([(tag.code, tag.value) for tag in entity.get_xdata(APP)],
          [(1000, "independent")], f"{entity.dxftype()} XData")


def association(doc, entity, expected_name, expected_ortho, named):
    attrs = entity.dxf
    for attr, expected in (
        ("ucs_origin", (5.125, -6.25, 7.5)),
        ("ucs_xaxis", (0.0, 2.0, 0.0)),
        ("ucs_yaxis", (-3.0, 0.0, 0.0)),
        ("elevation", -9.875), ("ucs_ortho_type", expected_ortho),
    ):
        equal(attrs.get(attr), expected, f"{attrs.name}.{attr}")
    handle_attr = "ucs_handle" if named else "base_ucs_handle"
    absent_attr = "base_ucs_handle" if named else "ucs_handle"
    handle = attrs.get(handle_attr)
    assert handle not in (None, "0"), (attrs.name, handle_attr, handle)
    target = doc.entitydb.get(handle)
    assert target is not None and target.dxftype() == "UCS", (attrs.name, handle)
    equal(target.dxf.name, expected_name, f"{attrs.name}.{handle_attr}")
    assert attrs.get(absent_attr) in (None, "0"), (attrs.name, absent_attr, attrs.get(absent_attr))
    xdata(entity)


def verify(path: Path):
    doc = ezdxf.readfile(path)
    profile = re.fullmatch(r"view-ucs-AutoCad(2000|2004|2007|2010|2013|2018)-(text|binary)\.dxf", path.name)
    assert profile is not None, ("Unexpected fixture name", path)
    versions = dict(zip(("2000", "2004", "2007", "2010", "2013", "2018"), ("AC1015", "AC1018", "AC1021", "AC1024", "AC1027", "AC1032")))
    assert doc.dxfversion == versions[profile[1]], (path, "DXF header/profile mismatch", doc.dxfversion)
    assert path.read_bytes().startswith(b"AutoCAD Binary DXF") == (profile[2] == "binary"), (path, "Transport/profile mismatch")
    named = doc.ucs.get("NamedFrame")
    base = doc.ucs.get("BaseFrame")
    for entity, expected_origin, expected_x, expected_y, flags in (
        (named, (10.125, -20.25, 30.5), (0.0, 1.0, 0.0), (-1.0, 0.0, 0.0), 64),
        (base, (-3.5, 4.25, 8.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), 0),
    ):
        for attr, expected in (("origin", expected_origin), ("xaxis", expected_x),
                               ("yaxis", expected_y), ("flags", flags)):
            equal(entity.dxf.get(attr), expected, f"{entity.dxf.name}.{attr}")
        xdata(entity)
    for name, target_name, ortho, is_named in (
        ("NamedReview", "NamedFrame", 0, True),
        ("UcsReview", "BaseFrame", 5, False),
    ):
        entity = doc.views.get(name)
        for attr, expected in (("direction", (2.0, -3.0, 7.0)),
                               ("target", (1.0, 2.0, 3.0)),
                               ("width", 44.5), ("height", 21.25), ("ucs", 1)):
            equal(entity.dxf.get(attr), expected, f"{name}.{attr}")
        association(doc, entity, target_name, ortho, is_named)
    for name, target_name, ortho, is_named in (
        ("*Active", "NamedFrame", 0, True),
        ("OrthoConfig", "BaseFrame", 4, False),
    ):
        configuration = doc.viewports.get(name)
        equal(len(configuration), 1, f"{name}.configuration count")
        entity = configuration[0]
        equal(entity.dxf.ucs_vp, 1, f"{name}.ucs_vp")
        association(doc, entity, target_name, ortho, is_named)
    lines = list(doc.modelspace().query("LINE"))
    equal(len(lines), 1, "line count")
    equal(lines[0].dxf.start, (1.0, 2.0, 3.0), "line start")
    equal(lines[0].dxf.end, (4.0, 5.0, 6.0), "line end")
    xdata(lines[0])
    audit = doc.audit()
    assert not audit.errors and not audit.fixes, (path, audit.errors, audit.fixes)
    data = path.read_bytes()
    return {"file": str(path), "acadver": doc.dxfversion,
            "transport": "binary" if data.startswith(b"AutoCAD Binary DXF") else "ASCII",
            "audit_errors": len(audit.errors), "audit_fixes": len(audit.fixes),
            "result": "PASS"}


if __name__ == "__main__":
    assert ezdxf.__version__ == "1.4.4", ezdxf.__version__
    assert len(sys.argv) == 2, "Supply the conformance artifact directory"
    directory = Path(sys.argv[1])
    expected = {f"view-ucs-AutoCad{year}-{transport}.dxf"
                for year in (2000, 2004, 2007, 2010, 2013, 2018)
                for transport in ("text", "binary")}
    paths = sorted(directory.glob("view-ucs-AutoCad*.dxf"))
    assert {path.name for path in paths} == expected, "Expected exactly all 12 VIEW UCS fixture profiles"
    for path in paths:
        print(json.dumps(verify(path)))
    print(json.dumps({"verified": len(paths), "ezdxf_version": ezdxf.__version__}))
