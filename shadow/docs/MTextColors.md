# MTEXT color interpretation

The renderer decodes the color values authored by the netDxf MTEXT writer without
retaining `AciColor` instances or referencing the mutable source adapter.

## Indexed controls

`\C1;` through `\C255;` resolve to the repository's complete `AciColor` palette.
The detached constant table preserves the source library's exact bytes, including
historical entries; it is not a recomputed HSV approximation or a claim that every
CAD application's configurable palette is identical. `\C0;` and `\C256;` retain
the existing inherited-paint contract. Out-of-range indices remain diagnosed as
`text.palette`; they do not index outside the table.

The palette is static unmanaged constant data accessed through a read-only span.
Lookup is bounded and allocates neither source color objects nor arrays.

## True-color controls

The MTEXT writer in `netDxf/Entities/MText.cs` writes the numeric value with the
red byte first: `R | (G << 8) | (B << 16)`, or numeric `0x00BBGGRR`. The scene paint
uses `0xAARRGGBB`. Explicit shifts convert between these conventions independently
of machine endianness. For example, `\c255;` is red and `\c16711680;` is blue.
This rule applies to the MTEXT inline control, not to every numeric DXF color field.

Invalid or overflowing true-color arguments stay visible with `text.format`
diagnostics and do not change the current style. Formatting braces restore the
previous immutable style. Source ranges and input/inline budgets are unchanged.

## Verification

`MTextColorVerification` adds eight executable cases covering all 255 indexed
colors, the independent source writer for every index and 128 deterministic RGB
colors, explicit unequal-channel fixtures, scope/inheritance behavior, malformed
controls, DXF save/reload, retained snapshots, SVG paints and native PNG pixels.
Existing rendering fixtures retain their expected colors; the two reversed numeric
controls have been corrected rather than reversing their assertions.

Run the original and advanced suites using the commands in `shadow/README.md`.
Generated artifacts include `artifacts/mtext-colors.png` and
`artifacts/mtext-colors.svg`. This change does not alter capture validation policy,
the single-edit allocation limit, workflow gates, or the original netDxf library.
Paragraph typography, field evaluation, configurable palettes and other specialized
CAD rendering features remain separate work.
