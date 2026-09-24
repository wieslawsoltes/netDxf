# Independent GVector bounds and source preservation

`GVector.ComputeExtremes` now copies the first vector into two separate mutable
results before finding componentwise minima and maxima. Previously both output
references and the first input pointed to one object. Computing a bound could
therefore alter the input, overwrite the other bound, and return equal minimum
and maximum vectors for a nonzero-size box.

For points (3,5), (-2,7), (9,-4), the corrected outputs are (-2,-4) and (9,7),
with the original vectors unchanged. Changing either returned vector or its
exposed component array cannot change the other output or the input. Even a
single input and a zero-dimensional vector produce independent result objects
and buffers. Repeated references in the selected input prefix remain supported.

Only result ownership changes. The public signature, requested-prefix behavior,
strict component comparisons, equal-value tie handling, and existing invalid
input/assertion policy are unchanged. Equal signed zeros retain the first
encountered representative. An initial NaN remains the seed for its coordinate;
later NaNs do not replace an ordinary extreme. This preserves the prior scalar
comparison policy; it does not introduce a new total ordering or claim meaningful
finite geometry for nonfinite inputs. Other vector/matrix operations are untouched.

## Verification

The added 161 cases cover deterministic clouds in dimensions 0, 1, 2, 3, 7 and
16; one-, two- and five-vector prefixes; all six permutations of a fixed golden
box; repeated input references; and all pairs of ten binary64 values, including
signed zeros, subnormals, extreme finite values, infinities and NaN payloads.
Each case checks source bits and references, separate buffers, repeated calls,
and mutations of input and outputs after computing bounds. An unused tail
contains an incompatible vector and null, proving the requested prefix is used.

The existing GVector safety registration calls the new suite; all its preceding
cases remain unchanged. A new auto-discovered independent checker derives the
same input-only corpus, checks output binary64 bits using stable Python min/max,
and rejects changed coordinates, state/identity flags, counts, input records,
missing/extra/duplicate observations and duplicate JSON properties. It validates
real exported operation results, not expected outputs substituted for execution.

Local execution is unavailable in the authoring continuation. Hosted core build,
complete conformance, independent checks and package qualification are required
before merge. Executed counts and source identity are recorded on the PR; this
file defines tests rather than claiming they have already run. No workflow is
added, removed or weakened by this geometry increment.

This is a geometry-kernel compatibility fix, not complete DXF/AutoCAD parity.
Historical typed formats, native CAD execution, private FIELD/TABLE/cache
regeneration and rendering qualification remain separate work. The independently
maintained JavaScript port and its frozen C# comparison baseline are unchanged.
