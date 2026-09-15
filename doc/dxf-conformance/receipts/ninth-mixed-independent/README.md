# Independent mixed graph identity review

The unchanged reviewer harness exercises four coherent modifications to actual
ASCII output bytes: replacing a manager, a surviving SECTION, its settings, or a
released mesh with a different identity. Incoming references and the handle seed
are adjusted consistently. All four changed drawings remain readable by ezdxf
without audit repairs, but the mixed gate rejects them because their identities
do not match the saved input snapshot. The intentionally changed DXFs are
negative controls.

The receipt pins the reviewer, gate and input/output hashes. The mandatory gate
also passes all 16 ordinary outputs and rejects its 300 parsed-output controls.
The additional four controls demonstrate identity continuity; their counts do not
measure feature completeness.

Generate the ordinary outputs by running the conformance harness with
`DXF_TEST_FILTER=ninth-mixed/`, then replay the independent review from the repo
root:

```sh
python doc/dxf-conformance/receipts/ninth-mixed-independent/review_ninth_mixed_independent.py . artifacts/conformance artifacts/ninth-independent-review
```

The tests explicitly author SECTION/settings links around the native map and
resource packets. This evidence does not establish section evaluation or native
AutoCAD execution.
