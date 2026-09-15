# POLYFACE and PolygonMesh integration checks

Both modules were integrated over PR90 source-identity, HATCH and SECTION_MANAGER changes. Their reader edits merged without discarding either the POLYFACE-specific child grammar or PolygonMesh surface-type handling. The shared POLYLINE, mesh-version, source-identity, HATCH-source and manager regressions were included in the merged run.

Debug and Release each pass **2,239 focused cases**. In each configuration, the POLYFACE output gate passes 96 drawings and 576 decoded corruption controls with zero audit errors or repairs; the PolygonMesh gate passes 116 required outputs, 12 physical corruption controls and two native 14-packet chains. The unchanged manager ambiguity probe passes 8/8 in both. Separate frozen reviewer receipts record 279 POLYFACE cases, 160 PolygonMesh cases, 16 HATCH cases and 8 source-metadata cases per configuration.

The build source revision is 93f3773, also embedded in both library informational versions. The first independent reruns inspected clean checkout 65e7374 after documentation was added; the production and test trees are unchanged. `receipt.json` pins source trees, tested assemblies, gate sources and every retained result. Compressed build logs preserve the 561 existing CS1591 documentation warnings and zero errors per configuration.

This is focused integration evidence. Root's final PR91 qualification supplies the complete conformance run and all target-framework builds. The two mesh ledger rows retain their partial status: POLYFACE arbitrary child metadata and original child identities remain outside this geometry change; PolygonMesh generated samples and child identities are regenerated. Native PolygonMesh evidence covers ordinary grids, while smooth-grid evidence is synthetic.
