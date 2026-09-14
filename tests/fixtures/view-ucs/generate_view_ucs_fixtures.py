"""Generate independent semantic VIEW/VPORT UCS fixtures with pinned ezdxf.

Run from anywhere: python generate_view_ucs_fixtures.py [output_directory]
No netDxf code is imported or invoked.
"""
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

import ezdxf

PROFILES = ("R2000", "R2004", "R2007", "R2010", "R2013", "R2018")
APP = "UCS_QA"
ASSOCIATION = {
    "ucs_origin": (5.125, -6.25, 7.5),
    "ucs_xaxis": (0.0, 2.0, 0.0),
    "ucs_yaxis": (-3.0, 0.0, 0.0),
    "elevation": -9.875,
}


def generate(directory: Path) -> None:
    assert ezdxf.__version__ == "1.4.4", ezdxf.__version__
    directory.mkdir(parents=True, exist_ok=True)
    manifest = {
        "generator": "generate_view_ucs_fixtures.py",
        "generator_library": "ezdxf",
        "generator_version": ezdxf.__version__,
        "native_cad_authored": False,
        "transport": "ASCII",
        "provenance": (
            "Generated independently through ezdxf 1.4.4 APIs. Named UCS and "
            "orthographic UCS references are separate cases, matching ObjectARX "
            "setUcs overload semantics. Axis magnitudes are intentionally nonunit. "
            "No netDxf code is imported or invoked."
        ),
        "schema_sources": [
            "https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-CF3094AB-ECA9-43C1-8075-7791AC84F97C.htm",
            "https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-8CE7CC87-27BD-4490-89DA-C21F516415A9.htm",
            "https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-__OVERLOADED_setUcs_AcDbAbstractViewTableRecord.html",
        ],
        "fixtures": [],
    }
    for profile in PROFILES:
        doc = ezdxf.new(profile)
        doc.appids.new(APP)
        named = doc.ucs.new("NamedFrame", dxfattribs={
            "origin": (10.125, -20.25, 30.5),
            "xaxis": (0.0, 1.0, 0.0),
            "yaxis": (-1.0, 0.0, 0.0),
            "flags": 64,
        })
        base = doc.ucs.new("BaseFrame", dxfattribs={
            "origin": (-3.5, 4.25, 8.0), "flags": 0,
        })
        for name, handle_attr, handle, ortho in (
            ("NamedReview", "ucs_handle", named.dxf.handle, 0),
            ("UcsReview", "base_ucs_handle", base.dxf.handle, 5),
        ):
            view = doc.views.new(name, dxfattribs={
                "direction": (2.0, -3.0, 7.0), "target": (1.0, 2.0, 3.0),
                "width": 44.5, "height": 21.25, "ucs": 1,
                "ucs_ortho_type": ortho, handle_attr: handle, **ASSOCIATION,
            })
            view.set_xdata(APP, [(1000, "independent")])
        active = doc.viewports.get("*Active")[0]
        active.update_dxf_attribs({
            "ucs_vp": 1, "ucs_handle": named.dxf.handle,
            "ucs_ortho_type": 0, **ASSOCIATION,
        })
        ortho = doc.viewports.new("OrthoConfig", dxfattribs={
            "ucs_vp": 1, "base_ucs_handle": base.dxf.handle,
            "ucs_ortho_type": 4, **ASSOCIATION,
        })
        line = doc.modelspace().add_line((1, 2, 3), (4, 5, 6))
        for entity in (named, base, active, ortho, line):
            entity.set_xdata(APP, [(1000, "independent")])
        audit = doc.audit()
        assert not audit.errors and not audit.fixes, (profile, audit.errors, audit.fixes)
        path = directory / f"independent-view-ucs-{profile}.dxf"
        doc.saveas(path)
        data = path.read_bytes()
        manifest["fixtures"].append({
            "path": path.name, "profile": profile, "acadver": doc.dxfversion,
            "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data),
            "audit_errors": 0, "audit_fixes": 0,
        })
    (directory / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps({"output": str(directory), "fixtures": len(PROFILES)}))


if __name__ == "__main__":
    generate(Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent / "fixtures")
