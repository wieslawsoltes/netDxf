# Detached underlay definitions and placement

The JavaScript port now mirrors `UnderlayDefinition`, `UnderlayPdfDefinition`,
`UnderlayDgnDefinition`, `UnderlayDwfDefinition`, and `Underlay` at their original
relative source paths. PDF page and DGN layout metadata, file-extension validation,
name derivation, reference queries, event substitution, clipping boundaries,
placement, transforms, and detached cloning follow the pinned C# implementation.

```js
import { UnderlayPdfDefinition, Underlay, Vector3, Matrix3 } from './javascript/index.js';
const definition = new UnderlayPdfDefinition('Floor plan', 'floor-plan.pdf');
definition.Page = '2';
const underlay = new Underlay(definition, new Vector3(10, 20, 0), 2);
underlay.Contrast = 75;
underlay.Fade = 20;
underlay.TransformBy(Matrix3.Identity, new Vector3(5, 0, 0));
const independentCopy = underlay.Clone();
```

No external document is opened by these model APIs. They do not parse or render
PDF, DGN, or DWF contents. They do not implement the still-missing registered
underlay collections, ownership reconciliation, typed `DxfDocument` serialization,
or external-reference resolution.

## Preserved distinctions

Definition constructors intentionally use the source's unchecked/trimmed name
path, whereas later public renaming uses the ordinary validated table-name path.
File extensions use ordinal-ignore-case matching. Invalid-path characters and
filename separators follow the explicit support-file host; portable imports use
POSIX-style separators. The source checks an invalid character only at position
zero, and the port retains that condition rather than silently broadening it.
PDF `Page = null` becomes an empty string; DGN `Layout = null` remains null.

The scalar constructor rejects nonpositive scales. The vector scale setter uses
`MathHelper.IsZero` on each component, permits negative nonzero components, and
therefore has intentionally different behavior for tiny values. Rotation editing
normalizes through the existing exact-math runtime and stores binary64 bits.
Contrast accepts 20–100 and fade 0–80. Unknown display flag bits are retained.

Definition-change callbacks may substitute a different definition. The stored
reference uses that substitution, while the source's entity code selection uses
the originally proposed definition's type. Tests retain this observable behavior,
including callback exceptions, null substitutions, and subsequent clone effects.

Transforms preserve the source's operation and assignment order, including its
three change-of-basis matrix products, diagonal-product sign test, zero-scale
fallback, and partial-state behavior on rejection. Clipping coordinates are not
silently transformed. Clones own their definitions, clipping data, common proxy
buffers, and both XData graphs, without copying a document owner or entity handle.

## Qualification

The supplemental underlay corpus contains 510 scenarios and 2,403 operations;
expected results are produced by the unmodified pinned C# assembly. It extends
the mandatory entity differential to 2,075 scenarios and 14,360 operations and
is also included in the mandatory browser corpus (102,040 total comparisons).
Eight supplemental unit tests cover the host boundary, validation order, event
behavior, copies and transformation. These tests do not increase the original
C#-test completion count. Existing scenarios and strict bit comparisons remain.

The preceding text-angle storage fix adds two further supplemental tests. The
new underlay model adds five source mirrors, not a claim of typed-file or full
C# API parity. Fresh hosted-browser and Windows qualification must be read from
the CI run for the published commit; local differential success alone does not
establish those results.
