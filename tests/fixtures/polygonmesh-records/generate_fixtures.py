#!/usr/bin/env python3
"""Unchanged independent ezdxf 1.4.4 producer files, including its BLOCK_RECORD vertex owner form."""
import hashlib
import io
import json
from pathlib import Path
import ezdxf

ROOT = Path(__file__).resolve().parent


def drawing(year):
    doc = ezdxf.new(f"R{year}")
    doc.appids.new("VERTEX_RECORD_META")
    doc.layers.new("VERTEX_ONLY")
    doc.linetypes.new("VERTEX_DASH", dxfattribs={"description": "vertex resource", "pattern": [0.75, 0.5, -0.25]})
    polyline = doc.modelspace().add_polymesh((3, 4))
    for i in range(3):
        for j in range(4):
            polyline.set_mesh_vertex((i, j), (i + 0.125 * j, j + 0.25 * i, 100 * i + 7 * j))
    first, second, third = polyline.vertices[:3]
    first.dxf.layer = "VERTEX_ONLY"
    first.dxf.linetype = "VERTEX_DASH"
    first.dxf.color = 0
    first.dxf.ltscale = 1.75
    first.dxf.lineweight = 35
    first.dxf.invisible = 1
    first.dxf.start_width = 0
    first.dxf.end_width = 2.5
    first.dxf.bulge = -0.25
    first.dxf.tangent = 15.5
    first.dxf.vertex_identifier = 17
    if year >= 2004:
        first.dxf.true_color = 0x123456
        first.dxf.color_name = "named color"
        first.dxf.transparency = 0x020000FE
    first.set_xdata("VERTEX_RECORD_META", [(1000, "vertex metadata"), (1004, bytes([0, 1, 127, 255])), (1005, third.dxf.handle)])
    first.set_reactors([second.dxf.handle, third.dxf.handle])
    second.set_reactors([first.dxf.handle])
    vertex_extension = second.new_extension_dict()
    vertex_xrecord = vertex_extension.add_xrecord("VERTEX_DATA")
    vertex_xrecord.reset([(1, "owned vertex data"), (330, first.dxf.handle), (310, bytes([5, 0, 255]))])
    end = polyline.seqend
    end.dxf.layer = "VERTEX_ONLY"
    end.dxf.linetype = "VERTEX_DASH"
    end.dxf.color = 0
    end.set_reactors([first.dxf.handle])
    end.set_xdata("VERTEX_RECORD_META", [(1000, "sequence metadata"), (1004, bytes([255, 0, 4])), (1005, second.dxf.handle)])
    end_xrecord = end.new_extension_dict().add_xrecord("SEQEND_DATA")
    end_xrecord.reset([(1, "owned sequence data"), (330, end.dxf.handle)])
    plain = doc.modelspace().add_polymesh((2, 3))
    for i in range(2):
        for j in range(3):
            plain.set_mesh_vertex((i, j), (10 + i, 20 + j, 30 + 10 * i + j))
    plain.vertices[0].dxf.layer = "VERTEX_ONLY"
    plain.vertices[0].dxf.linetype = "VERTEX_DASH"
    plain.vertices[0].dxf.end_width = 2.5
    plain.vertices[0].set_xdata("VERTEX_RECORD_META", [(1000, "clone metadata"), (1004, bytes([1, 2, 255]))])
    plain.seqend.dxf.color = 0
    doc.update_all()
    doc.classes.classes = dict(sorted(doc.classes.classes.items()))
    return doc, {"polyline": polyline.dxf.handle, "vertices": [v.dxf.handle for v in polyline.vertices], "seqend": end.dxf.handle,
                 "vertex_xrecord": vertex_xrecord.dxf.handle, "seqend_xrecord": end_xrecord.dxf.handle,
                 "block_record": doc.modelspace().block_record_handle, "plain_polyline": plain.dxf.handle,
                 "plain_vertices": [v.dxf.handle for v in plain.vertices], "plain_seqend": plain.seqend.dxf.handle}


def main():
    if ezdxf.__version__ != "1.4.4":
        raise RuntimeError("Pinned producer ezdxf 1.4.4 required")
    ezdxf.options.write_fixed_meta_data_for_testing = True
    fixtures = []
    for year in (2000, 2004, 2007, 2010, 2013, 2018):
        for binary in (False, True):
            doc, handles = drawing(year)
            stream = io.BytesIO() if binary else io.StringIO()
            doc.write(stream, fmt="bin" if binary else "asc")
            data = stream.getvalue() if binary else stream.getvalue().encode(doc.output_encoding, errors="dxfreplace")
            name = f"producer-R{year}-{'binary' if binary else 'ascii'}.dxf"
            (ROOT / name).write_bytes(data)
            audit = ezdxf.readfile(ROOT / name).audit()
            if audit.errors or audit.fixes:
                raise AssertionError((name, audit.errors, audit.fixes))
            fixtures.append({"file": name, "year": year, "binary": binary, "sha256": hashlib.sha256(data).hexdigest(), "handles": handles})
    (ROOT / "manifest.json").write_text(json.dumps({"producer": "ezdxf 1.4.4", "source": "https://github.com/mozman/ezdxf/tree/v1.4.4", "unchanged_producer_bytes": True,
        "native_application_execution": False, "owner_form": "VERTEX -> containing BLOCK_RECORD; SEQEND -> POLYLINE", "fixtures": fixtures}, indent=2) + "\n")


if __name__ == "__main__":
    main()
