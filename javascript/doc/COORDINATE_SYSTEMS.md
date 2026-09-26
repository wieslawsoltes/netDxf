# Coordinate systems, named views and viewport configurations

This increment mirrors `UCS`, `UcsOrthographicBase`, `UcsRelationships`, `View`,
`ViewUcs`, `View.LiveSection` and `VPort` at the original C# paths. It builds on
`994928bf120ea2a590124179c43f60b7dd4b78e8` without changing original C# sources,
fixtures, the generated geometry implementation or the reference-math backend.
These are detached models and explicit internal host adapters, not a complete
registered `DxfDocument`, VPORT collection, viewport entity or typed DXF codec.

```js
import {
  UCS, View, ViewUcs, VPort, Vector3, CoordinateSystem,
} from './javascript/index.js';

const frame = new UCS('Machine', new Vector3(10, 20, 0),
  Vector3.UnitY, Vector3.Negate(Vector3.UnitX));
frame.Elevation = 2;
const world = frame.Transform(new Vector3(1, 0, 0),
  CoordinateSystem.Object, CoordinateSystem.World); // (10, 21, 0)

const view = new View('Inspection');
view.Target = world;
view.Width = 40;
view.Height = 25;
view.Ucs = Object.assign(new ViewUcs(), { NamedUcs: frame });
const copy = view.Clone('Inspection copy');
// copy.Ucs is independent, but copy.Ucs.NamedUcs still references frame.

const configuration = VPort.Active; // A fresh reserved *Active record each time.
configuration.ViewHeight = 25;
configuration.ViewAspectRatio = 1.6;
configuration.NamedUcs = frame;
```

## UCS state and transformations

Origins and axes have value-copy semantics. Factories preserve the source's
normalization and evaluation order, including raw point-on-plane semantics and
the **radians** argument of `FromNormal` with rotation. Elevation is independent
of origin. An enumerable transform snapshots its transformation matrix and origin
before enumerating user input. Unrecognized coordinate-system combinations retain
the source's value-copy behavior rather than inventing another transform.

`OrthographicOrigins` is a stable live read-only dictionary adapter. It returns
copied vectors and supports `Count`, `ContainsKey`, `get_Item`, `TryGetValue`,
keys, values and iteration as `{ Key, Value }` pairs. Mutating IDictionary methods
reject with `NotSupportedException`. Use `SetOrthographicOrigin` and
`RemoveOrthographicOrigin` to author values. Internal `$set`/`$remove` hooks are
not supported authoring APIs. Deleted dictionary slots are reused in the pinned
runtime's order. Insertion invalidates enumerators; value replacement and removal
retain the .NET 8 version behavior.

`TryGetOrthographicOrigin(type, output)` writes to `output.value`, including a
fresh `Vector3.Zero` on a missing key. Base-UCS metadata preserves omitted versus
explicit-null group-346 state. A clone owns axes, overrides and XData but retains
the original base-UCS reference, including self/cyclic references; it does not
silently import or recursively clone that dependency.

## VIEW and VPORT behavior

Named views expose target/direction, center, height/width, lens length, clipping
planes, rotation, view mode, flags, render mode and camera-plottable state. Legacy
`Camera`, `Fov` and `Viewmode` aliases address the same underlying properties.
`IsPaperSpace` changes only its flag bit. Direction values must be finite and
exactly nonzero, but are **not** normalized: even a finite subnormal vector is
retained. Scalar guards, parameter names and signed-zero state follow the source;
view rotation is not silently normalized.

Viewport configurations include grid/snap settings, screen corners, view target
and direction, aspect ratio, lens/clipping, twist, render and display modes, UCS
vectors, elevation and named/base references. The configuration is not the
separate `Entities.Viewport` object. A viewport's equality and hash identity are
reference-based, unlike ordinary table-name equality. Hashes remain stable over
rename; their numeric values are intentionally process-specific, not claimed to
match a different .NET process. `*Active` recognition ignores case and surrounding
.NET whitespace, creates a reserved record, and does not make separate active
instances equal.

A `ViewUcs` bundle can belong to only one view. Assigning it twice to the same
view is a no-op; assigning it elsewhere fails before detaching the current bundle.
Detachment clears its internal owner so it can be reused. Clone creates a distinct
bundle attached to the cloned view, while retaining named/base UCS object identity.
Base-reference/orthographic consistency is checked by the original explicit
validation path, not by an invented earlier setter guard.

Null SUN with a retained presence bit survives cloning. Non-null owned SUN data
rejects the shallow table clone before clone-name validation, because the required
ownership-subtree operation is not implemented here. Live-section metadata retains
omitted versus explicitly null state, clear, clone and validation order. The
non-null `Section` entity was added in a later checkpoint; the registered live-section graph remains unqualified;
structural host checks are supplemental tests, not native document qualification.

## Ownership adapters and API boundary

The internal UCS adapter checks existing registered targets and maintains the
host's reference counts. View/viewport name callbacks preserve validation and
assignment order and re-read ownership after user callbacks. These helpers do
not construct document collections, assign handles, canonicalize registered
names, import dependencies, register SUN/live-section data, or read/write DXF.
Properties and methods corresponding to C# internal hooks remain implementation
adapters, not a claim that the missing registered workflows are implemented.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/COORDINATE_SYSTEMS.md). Current published scope is maintained in the [README](../README.md).
