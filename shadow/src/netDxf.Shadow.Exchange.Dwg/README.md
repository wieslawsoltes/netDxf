# Explicit DWG interchange

This optional library implements a managed ACadSharp 3.8.0 codec boundary for
netDxf hosts. Conversion is detached from EditorSession and cannot modify a
live source document. The Studio integration stages the result before loading
it and marks an export saved only after encoding succeeds.

`DrawingExchange.Convert` converts DXF to DWG or DWG to DXF. It requires a
format declaration matching the signature, a bounded input and an explicitly
supported output version. It reads with failsafe recovery disabled and DWG CRC
checking enabled. Codec warnings, errors and not-implemented notices are fatal;
None/informational notices are retained. The emitted stream is read back before
return, then a DXF transport is verified with the actual netDxf reader.

The adapter compares per-block entity type counts and named definition
inventories across the source, encoded output, and netDxf load. It rejects
unsupported/proxy entity families instead of implicitly importing an empty
placeholder. These structural checks detect missing entities and definitions;
**they do not prove that every geometric, header, extension-dictionary, xdata,
style or proprietary DWG property survived unchanged**. Native regressions
check concrete primitive coordinates, text, layer and shared INSERT semantics.
Do not describe this as general lossless or certified AutoCAD interchange.

The library bounds input/output bytes, logical entities, blocks and diagnostics.
Cancellation is checked during stream access and validation. It is not a hard
process memory/CPU sandbox for third-party decompression. The existing Studio
host remains loopback-only; use process isolation before adapting the codec to
untrusted multi-tenant servers. No native converter executable, shell command,
cloud upload or implicit external-reference resource access is used.

Declared DWG write profiles: AC1014, AC1015, AC1018, AC1024, AC1027, AC1032.
AC1021/R2007 is read-only in this codec. Profiles remain subject to actual
content admission and codec diagnostics; a version label is not a promise to
round-trip every entity supported by that release.

The linked codec has an MIT license; included upstream notices are retained.
No font files or proprietary Autodesk components are distributed here.
