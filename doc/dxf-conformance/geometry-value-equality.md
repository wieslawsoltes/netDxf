# GTE vector and matrix value equality

`GMatrix.operator ==` no longer invokes itself while testing for null. Its
reference guards now use `ReferenceEquals`, `Equals(GMatrix)` compares values
without calling the operator, and `!=` is the complement of equality. Two null
references compare equal; exactly one null or a different shape compares unequal.
This prevents process-terminating recursion in ordinary matrix comparisons.

`GVector.Equals` uses `Double.Equals` component by component instead of an
absolute-difference test against `Double.Epsilon`. The old predicate incorrectly
accepted NaN against arbitrary values, could conflate adjacent subnormals, and
was not transitive. Signed zeros are equal, every NaN payload compares equal to
other NaNs but not to finite values, and other finite/infinite values compare
exactly. This is the .NET value-equality convention, not native C++ NaN equality
or approximate geometric matching. The global geometric epsilon is irrelevant.

Both classes require the same runtime type, consistently with their existing
object equality entry point. Typed equality, object equality, operators and the
default collection comparer therefore agree. The public API signatures are
unchanged. Subclasses that define their own equality remain responsible for
maintaining their own equality contract.

Hashes now use the compared values (and matrix dimensions) rather than backing
array identity. Signed zero and NaN representatives are canonicalized for hashing
without changing the stored bits, including on .NET Framework runtimes whose
Double hash can distinguish NaN payloads. Independently constructed equal values
can be found or removed
using Dictionary and HashSet. Hashes are computed on demand; the classes and
their exposed component buffers are still mutable. **Never mutate a key while
it is indexed in a hash collection.** Hash values are runtime/process data,
not persistent IDs or cross-runtime serialization values. Unequal values may
legitimately collide.

## Qualification

The 555-case focused harness contains all 512 vector/matrix pair combinations over
sixteen binary64 inputs, including both zeros, adjacent subnormals, adjacent
ordinary values, extreme finite values, infinities and several NaN payloads.
Every pair checks both operators, typed/object/default equality, symmetry,
Dictionary lookup, HashSet cardinality, equal hashes and unchanged input bits.
Additional cases cover nulls, empty/nonsquare matrices in both storage modes,
vector sizes, inherited runtime types, geometric epsilon, arithmetic and safe
remove/mutate/reinsert operations. No previous test registration is removed.

The independent checker decodes the input bit patterns and verifies all 512
actual comparison records. It tests altered comparisons, hashes, identities,
collection cardinalities, duplicate JSON keys and incomplete inventories.
It does not assume any numeric hash value or reject legitimate hash collisions.
The shared installed-package consumer adds matrix/vector collection checks;
those assertions run through ordinary package selection and the existing eight
exact-assembly profiles when hosted CI executes them.

Existing ordering operators, constructor/dimension admission, inverse/solver
algorithms and general numerical conditioning are unchanged and not qualified
by this equality correction. No new DXF dialect, native renderer, private cache
regeneration, document transaction or AutoCAD open/AUDIT/save/reopen evidence is
provided. C# geometry-kernel fixes do not alter the JavaScript port's frozen
reference or waive its current differential failures.

Primary references: [Microsoft value equality guidance](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/statements-expressions-operators/how-to-define-value-equality-for-a-type)
and [Double.Equals](https://learn.microsoft.com/en-us/dotnet/api/system.double.equals),
including its NaN convention. Executed counts, source hashes and failures belong
to the PR's verification record, not inferred from these test definitions.
