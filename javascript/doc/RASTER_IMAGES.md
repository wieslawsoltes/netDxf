# Detached raster-image and wipeout models

`ImageDefinition`, `ImageDefinitionReactor`, `Image`, and `Wipeout` now have native
JavaScript implementations at the original C# relative paths. The public API
baseline is the pinned .NET-standard build, not the conditional `NET4X` image
file-decoding constructors.

```js
import {
  ImageDefinition, Image, ImageResolutionUnits, Vector3, Matrix3, Wipeout,
} from './javascript/index.js';
const definition = new ImageDefinition(
  'Floor plan', 'plan.png', 1600, 96, 900, 96, ImageResolutionUnits.Inches,
);
const image = new Image(definition, new Vector3(10, 20, 0), 16, 9);
image.Brightness = 60;
image.Contrast = 75;
image.Clipping = true;
image.TransformBy(Matrix3.Identity, new Vector3(5, 0, 0));
const copy = image.Clone();
const mask = new Wipeout(0, 0, 16, 9);
mask.Elevation = 2;
```

The caller supplies the pixel dimensions and resolution. These model constructors
do not open, decode, fetch, or render the referenced image. The portable module
entry remains independent of DOM and Node file APIs. Filename derivation and the
source's first-character-only invalid-path test use the existing explicit support
file host, with the portable POSIX profile as default.

## Behavior retained from the pinned implementation

Image-definition widths and heights are positive Int32 values. Horizontal and
vertical resolution have the source's nonpositive-only guard; an additional finite
restriction is not invented. Resolution-unit editing scales both resolutions only
when the unit value actually changes. Changing to centimeters divides by 2.54;
changing to inches multiplies by 2.54; unitless and unknown values leave them alone.
The constructor records its supplied units without performing that editing step.

Image drawing dimensions are separate from pixel-space clipping coordinates. The
initial rectangle uses `Definition.Width` and `Definition.Height`, not the image's
world-space size. Definition replacement preserves the current clipping object.
Assigning `ClippingBoundary = null` explicitly rebuilds the rectangle from the
current definition. Callback substitution, rejection order, and clone failures
remain observable, including a callback supplying a null definition.

Image axes use value-copy semantics and normalize accepted nonzero vectors. The
source's `Rotation` setter rotates the **existing** U and V vectors incrementally;
it is not an absolute-angle assignment. Brightness, contrast, and fade accept
0–100. Unknown display bits and the separate clipping flag survive cloning.

Transforms keep the three separate change-of-basis products and setter order.
In the source's singular V-axis fallback, the replacement direction is the old
**U** axis, not the old V axis; both implementations retain this distinction.
Image clipping coordinates remain unchanged by placement transforms. Wipeout
transforms, by contrast, update clipping coordinates and elevation, retaining
rectangular versus polygonal boundary type. Clones own the boundary, image
definition, XData graphs, and common entity data and remain detached from document
ownership and handles.

The C#-internal `ImageDefinitionReactor` is mirrored as a small explicitly exported
JavaScript model with immutable `ImageHandle`. Creating that object does not
register a reactor or wire up image-definition ownership. The ambiguous null
`Wipeout` enumerable constructor is selected with
`Wipeout.CreateOverload('System.Collections.Generic.IEnumerable<netDxf.Vector2>', null)`;
ordinary `new Wipeout(null)` selects the clipping-boundary overload.

## Tests and boundaries

The independent raster corpus adds 354 scenarios and 1,881 exact operations using
the actual pinned C# assembly. It includes constructors, argument validation,
resolution conversions, event substitutions, pixel-boundary resets, signed NaNs,
singular and nonuniform transforms, and clipping-type preservation. It extends
the mandatory entity stage to 2,429 scenarios and 16,241 operations and the full
browser corpus to 102,394 comparisons. Fifteen supplemental unit tests cover
additional isolation, host, and warmed-storage behavior; none is counted as a new
original C# test identity. Previous corpora remain intact.

Both Debug and Release entity differentials pass locally. Those results do not
substitute for the commit's Windows and real HTTP-origin browser runs. Registered
image collections, automatic reactor ownership, typed DXF image/WIPEOUT reading
and writing, global raster/wipeout document settings, external image decoding and
rendering, and complete C# API parity remain separate unfinished work.
