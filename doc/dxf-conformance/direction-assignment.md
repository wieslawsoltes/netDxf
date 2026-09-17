# Atomic finite direction assignment

This C# audit task starts from merged PR106, commit
`cd2b4b2d0949eaa85ff517b438a1838f8fb25ffb`, tree
`68dd970d70c3d79c8759aef02e4c9f492f2a8f06`. It is independent of
clone/transform PRs #107–110 and does not change the JavaScript port.

## Corrected existing behavior

`EntityObject.Normal`, `Attribute.Normal`, `AttributeDefinition.Normal`,
`Ray.Direction` and `XLine.Direction` previously assigned the output of
`Vector3.Normalize` before deciding whether it was a usable direction. A zero,
very small, or nonfinite input could silently leave NaN components. A sufficiently
large finite input could throw after replacing an existing valid value with zero.
Both outcomes violated the existing normalized-direction property contract.

The setters now prepare a finite, normalized candidate before assigning their
private field. The 2D and 3D RAY/XLINE constructors use the same validation.

| Input | New behavior |
| --- | --- |
| Finite, exactly nonzero vector | Normalize and assign, including nonzero subnormals and very large finite values |
| All-zero vector, including any signed-zero combination | Throw `ArgumentException` before changing state |
| Any NaN or infinite component | Throw `ArgumentOutOfRangeException` before changing state |
| Previously normalized valid vector | Preserve its exact components and normalized flag |

Setters report parameter `value`; constructors report `direction`. The
validation is independent of the caller-configurable `MathHelper.Epsilon`.
For a 2D constructor only the supplied two components are relevant: a zero XY
projection is not a valid 2D direction.

A failed assignment preserves the prior direction bits and proxy graphics.
Successful setters keep their previous proxy-graphics policy; this task does
**not** add automatic proxy invalidation to arbitrary property edits. Derived
source-bound overrides and their admission rules remain unchanged.

## Numerical policy and compatibility

The new internal `Vector3.NormalizeFiniteDirection` helper leaves the public
`Vector3.Normalize` methods unchanged. Finite/nonzero validation occurs even
when a value carries a cached normalized flag, so a cached zero or NaN produced
by the old public utility cannot bypass the new property checks. A cached finite
value must also have a component-based squared length within `2e-15` of one.
Otherwise it is normalized again. The public utility can produce an inaccurate
finite cache after subnormal sum-of-squares rounding; its flag is not proof of
unit length. Valid cached values still preserve their exact component bits.

Ordinary inputs with maximum component magnitude between `1e-150` and `1e150`
use the previous sum-of-squares/inverse-length arithmetic. Outside that range,
each component is divided by the largest magnitude before computing the length.
Division is intentional: forming the reciprocal of a subnormal magnitude can
overflow. These bounds keep the sum of three squares and its inverse length
representable. Already-normalized values retain their exact component bits,
including signed zeros, through repeated assignment and clone initialization.

A normalized component too small for binary64 may underflow to signed zero.
The operation retains finite unit direction within floating-point accuracy;
it does not promise an exact rational ratio, universal correctly rounded square
roots, or exact native AutoCAD numerical behavior. The independent qualification
uses a four-ULP bound per component and a `2e-15` squared-unit-length bound.
Wire checking does not require a particular lexical sign for zero.

This is a behavioral correction. Code that relied on NaN/zero normals, rejection
of tiny but nonzero vectors, or a failed assignment corrupting existing state
must adapt. It is not a general finite-input policy for every entity property.
Malformed in-memory save/adoption/clone guards remain necessary and tested.

```csharp
using System;
using netDxf;
using netDxf.Entities;

var ray = new Ray(Vector3.Zero,
    new Vector3(double.MaxValue, double.MaxValue, double.MaxValue));
var line = new Line();
line.Normal = new Vector3(double.Epsilon, 0, 0); // accepted, +X direction
Vector3 before = line.Normal;
try
{
    line.Normal = new Vector3(double.NaN, 0, 1);
}
catch (ArgumentOutOfRangeException)
{
    // line.Normal still equals before; no partially published normalization.
}
```

## Executed regression and independent verification

