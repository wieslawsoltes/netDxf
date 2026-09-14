#!/usr/bin/env python3
"""Independently author GEODATA v2 sources with ezdxf, retaining raw SHA256.

Autodesk wire reference:
https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-104FE0E2-4801-4AC8-B92C-1DDF5AC7AB64.htm
ezdxf entity source: entities/geodata.py. Tests qualify storage and identity;
they do not evaluate geodetic reprojection, survey accuracy or mesh rendering.
"""
from pathlib import Path
import hashlib
import json
import ezdxf
from ezdxf.entities.geodata import EPSG_3395

ROOT = Path(__file__).resolve().parent
SOURCE = [(-10.5, -20.25), (30.125, -20.25), (30.125, 40.5), (-10.5, 40.5), (1.25, 2.75)]
TARGET = [(11.4, 48.1), (11.6, 48.1), (11.6, 48.3), (11.4, 48.3), (11.5, 48.2)]
FACES = [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)]


def build(year):
    doc = ezdxf.new(f"R{year}")
    doc.appids.add("QA_GEODATA")
    model = doc.modelspace()
    line = model.add_line((20, 30, 40), (50, 60, 70))
    attrs = {
        "version": 2, "block_record_handle": model.block_record_handle,
        "coordinate_type": 1, "design_point": (1000000.0000000002, -123.75, 50.5),
        "reference_point": (11.576124, 48.137154, 500.25),
        "horizontal_unit_scale": 0.3048, "horizontal_units": 2,
        "vertical_unit_scale": 0.0254, "vertical_units": 1,
        "up_direction": (2, 3, 4), "north_direction": (3, 4),
        "scale_estimation_method": {2010: 2, 2013: 3, 2018: 4}[year],
        "user_scale_factor": 0.9996, "sea_level_correction": int(year != 2010),
        "sea_level_elevation": -17.25, "coordinate_projection_radius": 6378137.0,
        "geo_rss_tag": "<georss:point>48.137154 11.576124</georss:point>",
        "observation_from_tag": "survey origin Żółć", "observation_to_tag": "survey target 東京",
        "observation_coverage_tag": "five anchors; four triangles",
    }
    geo = model.new_geodata(dxfattribs=attrs)
    geo.coordinate_system_definition = EPSG_3395.replace("<Dictionary version=", (
        "<!-- independent QA: Żółć 東京; coordinate definition spans many255-character chunks -->\n"
        "<Dictionary version="))
    geo.source_vertices.extend(SOURCE)
    geo.target_vertices.extend(TARGET)
    geo.faces = list(FACES)
    geo.set_xdata("QA_GEODATA", [(1000, "geodata source fixture"), (1005, line.dxf.handle),
                                 (1004, bytes((0, 1, 255)))])
    app = doc.rootdict.add_new_dict("QA_GEODATA", hard_owned=True)
    record = app.add_xrecord("REFERENCE")
    record.extend([(1, "geodata reference"), (330, geo.dxf.handle), (340, model.block_record_handle)])
    return doc, {"version": doc.dxfversion, "geodata": geo.dxf.handle,
        "owner": geo.dxf.owner, "block_record": model.block_record_handle,
        "attributes": attrs, "coordinate_system_definition": geo.coordinate_system_definition,
        "source_vertices": SOURCE, "target_vertices": TARGET, "faces": FACES,
        "line": line.dxf.handle, "reference_record": record.dxf.handle}


def main():
    manifest = {"producer": "ezdxf " + ezdxf.__version__, "fixtures": []}
    for year in (2010, 2013, 2018):
        doc, semantics = build(year)
        path = ROOT / f"independent-geodata-R{year}.dxf"
        doc.saveas(path)
        loaded = ezdxf.readfile(path)
        saved = loaded.modelspace().get_geodata()
        assert saved is not None and saved.dxf.version == 2
        assert saved.coordinate_system_definition == semantics["coordinate_system_definition"]
        assert [tuple(v) for v in saved.source_vertices] == SOURCE
        assert saved.faces == FACES
        assert saved.get_crs() == (3395, True)
        audit = loaded.audit()
        assert not audit.errors and not audit.fixes, (audit.errors, audit.fixes)
        manifest["fixtures"].append({"file": path.name,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), **semantics})
        print("PASS", path.name, "v2; five point pairs/four faces; zero audit errors/repairs")
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
