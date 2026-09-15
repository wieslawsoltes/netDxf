# Polyline3D topology qualification artifacts

The qualification JSON pins the executed source and runtime dependency hashes,
all 551 unique results in each configuration, the 169-case new topology group,
188 output hashes per configuration and the two independent gate logs.
The independent review has its own 60-case results, twelve output hashes and
audit observations per configuration. Its cases overlap the behavior under
test and are not added to the conformance case count.

The probe source and project are copied unchanged from the independent reviewer.
To reproduce it, copy `review-probe.cs.txt` to `Program.cs` and
`review-probe.csproj.txt` to `Probe.csproj` in an isolated folder, then run:

```sh
dotnet build Probe.csproj -p:DxfReviewLibrary=/absolute/path/netDxf.netstandard.dll
dotnet bin/Debug/net8.0/Probe.dll /absolute/output/directory
```

Run the owner suite with `DXF_TEST_FILTER=polyline-` and a separate
`DXF_TEST_ARTIFACTS` directory for each configuration. Then run both
`tools/verify_polyline_topology.py` and `tools/verify_polyline3d_records.py`
against that directory. Fixture manifests and native-source hashes remain
in their existing checked-in fixture directories. The complete current
repository CI is qualified by the combined integration checkpoint.
