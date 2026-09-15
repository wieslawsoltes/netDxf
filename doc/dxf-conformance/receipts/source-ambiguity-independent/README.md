# Independent source identity review

The unchanged eight-case manager probe reproduced four accepted drawings with duplicate physical SECTION identities: an unknown, discarded entity reused the retained SECTION handle before or after its declaration, in text and binary. Native and private-control positives remained accepted. The shared source-lookup fix rejects those four duplicates; final eager validation also prevents an ambiguous retained record from silently losing common metadata.

The twenty-case regression harness links the repository test file. It covers those manager cases plus the consumed LAYER dictionary conversion, ordinary and nested private handle fields, nested reactor/extension lookalikes, and an unterminated control. Against the first uniqueness fix, only the four nested dictionary positives failed. With the balanced dictionary scanner and eager validation, all twenty pass in Debug and Release. Valid cases include cross-transport saves and preserve exact manager targets or layer snapshots.

`receipt.json` binds every saved result to the exact probe/test source and library SHA-256. Debug rejection is a normal `FormatException`; Release `DxfDocument.Load` reports invalid input by returning null. No process abort occurred in these runs. Results preserve intermediate stages rather than replacing their failures.

The probe sources are unchanged copies of the actual standalone harnesses. The project files provide relocatable reproduction; from the repository root, substitute an absolute library path and temporary output paths:

```sh
dotnet run --project doc/dxf-conformance/receipts/source-ambiguity-independent/manager-probe/Probe.csproj -p:DxfLibraryPath=/absolute/netDxf.netstandard.dll -- tests/fixtures/section/LiveSection1.dxf.gz /tmp/source-manager-probe
dotnet run --project doc/dxf-conformance/receipts/source-ambiguity-independent/regression-probe/Regression.csproj -p:DxfLibraryPath=/absolute/netDxf.netstandard.dll -- /tmp/source-regression-results.json
```

These are focused independent checks; the integration build and full conformance suite supply broader cross-module validation. The unchanged native source fixture stays in `tests/fixtures/section` under its existing provenance and hash manifest.
