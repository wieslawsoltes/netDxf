# Native stored section source

`LiveSection1.dxf.gz` contains the complete, unmodified R2018 DXF source from
LibreDWG commit `34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43`, path
`test/test-data/2018/LiveSection1.dxf`.

The gzip wrapper is deterministic (`mtime=0`). `manifest.json` pins the original
and compressed hashes, source byte count and primary Autodesk schema reference.
No section entity, settings packet, resource, owner, class or carrier record was
rewritten in the original. The complete original file loads directly.

The relevant native records are SECTIONOBJECT `228`, SECTION_SETTINGS `22A` and
the root-owned stored SECTION_MANAGER `229`. The entity uses indicator field62
inside AcDbSection and physically owns its settings through360/reciprocal330.
Settings generation option91 is17, and the four geometry91 values are1/2/4/8;
these are independent stored integers. Common entity proxy graphics total188bytes.

`tools/verify_section.py` verifies the entity/settings bodies and related graph
in complete-file exports and distinguishes native audits from the explicit
in-memory audit adapter used solely for documented SECTION spelling outputs.
See `doc/dxf-conformance/section.md` for exact qualified scope.
`tools/verify_section_manager.py` separately qualifies the exact native manager
packet and its registered section target; its schema cases for other profiles
are explicitly synthetic. See `doc/dxf-conformance/section-manager.md`.
