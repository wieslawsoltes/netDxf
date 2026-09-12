# Independent binary XData clones

`XData.Clone()` previously created new record objects but reused their mutable byte-array values. Editing a copied entity, table item, or nested block entity could therefore modify the original object's binary XData, and both could serialize the unintended mutation.

Binary values now receive independent arrays. Scalar record values retain their types and values; application registries and record lists retain the existing clone behavior. The constructor and `Value` accessor ownership contracts are unchanged. This fix is about clone isolation, not automatic remapping of database handles or arbitrary object graphs.

## Regressions

20 registered cases exercise empty/one-byte/127-byte arrays, source-to-clone and clone-to-source mutations, sibling clones, repeated source buffers, all declared scalar XData codes, entity/layer/nested-block clones, and distinct source/copy payloads after serialization in all six admitted DXF formats and both transports.

Against the preceding binary-chunk baseline, the new tests produced **316 passed / 19 failed** before the fix. The corrected signed library produced **335 passed / 0 failed**, in both local Debug and Release. Cross-platform final-head CI remains the merge gate.

The existing maximum binary XData record length of 127 bytes is unchanged. Autodesk's XData representation is documented at:
https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-A2A628B0-3699-4740-A215-C560E7242F63.htm

This resolves the mutable-binary-cloning finding in the source-pinned version/feature matrix; it does not imply complete XData reference remapping or full DXF conformance.
