# Independent periodic numerical qualification

This review found and verified corrections for periodic numerical defects in
the recovered local-weight evaluator integrated as `677c266`. Final production
source is `b604a12`; exact source and assembly checksums are recorded beside the
results. The same precompiled public-API harness, input cases and Python exact
rational oracle were used for the comparisons.

| Result | Before | Final Debug | Final Release |
| --- | ---: | ---: | ---: |
| Cases submitted | 376 | 376 | 376 |
| Accepted cases | 347 | 351 | 351 |
| Explicitly rejected cases | 29 | 25 | 25 |
| Evaluated samples | 4,619 | 4,707 | 4,707 |
| Coordinate comparisons outside the stated tolerance | 1,246 | 0 | 0 |
| Reversal comparison failures | 2 | 0 | 0 |

The 25 final refusals are the same deliberately unrepresentable knot/grid cases
present in the baseline: repeated binary64 knots or a requested sampling grid
whose parameters cannot remain distinct. Four additional baseline refusals were
finite constant-MaxValue curves whose intermediate arithmetic overflowed; all
four are accepted correctly after the correction. Refusals are reported
separately and are not counted as successful evaluations.

The corpus comprises 60 constant-coordinate cases across five degrees, six
coordinate magnitudes and two weight forms; 100 deterministic cases spanning
binary64 coordinate and weight exponents; 100 nonconstant subnormal-coordinate
cases; 90 extreme knot scale/offset/grid cases; six compensated weight-ratio
cases; nine unit-weight cancellation cases; nine non-dyadic weighted cancellation
cases; and two positive-subnormal-basis cases. This is 365 original cases plus
two separately preserved supplements of nine and two cases.

`probe/Program.cs` uses public constructors, HATCH adapters, `PolygonalVertexes`,
clone and reversal. `review.py` does not call the implementation's evaluator:
it expands the actual control list, evaluates the B-spline basis using exact
Python fractions of binary64 input values, forms the rational weighted quotient,
and rounds its result to binary64. It evaluates at the actual binary64 sampling
parameter, keeping parameter-grid rounding separate from curve arithmetic.
Comparisons flag relative error above 1e-10 when exceeding two output ULPs;
constant subnormal coordinates must retain their exact representable value.
Reversal comparisons use a separate 1e-9 relative/four-ULP boundary.

The witnesses isolate several distinct defects:

* Constant and nonconstant subnormal coordinates disappeared during early
  weighted-coordinate multiplication, including a constant curve returning the
  origin instead of `(double.Epsilon,-double.Epsilon,0)`.
* Dividing weights `1e-310/1e13` quantized a subnormal coefficient before a
  coordinate of magnitude `1e308` restored its normal contribution, causing
  about 1.19% relative error.
* Opposing large coordinates erased a finite middle contribution during
  summation: an exactly representable sample of 0.75 was returned as zero.
* Non-dyadic weight products lost residuals before their compensated sum. One
  sample returned 0.9523809523809523 instead of the exact-input reference
  0.7935873589017969. The final exact local-basis fallback corrects the original
  binary64 packet rather than reusing already rounded basis values.
* A positive subnormal basis value, rather than a completely underflowed zero,
  caused a restored normal coordinate to differ by about 1.19%. It now uses the
  exact fallback as well.
* Reversal on an active domain `[-8e307,8e307]` lost the reflected knot `1e-15`
  by subtracting the original tiny knot from a large endpoint first. Summing
  opposite-sign active endpoints first preserves that knot and its curve.

`actual-numeric-outputs.tar.gz` preserves all nine actual output packets: the
three case groups against the baseline, final Debug and final Release. Each
packet has an entry in `output-manifest.json`. The per-case oracle reports remain
directly readable under `before`, `after-debug` and `after-release`. The compiled
harness and selected library checksums identify the binaries actually tested;
library binaries can be rebuilt from their source commits.

To reproduce, copy the selected net8.0 `netDxf.netstandard.dll` beside
`probe/Probe.csproj`, build that project with .NET SDK 8, then run its `Probe.dll`
with a case-file path and an output JSON path. Run:

```sh
python review.py cases-complete.json output.json review.json
```

Repeat with `cases-weighted-cancel.json` and `cases-basis-subnormal.json`. Select
the final Debug or Release library without changing the harness or input files.
The recorded run used SDK 8.0.408 and the net8.0 runtime. This receipt does not
claim native CAD execution or establish an error bound for every possible spline
packet; it records a reproducible numerical regression gate and its explicit
input refusals.
