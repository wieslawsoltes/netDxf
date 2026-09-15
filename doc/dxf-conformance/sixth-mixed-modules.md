# SECTION, TABLESTYLE, SUN and FIELD coexistence

`SixthMixedModuleTests.cs` qualifies the four stored families in one document. The matrix contains 18 cases and emits 42 mandatory DXF files. Authored graphs run in R2007, R2010, R2013 and R2018, in text and binary. R2007 uses a registered VPORT SUN host; later profiles use a registered named VIEW. The overall retained-only layout viewport is outside this matrix and outside the SUN attachment API's registered-host scope.

Each authored graph contains a SECTION and its reciprocal typed settings, a stored TABLESTYLE with three exact STYLE bindings and an unrelated private group7, and a SUN with an owned extension dictionary and aliased XRECORD. A stored FIELD owns a second FIELD and references the SECTION, settings, SUN, TABLESTYLE and an external LINE. Repeated and numeric-null references retain their order. FIELD code deliberately splits an encoded character across string chunks and includes a protected literal escape; its evaluator and caches remain inert.

The settings also reference the FIELD, TABLESTYLE and original SUN. The SUN's metadata links its owner and itself alongside those external objects. This makes the source/reference cycles and clone mappings observable across families. Tests check removal and immutable-container clone refusals before any allocation or membership changes, then clone the supported SECTION and SUN subgraphs. Internal identities remap, external identities remain exact, and erasing the copies clears only their owned objects and the copied SUN slot. Renaming the bound STYLE changes the public TABLESTYLE references while leaving its private packet and FIELD code/cache unchanged. Profile conversion rejects before writing any bytes.

The five authored output stages are `original`, `renamed`, `cloned`, `erased` and `context`. The context inputs use padded lowercase FIELD references and a private host group361 decoy. The public relationship must remain bound to its exact retained source target. The writer's omission of an unmodeled host-private control packet is not claimed as private host-payload preservation.

Two further outputs load the complete pinned `tests/fixtures/section/LiveSection1.dxf.gz` R2018 drawing and add explicitly authored SUN, FIELD and TABLESTYLE carriers. The source drawing is not extracted, its handles are not relocated, and its existing SECTION228, settings22A, TABLESTYLE87 and CELLSTYLEMAP remain in place. The verifier checks their original ordered subclass bodies, the SECTION proxy bytes and common owner, and the native TABLESTYLE extension/map relationship against the unchanged pinned source. New synthetic carriers do not establish native producer acceptance for their authored values. No complete-drawing byte identity, FIELD evaluation, section generation, solar calculation, rendering or native CAD execution is claimed.

Run the independent gate with:

```sh
python tools/verify_sixth_mixed.py artifacts/conformance
```

The gate requires the complete 42-file inventory. It inspects actual ordered packets, CLASS counts, private/public resource scope, reciprocal ownership, aliases, clone identity disjointness and physical removal of five cloned owned identities. It independently compares eight native bodies and performs unmodified ancillary audits. Six deliberate copies of actual output packets corrupt a FIELD child owner, FIELD external target, SUN owner, TABLESTYLE private name, settings source or cloned owned inventory; every corruption must be detected.
