# PolygonMesh retained-record qualification receipts

`qualification.json` pins the production source files, exact Debug/Release libraries, tests and verifier. `debug-results.json` and `release-results.json` contain the 387 new tests. The separate polyline and cardinality inventories preserve 551 and 727 existing cases per configuration. Output manifests pin generated files; gate transcripts record exact expected inventories and deliberately corrupted-output controls.

`polygon-review-summary.json`, `review-final-results.json`, `review-release-results.json` and the review audit receipts preserve the independent 50-case checks in both configurations. `review-initial-results.json` intentionally records the two failing pre-fix mesh metadata-budget cases.

`budget-review-summary.json` and the three `budget-*-results.json` files preserve the older Polyline3D defect and its fix. Both the c108513 pre-topology library and the PR92 library accept oversized child metadata and reject their own output. The fixed library rejects before bytes or handle allocation. These historical failures are evidence of the repaired behavior, not outstanding qualification failures.

For independent reproduction, copy each `*-Program.cs.txt` and corresponding `*-Probe.csproj.txt` to an isolated directory as `Program.cs` and `Probe.csproj`. Build with `/p:DxfReviewLibrary=/absolute/path/netDxf.netstandard.dll`. Run the polygon probe with the repository and output directory as arguments. Run the budget probe with an output directory and `fixed` or `gap` as the second argument, as shown by the preserved harness. No native CAD application execution is claimed.
