# Independent TABLESTYLE edit review

This standalone probe checks the public TABLESTYLE API against a frozen library assembly.
It is separate from the conformance implementation tests and uses metamorphic comparisons:
applying all three row edits and a header edit in one call must produce exactly the same
stored tags as sequential edits in three different orders. It also checks immutable prior
snapshots, unchanged resource identities, scalar/string round trips in both transports,
and callback reentry from GetEnumerator, MoveNext, Current, and Dispose.

Each carrier exercises 244 cases: three current cultures, ten descriptions, eight distinct
scalar values (including signed zero, the first two positive subnormals and double.MaxValue),
plus four callback positions. Descriptions include maximum-length Unicode and backslashes,
surrogate pairs, and literal escape-looking text. Numeric comparison uses IEEE-754 bits.

The input should be a valid native carrier with a recognized classic TABLESTYLE header.
`DXF_TEST_FILTER=table-style-edit` in the conformance harness creates suitable
`edited-table-style-native-before-*.dxf` carriers from the pinned producer fixtures.
Pass the exact implementation DLL rather than silently rebuilding it during review:

```sh
dotnet build tools/table_style_edit_review/probe.csproj \
  -p:NetDxfAssembly=/absolute/path/netDxf.netstandard.dll
dotnet tools/table_style_edit_review/bin/Debug/net8.0/probe.dll \
  /absolute/path/edited-table-style-native-before-acad_table_simple.dxf-False.dxf result.json
```

The default assembly reference is the repository's Debug net8.0 library build. Results
record input and library SHA-256, source profile, every case, and full failure details.
The qualification record names the exact production commit. This probe supplies no
native AutoCAD execution or TABLE/CELLSTYLEMAP layout/regeneration evidence.
