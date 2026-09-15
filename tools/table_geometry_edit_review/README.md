# Independent editable TABLEGEOMETRY review

These harness and audit sources are preserved unchanged from the independent
review of production commit `34780f6f07a68637643a18d2f51bca9587a0b89b`. Exact
source, library, result and audit hashes appear in the unchanged
[review summary](../../doc/dxf-conformance/table-geometry-editing-review/summary.json).
Its original scratch-relative paths describe where that review ran. The same
result and audit bytes are retained in the neighboring `debug-*` and `release-*`
receipt files.

Build `Probe.csproj` with
`-p:DxfReviewLibrary=/absolute/path/to/netDxf.netstandard.dll` for the chosen
configuration. Run the resulting probe DLL with a fresh output directory as
the first argument and the repository directory as the second argument. Both
arguments are needed for the full 40-case review, including the pinned native
packets. Run `audit_geometry_edit_outputs.py` with the resulting output directory
to independently audit the generated DXFs with ezdxf.

Debug and Release each passed 40 cases and audited 26 outputs without errors or
repairs. The summary discloses the use of whole native originals and selected
native carriers, binary comment handling, reader-normalized handle spelling,
and corrected initial fixture setup assumptions. No production change was
requested. Native application execution and table regeneration were not tested.
