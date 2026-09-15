# Pinned native DIMASSOC components

`source-manifest.json` identifies six unchanged LibreDWG test-corpus originals
at commit `34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43`. The compressed originals
retain complete original bytes; SHA-256 and Git blob hashes pin that evidence.

`extract_fixtures.py` requires ezdxf 1.4.4. It selects all eight DIMASSOC records
in each example drawing, their eight owner dictionaries, eight dimension hosts,
referenced geometry and the POLYLINE's actual VERTEX/SEQEND descendants. Every
one of these 32 packets remains exact except the identity/reference map recorded
in `manifest.json`. The mapped common owner places source entities in a generated
model-space BLOCK_RECORD. No association values, counts, metadata or geometry
packets are normalized, substituted or interpreted.

Named layers, dimension styles and empty dimension display blocks are synthetic
carrier resources. This fixture qualifies the selected packet relationships,
not original rendered dimensions, complete source resource definitions or an
unchanged whole-document import. The complete source drawing remains available
in the pinned gzip file for independent inspection.

The six source extractions each pass an unadapted ezdxf structural audit with
zero errors and zero repairs. Only generated CLASS ordering is sorted. Fixed
generated dates/GUIDs and `determinism.json` verify identical extraction bytes
under `PYTHONHASHSEED` values 0, 1 and 17.

Seven associations per profile use the qualified direct-geometry variant. The
native `42F` packet repeats group 331 and remains opaque: its POLYLINE→VERTEX
identity path is not replaced with a vertex index. The typed POLYLINE reader
does not preserve that separate VERTEX identity on output, so only the opaque
association packet is qualified there. Complete raw identity preservation uses
`DxfRawDocument`. Unknown reference evaluation and dimension regeneration remain
outside this suite.

`tools/verify_dimassoc.py` independently compares original, extracted and output
packets, proves exact source mappings, checks reciprocal dimension ownership and
reactor backlinks, and requires corrupted output controls to fail.
