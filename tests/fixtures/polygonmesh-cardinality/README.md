# PolygonMesh coordinate-count fixtures

`manifest.json` pins 36 independent ezdxf 1.4.4 documents: three polygon mesh
profiles × six supported versions × ASCII/binary. Plain grids have 16 ordinary
vertices. Quadratic and cubic grids have 16 frame controls and 20 separately
classified generated sample packets. Samples are illustrative schema data; the
producer does not claim native CAD spline evaluation. Recreate with
`python tools/generate_polygonmesh_cardinality.py` and review regenerated hashes.

`native-manifest.json` pins two native TS1 3×4 mesh carriers and the compressed
originals already checked in under `tests/fixtures/field-oracle`. Each retains the
14 physical POLYLINE/VERTEX/SEQEND packets. Only the parent's common owner handle
changes to attach it to the carrier's model-space block. The independent verifier
checks the exact packet transplant, not just geometry.

The module contract and exclusions are documented in
`doc/dxf-conformance/POLYGONMESH_CARDINALITY.md`.
