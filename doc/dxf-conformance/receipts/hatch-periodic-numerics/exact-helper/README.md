# Exact periodic helper qualification

This frozen 403-case corpus qualifies the exact fallback helper separately from
the periodic end-to-end numerical corpus. The executable links the production
helper source directly and uses the built netDxf assembly for its existing
Vector3 and spline-degree definitions. It does not assert that every sample
routes through the fallback in the public evaluator.

The acceptance criterion is equality of every output binary64 bit to an
independent Python `Fraction` calculation. Floating inputs are interpreted as
their exact binary64 rational values, including the actual sample parameter.
The reference uses recursive Cox-de Boor basis evaluation; the implementation
uses iterative local basis evaluation. Rational-to-float reference conversion
uses Python's nearest-even conversion, and the comparison includes signed zero.

The corpus contains generated finite rational quotients, explicit halfway cases
near normal and subnormal limits with both signs, Double.MaxValue, nonuniform
basis evaluations for degrees 1 through 10, all nine weighted-cancellation
witnesses, and a separate non-dyadic basis-cancellation witness. Every one of the
403 comparisons passed. `results.json` records the tested helper source hash;
the compressed expected values and outputs preserve the original run.

`basis-cancellation-witness.json` is also supplied in the end-to-end review
schema. Its forward sample uses the actual parameter
`0.45000000000000007`, with exact X result bits `3FEA7269B338257D`.
Only forward evaluation is qualified by this witness. Tiny differences in
reversed floating sample parameters or reflected knots can be amplified by its
ill-conditioned opposing large control coordinates.

Replay the stored evidence from this directory:

```sh
python3 review.py cases.json.gz outputs.json.gz
```

To rerun the linked implementation, first build netDxf for the desired
configuration. Then, from this directory:

```sh
gzip -dc cases.json.gz > /tmp/netdxf-exact-cases.json
dotnet run --project Probe.csproj -c Debug -- /tmp/netdxf-exact-cases.json /tmp/netdxf-exact-outputs.json
python3 review.py cases.json.gz /tmp/netdxf-exact-outputs.json
```

The helper uses GCD-reduced BigInteger rationals, with a 65,536-byte bound on
numerator/denominator components and multiplication size checks before forming
products. Exceeding the bound rejects with `NotSupportedException`. Final
rounding uses integer division with remainder and constructs the destination
binary64 directly, avoiding an intermediate floating quotient or subnormal
double rounding. No FMA, extended floating precision, or recent BigInteger API
is required by the helper.
