# Detached LIGHT and LIGHTLIST models

`Light` and `DxfLightList`/`DxfLightListEntry` mirror the pinned source files. Their
three light-type, three attenuation-type and two shadow-type enum values are
exported from `Light.js` and the package entry. These are authored DXF parameter
models, not a lighting renderer or a private photometric-data interpretation.

```js
import { Light, LightType, DxfLightList, DxfLightListEntry, Vector3, Matrix3 }
  from './javascript/index.js';
const light = new Light();
light.LightType = LightType.Spot;
light.Name = 'Work light';
light.Position = new Vector3(1, 2, 3);
light.Target = Vector3.Zero;
light.Intensity = 2;
light.AttenuationStartLimit = 1;
light.AttenuationEndLimit = 5;
light.TransformBy(Matrix3.Scale(2), new Vector3(10, 0, 0));
const list = new DxfLightList(42); // Raw stored version, not a semantic version.
list.Entries.Add(new DxfLightListEntry(light, 'Independent stored alias'));
```

Position and target are independently copied finite WCS points. Nonnegative
finite intensities, distances and angles preserve signed zero and dormant values.
Hotspot and falloff angles are not normalized, limited to 360 degrees, or coupled
to one another. Shadow map size/softness retain valid zero values. Name editing
rejects null and CR/LF/NUL without modifying existing text; unlike LIGHTLIST entry
names, the LIGHT model itself does not reject isolated UTF-16 surrogate units.

Transforms accept finite nonsingular similarity frames, including reflections.
The stable-length calculation retains tiny and large scales without unnecessarily
squaring their unscaled components. Source-defined relative uniformity and
orthogonality checks use 1e-10; this is the original API's acceptance condition,
not a tolerance in the independent exact-bit comparator. Nonuniform scale,
shear, nonfinite translation, or result overflow is rejected before mutating
position, target or either attenuation limit. Common Normal, proxy data,
intensity, angles and shadow settings remain unchanged. Matrix4 overloads retain
the existing base entity adapter's selection of the first three rows.

Cloning owns appearance objects, vector values, XData and common metadata and
leaves handles, owners and reactor lists detached. It does not resolve ownership
against any registered document.

LIGHTLIST versions retain the complete signed Int32 domain. Entries preserve
order, duplicate LIGHT references, and independently stored names (including
empty names). They validate single-line Unicode without resolving an entity name
or attaching an ACAD_LIGHT dictionary automatically. Mutable collection index
validation precedes item validation. Clone shells contain no entries; reference
copying uses the caller's mapping for every entry and requires each result to be
a LIGHT. Failure after copying an earlier entry retains that partial progress,
as does the source method. Registered host checks and DXF-version schema guards
are present, but no registered DxfDocument/ObjectDatabase implementation is
claimed by those structural adapters.

## Verification and remaining boundaries

The new independent corpora execute 308 LIGHT scenarios / 2,392 operations and
41 LIGHTLIST scenarios / 227 operations against the actual unchanged C# assembly.
They pass exact comparisons in local Debug and Release. Coverage includes
constructor/default state, Unicode distinctions, all property guards, small
and large scales, shear/singularity rejection, overflow atomicity, base overloads,
seeded rotations, duplicate references and failed mappings. Existing corpora
remain unchanged: the required entity stage grows to 3,012 scenarios / 19,632
operations and the database-model stage to 849 scenarios / 4,011 operations.
The browser generator includes both additions, totaling 103,826 comparisons.

One complete original test, `light/name/nonmutating-validation`, is registered;
the original LIGHTLIST entry-validation case includes document registration and
therefore is not shortened or counted. Thirteen supplemental tests are separate
from the original-case ledger. The original JavaScript suite passes 2,576 cases
and the supplemental suite 338. Fresh hosted browser and Windows results must
be read independently from the CI run for the published commit.

Typed LIGHT/LIGHTLIST reading and writing, registered collections and ownership,
external/private photometry, original typed IO tests, the earlier numeric
runtime boundaries and complete C# parity remain unfinished. The saved coordinate
verification continuation remains unpublished because its shared verifier-file
upload was blocked. This batch does not alter or partially activate that file.
