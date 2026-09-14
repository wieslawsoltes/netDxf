#!/usr/bin/env python3
"""Independent ezdxf producers for pointer, redraw-order and clipping objects.

Autodesk SPATIAL_FILTER schema:
https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-34F179D8-2030-47E4-8D49-F87B6538A05A.htm
ezdxf 1.4.4 primary sources: entities/idbuffer.py, entities/dxfobj.py,
entities/spatial_filter.py and xclip.py. These fixtures assert storage and graph
semantics, not native AutoCAD rendering. SORTENTSTABLE R2000 is a separate
compatibility probe, since ezdxf documents a native-reader issue at that version.
"""
from pathlib import Path
import hashlib
import json
import ezdxf
from ezdxf.math import Matrix44
from ezdxf.xclip import new_spatial_filter

ROOT = Path(__file__).resolve().parent
VERSIONS = (2000, 2004, 2007, 2010, 2013, 2018)


def build(year, force_sort=False):
    doc = ezdxf.new(f"R{year}")
    doc.appids.add("QA_CONTAINERS")
    model = doc.modelspace()
    line1 = model.add_line((1.25, -2.5, 3.75), (8.5, 9.25, -4.125))
    line2 = model.add_line((-10, -20, -30), (-40, -50, -60))
    circle = model.add_circle((4.5, -5.25, 6.125), 2.75)
    block = doc.blocks.new("QA_CLIP_CONTENT")
    block.add_line((-5, -4, 0), (8, 7, 0))
    block.add_circle((1, 2, 0), 3)
    app = doc.rootdict.add_new_dict("QA_CONTAINERS", hard_owned=True)
    variable = app.add_dict_var("MODE", "container fixture")
    clips = []
    for shape in ("rectangle", "polygon"):
        for front, back in ((0, 0), (0, 1), (1, 0), (1, 1)):
            index = len(clips)
            name = f"{shape}-front{front}-back{back}"
            insert = model.add_blockref("QA_CLIP_CONTENT", (10.5 + index, -7.25, 3.125),
                                       dxfattribs={"xscale": 2, "yscale": 3, "zscale": 4,
                                                   "rotation": 30})
            insert.set_xdata("QA_CONTAINERS", [(1000, name)])
            spatial = new_spatial_filter(insert)
            boundary = ((-2.5, -1.25), (7.75, 5.5)) if shape == "rectangle" else (
                (-3.5, -1.25), (5.75, -2.5), (8.25, 2.75), (3.125, 7.5), (-4.5, 4.25))
            spatial.set_boundary_vertices(boundary)
            spatial.dxf.extrusion = (0, 0.6, 0.8)
            spatial.dxf.origin = (1.125, -2.25, 3.5)
            spatial.dxf.is_clipping_enabled = index % 2
            spatial.dxf.has_front_clipping_plane = front
            spatial.dxf.front_clipping_plane_distance = 2.5 + index
            spatial.dxf.has_back_clipping_plane = back
            spatial.dxf.back_clipping_plane_distance = 19.25 + index
            inverse = insert.matrix44()
            inverse.inverse()
            spatial.set_inverse_insert_matrix(inverse)
            transform = Matrix44((
                1.25, 0.125, 0.25, 0,
                0.375, 1.5, -0.125, 0,
                -0.25, 0.625, 2.25, 0,
                7.5 + index, -11.25, 3.125, 1,
            ))
            spatial.set_transform_matrix(transform)
            spatial.set_xdata("QA_CONTAINERS", [(1000, name), (1005, insert.dxf.handle)])
            clips.append({"name": name, "insert": insert.dxf.handle,
                          "spatial": spatial.dxf.handle,
                          "owner": spatial.dxf.owner,
                          "boundary": boundary,
                          "front": front, "back": back,
                          "enabled": index % 2,
                          "front_distance": 2.5 + index if front else None,
                          "back_distance": 19.25 + index if back else None,
                          "inverse_matrix": list(inverse), "transform_matrix": list(transform)})
    buffer = doc.objects.add_dxf_object_with_reactor("IDBUFFER", {"owner": app.dxf.handle})
    buffer.handles = [circle.dxf.handle, "0", line1.dxf.handle, circle.dxf.handle,
                      variable.dxf.handle, clips[0]["insert"], "0"]
    app.add("BUFFER", buffer)
    buffer.set_xdata("QA_CONTAINERS", [(1000, "ordered nulls and duplicates"),
                                       (1005, line1.dxf.handle)])
    empty = doc.objects.add_dxf_object_with_reactor("IDBUFFER", {"owner": app.dxf.handle})
    app.add("EMPTY", empty)
    associations = []
    if year >= 2004 or force_sort:
        associations = [(circle.dxf.handle, "FFFFFFFFFFFFFFFE"),
                        (line2.dxf.handle, "0"),
                        (clips[0]["insert"], line2.dxf.handle),
                        (line1.dxf.handle, "FFFFFFFFFFFFFFFE")]
        model.set_redraw_order(associations)
        doc.header["$SORTENTS"] = 17
    return doc, {"version": doc.dxfversion, "buffer": buffer.dxf.handle,
                 "buffer_handles": list(buffer.handles), "clips": clips,
                 "redraw_order": associations}


def main():
    manifest = {"producer": "ezdxf " + ezdxf.__version__, "fixtures": []}
    for year in VERSIONS:
        doc, semantics = build(year)
        path = ROOT / f"independent-typed-containers-R{year}.dxf"
        doc.saveas(path)
        saved = ezdxf.readfile(path)
        audit = saved.audit()
        assert not audit.errors and not audit.fixes, (path, audit.errors, audit.fixes)
        assert len(list(saved.objects.query("SPATIAL_FILTER"))) == 8
        manifest["fixtures"].append({"file": path.name,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), **semantics})
        print("PASS", path.name, "8 clips; zero audit errors/repairs")
    probe, semantics = build(2000, force_sort=True)
    probe_dir = ROOT / "compatibility-probe"
    probe_dir.mkdir(exist_ok=True)
    path = probe_dir / "sortentstable-R2000.dxf"
    probe.saveas(path)
    manifest["compatibility_probe"] = {"file": str(path.relative_to(ROOT)),
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "note": "Independent ezdxf storage only; R2000 native-reader compatibility is unqualified.",
        **semantics}
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
