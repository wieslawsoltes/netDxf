# Explicit BIGFONT map import

Upload an authorized BIGFONT SHP/SHX resource with **Load SHX/SHP**, then select **Load BIGFONT map**. No font files are bundled, installed, or opened from a DXF path. Character interpretation is explicit; a filename and BIGFONT lead-byte ranges cannot identify a Unicode code page.

```json
{"schemaVersion":1,"resource":"companion.shx","characters":{"U+03A9":32832,"U+1F600":32833}}
```

The UTF-8 schema is bounded to 4 MiB and 65,536 entries by default. Root fields are exact and required. Unknown/duplicate fields, duplicate Unicode aliases, invalid scalars, nonintegral/out-of-range packed words and trailing values reject. Keys are `U+` followed by four to six hexadecimal digits. A leading UTF-8 BOM is accepted. Serialization uses canonical field order and ascending Unicode keys. Resource is a registered `.shx` basename, never a path. Numeric mappings are words, not glyph names.

`ShxBigFontMapFile.ReadStrict` and `Write` are portable and cancellable. `ShxFontCatalog.ImportBigFontMap` binds only to an existing BIGFONT alias and preflights every high byte against that resource's declared ranges before publication. Missing glyph programs are retained for rendering diagnostics instead of silently changing character interpretation. `ShxBigFontMapFile.Equivalent` compares structural contents; equivalent imports retain the exact resource-set identity and revision. Changed maps atomically replace font/map bindings; old pinned sets remain immutable.

Studio's bounded `/api/bigfont-map?revision=...` POST validates its complete document/workspace/view receipt before reading and again before publication. It refuses branch mutation. A changed map advances font revision and view epoch, clears presentation selection, and publishes through the existing SSE feed without editing source or undo history. Equivalent imports are exact no-ops. The upload module mirrors the existing font control's disabled state and never retries mutations.

The existing `Read(ReadOnlyMemory<byte>, maximumBytes, maximumCharacters, cancellationToken)` metadata API remains compatible with host aliases and one-to-six hexadecimal digit keys. `ReadStrict(ReadOnlySpan<byte>, ShxBigFontMapReadOptions, cancellationToken)` is the explicit import boundary; Studio uses it. Both entry points share one bounded UTF-8 parser. The original `Write()` and `Studio.LoadBigFontMap(long, byte[])` entry points remain available alongside cancellable overloads.

The diagnostics workspace retains its existing integrated upload handler; the import is not registered a second time and the CAD landing page is not replaced. Both the previously merged map tests and this PR's strict parser, import, pinning and no-op tests are registered in the advanced verification executable. No workflow or existing gate is changed.
