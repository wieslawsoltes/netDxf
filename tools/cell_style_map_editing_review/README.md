# Independent CELLSTYLEMAP editing review

This executable probes the public editing API using an already built netDxf
assembly. It is separate from the conformance test executable and does not use
its reflection helpers, fixture builders or assertions.

After building the library and generating the `cell-map-edit-*.dxf` conformance
artifacts, run from the repository root:

```sh
dotnet run --project tools/cell_style_map_editing_review/Probe.csproj -c Debug -- artifacts/cellmap-debug artifacts/cellmap-probe-debug.json
dotnet run --project tools/cell_style_map_editing_review/Probe.csproj -c Release -- artifacts/cellmap-release artifacts/cellmap-probe-release.json
python3 tools/verify_cell_style_map_editing.py artifacts/cellmap-debug
python3 tools/verify_cell_style_map_editing.py artifacts/cellmap-release
```

The runtime probe has 30 cases across R2004 and R2018. It checks caught reentry
and source-version changes during `GetEnumerator`, `MoveNext`, `Current` and
`Dispose`; ownership unlinking; invalid reactors; caller side effects after
failure; literal Unicode escape strings; acceptance at the exact plain and
escaped 1,048,576-code-unit limits; rejection one code unit beyond those limits;
and binary/text behavior for names containing CR/LF. Failed edits must retain
the old payload and entry snapshots, and the edit guard must permit recovery.
The output records the actual loaded assembly's SHA-256.

The Python gate independently reads 10 schema and 10 native output packets with
ezdxf 1.4.4. Native source hashes and the 316 selected carrier records are checked
against the pinned fixture manifests. Only the three known producer entry-name
tags may change in each native map payload. Common metadata, source ownership,
resource identities, carrier closure and class metadata are checked separately.
All 254 mutations of actual parsed output packets must be rejected by the same
verification functions used for the positive outputs. The schema controls check
Unicode and literal DXF escape semantics with ezdxf's decoder.

This qualifies stored names and packet/graph preservation. It does not qualify
table regeneration, cell-style role inference, font rendering or CAD execution.