The unchanged `DirectionAssignmentTests.cs` SHA-256 is
`c3baed7b33a112f5cb6fd1ad5a1a464e8a6302e58e19450fac31e392fbdf025a`.
Its **1,789** harness cases passed **312 before** the production changes and
**1,789 afterward**. They exercise 16 property contexts, 42 extreme finite
inputs, 19 invalid inputs, constructor admission, exact rollback, clone/repeated
assignment stability, three global epsilon settings, and text/binary save/load.
Sixteen harness cases each check 512 ordinary legacy results bit for bit; these
8,192 operations are not counted as 8,192 additional harness cases.

The full suite retains the 38,715 baseline case identities. Fourteen existing
POLYFACE/legacy-POLYLINE cases previously manufactured malformed objects through
the defective normal setter. They now assert atomic setter rejection, then
explicitly inject the malformed private field using test-only reflection before
running their original save/adoption/clone guards. No case identity or guard
assertion was removed, and production guards were not relaxed.

`tools/verify_direction_assignment.py` independently constructs expected vectors
with 1,100-digit Python Decimal square roots. It examines original physical
DXF tags rather than the independently loaded model's normalized vectors:

- **504 drawings**, with **2,520** RAY/XLINE/LINE/ATTRIB/ATTDEF direction packets,
  across six typed profiles and two transports; zero graph errors or repairs.
- **32,760** actual-record changes, NaNs, omissions, duplicate direction tags and
  missing selected records rejected through the same positive validator.
- **512** independently regenerated seeded bit-vector inputs, **1,536** numerical
  component checks and **1,536** rejected 32-ULP numerical corruptions.
- Exact fixture inventory with separate missing/extra inventory controls.

The verifier checks selected direction fields, origin/endpoints, LINE thickness,
attribute tags/text and layer names. It is not a byte-exact comparator for every
unselected tag. The inputs are synthetic mathematical cases, not new native
producer evidence. The existing independent verifier runner automatically
includes this new script; no CI workflow or baseline gate is removed.

The complete local Debug and Release suites each pass **40,504 unique cases**,
zero failures, with **9,837 DXF files** per configuration. Their result JSON
bytes are identical (SHA-256
`259fbdeb9a4f676e7586798c6006afc507f14e7c78422aeaf674ebe942040cf2`).
All **146 independent verifiers** pass against final Release output, with zero
failures or timeouts. Actual filesystem missing/extra fixture controls also
reject. The 18 coverage-ledger, 17 standard-FIELD and eight text-host checker
unit tests pass, and the historical 297-row matrix remains unchanged.

The [local qualification receipt](direction-assignment-qualification.json)
records source and result fingerprints. Local execution uses actual SDK
8.0.425 / runtime 8.0.31, targeting net8.0, with zero compilation errors and the
existing 561 CS1591 library warnings. Windows and netstandard2.0 require their
own executed CI checks; final remote results are recorded separately in the PR.

## Reproduction

From the repository root, with .NET 8 and the independent checker dependencies:

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python -m pip install -r tools/requirements-independent.txt
python tools/run_independent_verifiers.py artifacts/conformance \
  --output artifacts/direction-independent --jobs 4
```

The harness honors `DXF_TEST_ARTIFACTS` for a separate output directory and
`DXF_TEST_FILTER=direction-assignment/` for a focused run. When overriding the
artifact directory, pass that same directory to the independent verifier.
Offline net8-only builds may use `-p:TargetFrameworks=net8.0`; this is not a
substitute for the netstandard2.0 CI build.

## Primary DXF references and remaining scope

Autodesk documents RAY and XLINE groups 11/21/31 as unit direction vectors in
world coordinates, and ATTDEF groups 210/220/230 as extrusion direction:

- [RAY](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-638B9F01-5D86-408E-A2DE-FA5D6ADBD415.htm)
- [XLINE](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-55080553-34B6-40AA-9EE2-3F3A3A2A5C0A.htm)
- [ATTDEF](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F0EA099B-6F88-4BCC-BEC7-247BA64838A4.htm)
- [Object Coordinate Systems](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-D99F1509-E4E4-47A3-8691-92EA07DC88F5.htm)

The scale handling, exception policy and numerical tolerances above are explicit
library contracts, not claims that Autodesk prescribes this algorithm.
Qualification covers R2000/R2004/R2007/R2010/R2013/R2018 in text and binary.
Historical typed dialects, arbitrary private FIELD/TABLE interpretation and
cache regeneration, dependency-complete import, general document-version
conversion, other geometry transforms, native fonts and native AutoCAD
open/AUDIT/save/reopen remain separate work. The historical coverage matrix is
not relabelled as universal DXF or AutoCAD parity.
