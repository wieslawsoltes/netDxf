# SECTIONSETTINGS stored-packet fixtures

Eight unchanged producer outputs use IxMilia.Dxf 0.8.4 in text and binary for
R2007, R2010, R2013 and R2018. The manifest pins each compressed archive and its
decoded bytes, the NuGet package archive, and a separately identified schema
source commit. The package does not declare that source commit as its build
revision.

The producer writes one SECTIONSETTINGS object with two type bundles. Its first
bundle has three geometry records after one shared `SectionGeometrySettings`
marker, using documented color group 63. The empty second bundle also retains
its sequence marker. These are actual independent producer packets. IxMilia's
generation-option API is Boolean; it is not an oracle for native integer flags
such as 17, and its lenient parser is not used to establish rejection behavior.

The original producer files have unrelated DIMSTYLE/STYLE scaffolding defects
that prevent complete netDxf import. `extract_fixtures.py` places the unchanged
application packet into independent ezdxf 1.4.4 scaffolding, changing only the
explicitly mapped identity and owner handles. It verifies exact packet equality
after mapping and requires zero audit errors or repairs. Only generated CLASS
packets are sorted. Extracted bytes are deterministic under Python hash seeds
0, 1 and 17. Whole original-file typed interoperability is not claimed.

Run `python tests/fixtures/section-settings/extract_fixtures.py` to regenerate
the extracted inputs from the pinned archives. The producer source is under
`producer/`; its unrelated header timestamps need not reproduce identical
original files.

Native evidence is shared with the SECTION module at
`../section/LiveSection1.dxf.gz`, pinned to LibreDWG commit
`34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43`. The complete unchanged R2018 drawing
loads directly. Its SECTIONOBJECT handle 228 owns SECTION_SETTINGS handle 22A.
The native settings packet preserves option 17, four repeated geometry markers,
group-91 integers 1/2/4/8, indexed colors in group 62, and an unresolved reserved
layer name. Conformance compares every field of that native packet unchanged
after both text and binary output. No licensed AutoCAD execution or generated
section geometry is claimed.
