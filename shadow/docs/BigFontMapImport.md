# Explicit BIGFONT map import

Upload an authorized BIGFONT SHP/SHX resource with **Load SHX/SHP**, then select **Load BIGFONT map**. No font files are bundled, installed, or opened from a DXF path. Character interpretation is explicit; a filename and BIGFONT lead-byte ranges cannot identify a Unicode code page.

```json
{"schemaVersion":1,"resource":"companion.shx","characters":{"U+03A9":32832,"U+1F600":32833}}
```

The UTF-8 schema is bounded to 4 MiB and 65,536 entries by default. Root fields are exact and required. Unknown/duplicate fields, duplicate Unicode aliases, invalid scalars, nonintegral/out-of-range packed words and trailing values reject. Keys are `U+` followed by four to six hexadecimal digits. A leading UTF-8 BOM is accepted. Serialization uses canonical field order and ascending Unicode keys. Resource is a registered `.shx` basename, never a path. Numeric mappings are words, not glyph names.

`ShxBigFontMapFile.Read` and `Write` are portable and cancellable. `ShxFontCatalog.ImportBigFontMap` binds only to an existing BIGFONT alias and preflights every high byte against that resource's declared ranges before publication. Missing glyph programs are retained for rendering diagnostics instead of silently changing character interpretation. `ShxBigFontMapFile.Equivalent` compares structural contents; equivalent imports retain the exact resource-set identity and revision. Changed maps atomically replace font/map bindings; old pinned sets remain immutable.

Studio's bounded `/api/bigfont-map?revision=...` POST validates its complete document/workspace/view receipt before reading and again before publication. It refuses branch mutation. A changed map advances font revision and view epoch, clears presentation selection, and publishes through the existing SSE feed without editing source or undo history. Equivalent imports are exact no-ops. The upload module mirrors the existing font control's disabled state and never retries mutations.

This increment reconstructs the previously unpublished map-import feature from the verified upstream APIs because the earlier local archive was unreadable in the continuation environment. It is not claimed byte-identical to the prior patch. The prior artifact remains untouched. New hosted tests, not the prior local test counts, qualify this implementation.

Run the existing advanced verification executable and HTTP/SSE suite from the repository root. Map parser, canonicalization, malformed/truncated input, quotas, catalog atomicity, revision/pinning and Studio no-op/history tests are registered in that executable. No workflow or existing gate is changed.
