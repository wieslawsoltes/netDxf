# Raw LINE embedded-marker / XData boundary correction

`ReadLineGeometry` previously handled group 101 before checking whether XData
had begun. An `Embedded Object` marker after group 1001 could therefore suppress
all remaining checks and return geometry for a malformed LINE packet. This was
inconsistent with its existing rejection of ordinary data after XData.

Group 101 after an XData application marker now throws `FormatException`, before
entering embedded-tail mode. `WithLineEndpoints` uses the same parser, including
for no-op calls, so malformed semantic packets cannot bypass validation there.
The generic raw document API still retains unknown tags; no recovery mode,
generic framing change or mutation is introduced.

Valid embedded tails beginning before XData are unchanged: they cannot override
core coordinates, valid no-ops preserve original bytes, and real edits reject
because private-data regeneration is unsupported.

The same 72 regression cases pass 18 before / 72 after this one-line fix. The
54 failing-before cases span nine raw families, text and binary input, and
markers after the application name, string value and final integer XData tag.
The 18 positive controls retain the supported embedded-tail behavior. Both read
and no-op edit rejection and original source-byte preservation are asserted.
No previous test body or assertion is relaxed. Complete-suite and platform
results are recorded on the exact task head in its PR.

```sh
DXF_TEST_FILTER=raw-line-xdata/ dotnet run --project tests/netDxf.Conformance -c Release
```

This corrects selected semantic validation, not full historical typed support,
private dependency regeneration or native AutoCAD conformance. No new DXF
profile or general conversion capability is claimed.
