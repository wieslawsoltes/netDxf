# Independent target-version diagnostics review

This directory preserves the three independent C# harness files and project file byte-for-byte, with a `.txt` suffix, together with the Debug/Release results and actual-writer evidence. The receipt records their hashes, the production assembly hashes, and the fixture-factory assembly hash used for the recorded run.

The review ran 518 cases in each configuration. It checks every documented diagnostic code and exact public property path, all eleven retained-record families with actual source identities, the three physical output omissions, all six writer profiles and both transports. It separately checks captured snapshots, live source references, lazy object-database avoidance, allocation/header state, collection membership/order/identity and silent metadata callbacks.

Valid fixture construction reuses the disclosed conformance helper methods. The diagnostic expectations, actual-Save comparisons, packet assertions and purity checks are independent. The historical factory assembly hash in the receipt precedes the final conformance-only opaque-control additions; the reused fixture methods are unchanged. A replay against a newly built conformance assembly records its new hash.

To replay, copy `Program.cs.txt`, `Matrix.cs.txt`, `StoredMatrix.cs.txt` and `Probe.csproj.txt` to a temporary directory and remove the `.txt` suffix. Build `Probe.csproj` with `-p:DxfReviewLibrary=/absolute/path/to/netDxf.netstandard.dll`. Run the built probe with three absolute arguments: output directory, conformance assembly path, and repository root. Run both configurations against their respective library assemblies. For example:

```sh
dotnet build /tmp/version-review/Probe.csproj -p:DxfReviewLibrary=/checkout/netDxf/bin/Debug/net8.0/netDxf.netstandard.dll
dotnet /tmp/version-review/bin/Debug/net8.0/Probe.dll /tmp/version-review-output /checkout/tests/netDxf.Conformance/bin/Debug/net8.0/netDxf.Conformance.dll /checkout
```

The report is bounded to existing writer behavior. The review makes no native CAD, rendering, application evaluation or complete DXF legality claim.
