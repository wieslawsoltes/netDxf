# STYLE flags, last height, and font data

`TextStyle` and `ShapeStyle` retain all stored group70 flags, all group71 text
generation bits, and optional group42 (`LastHeight`). `LastHeight = null` omits
that field; zero is an explicit value. The stored last height is independent of
fixed `TextStyle.Height` and shape geometry. Its value must be finite; signed
values are preserved without interpreting them as a geometry instruction.

`TextStyle.Flags` uses `TextStyleFlags`, including vertical, external-reference,
resolved, and previously-referenced bits. Unknown bits survive reads, clones,
and writes. Setting the shape discriminator on `TextStyle`, or clearing it on
`ShapeStyle`, rejects the assignment. `IsVertical`, `IsBackward`, and
`IsUpsideDown` change only their corresponding bit. The remaining group70/71 bits
are retained; this library does not infer xref status or recompute historical
reference flags. Shape group71/42 values are stored without rendering meaning.

```csharp
var style = new TextStyle("Survey", "Arial.ttf")
{
    LastHeight = 2.75,
    Flags = TextStyleFlags.Referenced,
    ExtendedFontData = new TextStyleFontData("Arial", 0x03000022)
};
style.FontStyle = FontStyle.Bold; // changes only bits 24 and 25
// Both the file name and the extended font data remain present.
```

`TextStyle.ExtendedFontData` exposes an immutable snapshot of the canonical ACAD
XData prefix: the first record is group1000 (family name), and the second is
group1071 (complete signed 32-bit font data). Only the documented bold and italic
bits are interpreted. The other bits, including pitch/family/charset data, are
preserved exactly. An empty family and zero flags are a present packet.
`FontFamilyName` and `FontStyle` project this prefix; direct edits to the prefix
through `XData` are immediately visible. Font-file names and extended data may
coexist, including empty and extensionless file names read from DXF.

Setting `ExtendedFontData` replaces a recognized prefix or prepends one to
unrelated ACAD data. Other records keep their values and order. Setting it to
null removes only a recognized prefix, leaving the ACAD entry and unrelated
records intact. A noncanonical prefix, such as an application-defined control
list, is not searched for a later string/integer pair. If removing a font prefix
would expose another immediate string/integer pair, removal fails before
mutation: that suffix is indistinguishable from a new font prefix on reload.
The caller must explicitly restructure that ambiguous ACAD payload. This guard
also applies when the legacy `FontFile` setter requests removal of font data.

The font-family constructor and explicit property setters update this XData
model when called. **Saving does not create, clear, replace, or alter STYLE
XData records.** Existing legacy property semantics remain: assigning
`FontFamilyName` clears font file names and selects regular font style;
assigning `FontFile` clears the recognized font prefix and BigFont.
`ExtendedFontData` provides the independent stored-data operation. Cloning
preserves full flags, optional height, both file names including BigFont, and
all XData with separate mutable storage.

Font file names, family data, and unrelated STYLE XData strings
preserve literal DXF-looking backslashes, valid Unicode including supplementary
characters, and NUL/CR/LF through the existing Unicode escape convention.
Unpaired UTF-16 surrogates reject saving before output or document setup, in
both transports and every supported profile. Repairing the string allows a
retry using the same graph. This module does not change string handling in
unrelated tables or entities.

The reader bounds its scalar packet to `AcDbTextStyleTableRecord`, accepts
scalar reordering, and rejects duplicate modeled scalar fields, unexpected
subclasses, or XData tags without an application registry. The existing
normalization of fixed height, width factor and oblique angle is retained.
Shape STYLE group4 is outside this module: the published shape-file definition
assigns meaning only to its discriminator and primary font file. Shape XData
is preserved through the existing public collection.

## Qualification

The fields and font prefix are qualified for all six admitted profiles:
R2000, R2004, R2007, R2010, R2013, and R2018, in text and binary transports.
`tests/fixtures/style-fidelity/generate.py` independently produces twelve
fixtures using ezdxf1.4.4. They contain rich font data with unknown and lower
font bits, explicit empty data, a nonfont ACAD prefix, independent XData,
text/MTEXT references, a following POINT, a shape definition, and optional
last-height cases. The generator records source hashes in `manifest.json`.
Its explicit low-level removal of one group42 qualifies absent data rather than
relying on the producer's default-value export policy.

`TextStyleFidelityTests.cs` exercises 291 cases: authored and independent inputs,
two save/reload cycles with transport changes, clones, boolean bit projections,
malformed packets, font-prefix boundaries, save-time identity and reference
stability, literal/control/supplementary Unicode, and failed-save repair/retry.

`python tools/verify_text_style_fidelity.py <artifacts>` requires exactly
72 emitted drawings: 36 authored optional-height cases, 24 independent producer
cycles, and twelve literal-string cases. It checks exact native tags and full
XData sequences, file names, source/copy state, declared profile and transport,
independent font-bit interpretation and entity references, with zero ezdxf
audit errors or repairs. It does not use a netDxf reader as its oracle.

This is stored STYLE fidelity. It does not embed font files, verify installed
fonts, resolve external font substitution, or claim native CAD rendering
qualification. DIMSTYLE and entity-specific text layout remain separate work.

## Primary references

- [Autodesk STYLE group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-EF68AF7C-13EF-45A1-8175-ED6CE66C8FC9.htm)
  defines groups70/71/42, font file fields and complete1071 font data.
- [ezdxf STYLE internals](https://ezdxf.readthedocs.io/en/stable/dxfinternals/tables/style_table.html)
  documents the R2000 packet and canonical ACAD1000/1071 prefix, including italic
  `0x01000000` and bold `0x02000000`.
- [ezdxf1.4.4 textstyle implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/textstyle.py)
  independently implements the prefix interpretation, permits empty family/file
  data, and supplies the producer schema. Its warning that extended font data is
  not a reliable font-resolution source is consistent with this storage contract.
