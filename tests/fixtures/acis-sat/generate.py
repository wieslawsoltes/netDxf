#!/usr/bin/env python3
"""Reproduce bounded independent SAT producers with ezdxf 1.4.4, never netDxf."""
from pathlib import Path
from datetime import datetime
import hashlib
import io
import json
import ezdxf
from ezdxf.acis import api
from ezdxf.acis.hdr import AcisHeader
from ezdxf.render import forms, MeshBuilder
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.tools import crypt

ROOT = Path(__file__).resolve().parent
VERSIONS = [2000, 2004, 2007, 2010]


def chunks(path):
    data = path.read_bytes()
    tags = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None))
    active = False
    result = []
    for code, value in tags:
        if code == 100:
            active = value == "AcDbModelerGeometry"
        elif code == 0 or code >= 1000:
            active = False
        elif active and code in (1, 3):
            result.append([code, value])
    return result


def generate():
    assert ezdxf.__version__ == "1.4.4"
    ezdxf.options.write_fixed_meta_data_for_testing = True
    manifest = {"producer": "ezdxf 1.4.4", "sources": []}
    for year in VERSIONS:
        for kind in ["BODY", "REGION", "3DSOLID"]:
            doc = ezdxf.new("R" + str(year))
            doc.set_modelspace_vport(12)
            doc.header["$TDCREATE"] = 2451544.5
            doc.header["$TDUPDATE"] = 2451544.5
            doc.appids.new("QA_ACIS_SAT")
            msp = doc.modelspace()
            entity = {"BODY": msp.add_body, "REGION": msp.add_region, "3DSOLID": msp.add_3dsolid}[kind]()
            mesh = forms.cube()
            if kind == "REGION":
                mesh = MeshBuilder()
                mesh.add_face([(0, 0, 2), (3, 0, 2), (3, 4, 2), (0, 4, 2)])
            body = api.body_from_mesh(mesh)
            api.export_dxf(entity, [body])
            header = AcisHeader()
            header.set_version(700)
            header.n_entities = 1
            header.creation_date = datetime(2000, 1, 1)
            # Use the producer's length-prefixed header writer for a valid long line.
            # Its encoded form contains an apparent DXF Unicode escape verbatim.
            header.product_id = "ezdxf ACIS QA " + "C" * 270 + next(crypt.decode([r"\U+0041"]))
            entity.sat = header.dumps() + list(entity.sat[3:])
            entity.set_xdata("QA_ACIS_SAT", [(1000, "after SAT chunks"), (1070, 17)])
            entity.dxf.color = 5
            if year >= 2004:
                entity.dxf.color_name = "QA$SAT"
            if year >= 2007:
                entity.dxf.shadow_mode = 0
            msp.add_line((10, 20, 30), (40, 50, 60))
            for binary in (False, True):
                suffix = "binary" if binary else "ascii"
                path = ROOT / f"independent-acis-{kind.lower()}-R{year}-{suffix}.dxf"
                doc.saveas(path, fmt="bin" if binary else "asc")
                loaded = ezdxf.readfile(path)
                audit = loaded.audit()
                assert not audit.errors and not audit.fixes
                item = loaded.modelspace().query(kind)[0]
                bodies = api.load_dxf(item)
                surfaces = [m for b in bodies for m in api.mesh_from_body(b)]
                record = {"filename": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "version": doc.dxfversion,
                          "entity": kind, "binary": binary, "chunks": chunks(path), "lines": list(item.sat),
                          "bodies": len(bodies), "vertices": [len(m.vertices) for m in surfaces], "faces": [len(m.faces) for m in surfaces]}
                assert any(c == 3 for c, _ in record["chunks"])
                assert any(r"\U+0041" in text for _, text in record["chunks"])
                manifest["sources"].append(record)
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Generated {len(manifest['sources'])} ACIS sources; all loaded through the independent modeler subset with zero audit errors or fixes.")


if __name__ == "__main__":
    generate()
