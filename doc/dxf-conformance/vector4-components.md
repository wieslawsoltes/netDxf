# Four-component export and distance audit

Two copied-component defects in `Vector4` are corrected:

* `ToArray()` returned only X, Y and Z. It now returns a fresh four-element array
  in X/Y/Z/W order, matching the array constructor and indexer contracts.
* `SquareDistance(u, v)` used `(u.W - v.Z) * (u.W - v.W)` for its final term.
  Both factors now use W, restoring the four-dimensional squared-distance formula.
  `Distance`, which takes its square root, receives the same correction.

Array export preserves stored binary64 component bits, including signed zero,
subnormals, finite extremes, infinities and quiet-NaN payloads. Independent exports
do not alias each other or mutate the source. Consumers expecting the erroneous
three-element result must adapt; there is no silent compatibility mode.

The distance correction does not introduce a robust hypot implementation or a
new nonfinite-input policy. Overflow, underflow and NaN behavior of the existing
subtraction/sum/square-root operations remain their ordinary floating-point
behavior. General normalization, equality and unrelated vector methods are not
changed.

The same 171 focused cases fail before the two production corrections and pass
afterward. The focused tests exercise exports in every component, constructor round trips,
source isolation, 128 bounded exact-integer distance cases, 32 W-only separations,
symmetry, self distance and translation invariance. One additional harness case
emits 512 deterministic scenarios; these iterations are not counted as 512 extra
harness cases. The independent checker regenerates the integer inputs, compares
squared distances exactly, and checks square roots against 100-digit Decimal
arithmetic within one ULP. Corrupted outputs, truncated inputs and changed corpus
lengths must reject through the same positive validator.

```sh
DXF_TEST_FILTER=vector4-components/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_vector4_components.py artifacts/conformance
```

Exact before/after and complete-suite results are recorded against the task PR's
executed source. This fixes existing reusable math APIs, not additional DXF
versions or native AutoCAD behavior. Native producer/visual qualification, all
historical typed dialects, private FIELD/TABLE regeneration and general document
conversion remain separate work.
