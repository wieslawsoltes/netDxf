#!/usr/bin/env python3
"""Create independent ezdxf POLYGONMESH schema fixtures, not native CAD output."""
from pathlib import Path
import hashlib
import json
import ezdxf

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "tests/fixtures/polygonmesh-cardinality"

def main():
    assert ezdxf.__version__ == "1.4.4"
    DEST.mkdir(parents=True, exist_ok=True)
    receipts = []
    for version in ["AC1015", "AC1018", "AC1021", "AC1024", "AC1027", "AC1032"]:
        for mode, smooth in [("plain", 0), ("quadratic", 5), ("cubic", 6)]:
            doc = ezdxf.new(version)
            mesh = doc.modelspace().add_polymesh((4, 4))
            for m in range(4):
                for n in range(4):
                    mesh.set_mesh_vertex((m, n), (m, n, m * n * .125))
            if smooth:
                mesh.dxf.flags = 20
                mesh.dxf.smooth_type = smooth
                mesh.dxf.m_smooth_density = 4
                mesh.dxf.n_smooth_density = 5
                for vertex in mesh.vertices:
                    vertex.dxf.flags = 80
                # Separate schema-level fitted samples. Their coordinates are
                # illustrative; this is not evidence of a CAD spline evaluator.
                for m in range(4):
                    for n in range(5):
                        mesh.append_vertex((m, n * .75, m * n * .09375), {"flags": 72})
            doc.modelspace().add_line((7, 8, 9), (10, 11, 12))
            for binary in [False, True]:
                name = f"{version}-{'binary' if binary else 'ascii'}-{mode}.dxf"
                path = DEST / name
                doc.saveas(path, fmt="bin" if binary else "asc")
                reloaded = ezdxf.readfile(path)
                mesh2 = reloaded.modelspace().query("POLYLINE").first
                assert [v.dxf.flags for v in mesh2.vertices] == ([64] * 16 if not smooth else [80] * 16 + [72] * 20)
                receipts.append({"file": name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "version": version, "binary": binary, "mode": mode, "grid": [4, 4], "controls": 16, "samples": 20 if smooth else 0})
    (DEST / "manifest.json").write_text(json.dumps({"producer": "ezdxf", "producer_version": ezdxf.__version__, "native_cad": False, "fixtures": receipts}, indent=2) + "\n")

if __name__ == "__main__":
    main()
