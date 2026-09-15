# Independent mixed-reference verification

The frozen `5190451` mixed runtime passed all eight cases. Later topology source changes are exercised again by the complete integration suites. The receipt pins runtime, harness, source fixture, original outputs and corrected verifier hashes.

The original 48-control verifier missed six corruptions of actual ASCII bytes. These could retain a clean independent audit while changing the stored plane, seed or count declarations. The corrected mandatory verifier rejects all 72 controls; the independent harness additionally rejects all six byte-level mutations. These checks overlap and are not a count of distinct implemented features.

Replay from the repository root with:

```sh
python doc/dxf-conformance/receipts/eighth-mixed-independent/review_eighth_mixed_independent.py . doc/dxf-conformance/receipts/eighth-mixed-independent artifacts/mixed-independent-review --prior-gate doc/dxf-conformance/receipts/eighth-mixed-independent/mixed-gate-5190451.py
```

The intentionally corrupted DXFs are negative controls. The supplied unmodified input/output pairs retain the exact runtime packets. They do not establish native AutoCAD execution or evaluation.
