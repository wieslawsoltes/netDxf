# Independent TABLECONTENT edit review

`Program.cs`, `Probe.csproj` and `audit_probe_outputs.py` are unchanged copies of
the independent review harness. The reviewer tested production commit
`c87afec225d1f31b04e645b4d4719cac947fee21` against frozen Debug and Release
assemblies. The original receipt and result/audit files are preserved under
`doc/dxf-conformance/table-content-editing-review/`.

From the repository root, build the library for the desired configuration and
then build this harness with an absolute assembly path:

```sh
dotnet build tools/table_content_edit_review/Probe.csproj -c Release \
  -p:DxfReviewLibrary=/absolute/path/to/netDxf.netstandard.dll
dotnet tools/table_content_edit_review/bin/Release/net8.0/Probe.dll \
  /absolute/path/to/repository /absolute/path/to/probe-output
python tools/table_content_edit_review/audit_probe_outputs.py \
  /absolute/path/to/probe-output
```

The two harness arguments are the repository and output directory. Input fixtures
are verified through the existing TABLECONTENT carrier manifest. The harness
uses only public editing APIs and inspection; reflection is limited to the
internal handle-seed inspection used to check rejection atomicity.

The original 64 DXF output files are preserved byte-for-byte in
`doc/dxf-conformance/table-content-editing-review/native-outputs.tar.gz`.
Archive members retain the original `tablecontent-edit-expanded/` and
`tablecontent-edit-release/` names. `native-output-manifest.json` records each
member's SHA256 and length; every archive member was read back and verified
against its original bytes. The summary and individual JSON receipts are copied
without changing their original paths or reported assembly identities.

The reviewer found no production defect. The requested companion-metadata
oracle expansion is implemented separately in
`tools/verify_editable_table_content.py`, using only verified input-carrier
metadata substitutions. This owner gate does not substitute for the unchanged
independent runtime and output review.
