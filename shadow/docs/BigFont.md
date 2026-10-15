# Explicit BIGFONT resources and paired typography

The SHX consumer reads regular and extended BIGFONT SHP definitions and indexed `AutoCAD-86 bigfont 1.0` binaries. Unicode interpretation remains a separate, explicit host resource: lead-byte declarations alone do not identify a code page. There is no filename-based encoding guess, font installation, disk lookup or network lookup.

## Parsing and execution

SHP begins with `*BIGFONT nchars,nranges,start,end,...`. The character count is an allocation hint, not an assertion that the glyph table has exactly that many definitions. Lead-byte intervals are validated, copied and canonicalized; overlaps and out-of-byte-range values are rejected. Regular metadata has height, descender, orientation and terminator. Extended metadata has height, zero, orientation, nominal width and terminator.

The binary decoder reads the eight-byte sparse directory entries `(word id, word size, dword absolute offset)` and declared lead-byte ranges. Empty slots are not glyphs. All ranges are checked before decoding; records cannot point into the directory, overlap or duplicate IDs. Each referenced record must terminate correctly and fit the input budget. SHAPES and UNIFONT retain their previous codecs, including their existing subshape word ordering. BIGFONT escaped subshape words use their own packed-byte ordering.

Extended opcode `7,0` places a primitive using its base point and independent width/height relative to nominal BIGFONT dimensions. The VM shares recursion, instruction and curve quotas across primitive calls. Each placement has local position/scale/stack state, inherits pen state, and restores caller cursor/pen/scale after the placed primitive. Circular primitive arcs become analytic ellipses under nonuniform placement; bounds use ellipse parameter extrema. Glyphs still provide explicit normal or vertical advances. Vertical paragraph layout requires downward advancement and return to the column baseline rather than guessing an advance for an arbitrary shape program.

`ShxBigFontInfo.Decode` tokenizes raw bytes in one pass into immutable `ShxEncodedCharacter` entries. A declared lead byte consumes its following trail byte even when that byte is normally a delimiter. Truncated pairs and character-budget overflow fail explicitly. These packed-byte tokens are not Unicode characters.

## Host-selected Unicode interpretation

```csharp
var big = ShxFontReader.ReadBinary(authorizedBytes);
var mapping = new ShxBigFontMap(new[]
{
    new KeyValuePair<int, int>(unicodeScalar, packedShapeNumber)
});
var catalog = new ShxFontCatalog();
catalog.Register("primary.shx", primaryFont);
catalog.RegisterBigFont("companion.shx", big, mapping);
var projector = new ShxEntityProjector(catalog.Current); // Pins font AND mapping.
```

`ShxBigFontMap.FromEncoding(big, hostSelectedEncoding)` can derive the mapping for declared double-byte glyphs from a host-provided `System.Text.Encoding`. It uses exception fallbacks and exact byte roundtrips; invalid sequences and multi-scalar decodings are excluded, and ambiguous Unicode aliases are rejected. It never globally registers encoding providers or chooses a code page from a resource filename. Explicit maps also support supplementary Unicode scalars and symbols outside standard code pages.

`RegisterBigFont` publishes a font and map atomically as one catalog revision. A map-only replacement invalidates live scenes. Pinned font sets preserve both the bytes' decoded instructions and the old character interpretation. Exact-path font aliases cannot accidentally combine with an unrelated basename mapping. Resource removal and replacement by an ordinary font clear the associated mapping.

## Shared typography

TEXT, MTEXT, attributes and complex linetype labels read the primary style's `BigFont` reference. Both primary and companion resources must be explicitly registered. Mapped companion glyphs are normalized into primary-font units before the shared measurement and painting paths use them. Different cap heights therefore do not change the intended text height or cause measurement/drawing disagreement. Unmapped characters keep primary-font lookup; mapped missing symbols draw an explicit diagnosed box. Unknown font/mapping bindings are rejected rather than substituted with an arbitrarily chosen code page.

Horizontal rich text continues to use the existing paragraph, scope, rule, width, oblique and tracking layout. Vertical text uses the already implemented downward glyph execution and leftward column composition, now with the same companion mapping and independent metrics. Complex linetype providers reuse this pipeline and its resource revision. Output remains neutral presentation geometry consumed by both Skia and SVG; it does not add topology edges.

## Limits and qualification

Input byte, symbol, instruction, call-stack, position-stack, character, curve and glyph-cache quotas remain active. Mapping construction defaults to 65,536 entries. Normalized companion glyph caches are weakly keyed by font and bounded by the existing per-font glyph quota. Tests use original synthetic instructions and generated indexed containers, not redistributed font files. Regression coverage includes sparse directories, truncations, record overlap, Unicode mapping, primitive placement, normal/vertical metrics, cache/resource pinning, source capture/save/reload and both renderers.

This is a strict supported-format implementation, not certification against every historical compiler variant or proprietary font. Unsupported directory layouts, malformed programs and typography outside the documented shared layout contract remain explicit errors. Hosts must select the correct encoding or provide an authoritative map; a font's range header cannot supply missing encoding knowledge. Studio's existing resource loader can decode BIGFONT bytes; selecting its Unicode map remains an explicit host/catalog operation.

Specifications: Autodesk [Defining a Big Font](https://help.autodesk.com/cloudhelp/2019/ENU/AutoCAD-MAC-Customization/files/GUID-DE0CCC57-AC55-4CDC-887E-730FC90364E4.htm) and [Extended Big Font](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-Customization/files/GUID-00ED0CC6-A4BE-4591-93FA-598CC40AA43D.htm). Indexed binary layout was cross-checked against [OpenCADStudio's parser at a fixed revision](https://github.com/HakanSeven12/OpenCADStudio/blob/843cbcd6071843073a83208c2b333b66d9c90752/src/scene/text/shx.rs). The implementation does not adopt that parser's filename heuristics or silent malformed-record recovery.
