# Mixed object lifecycle scenarios

`FourthMixedModuleTests` combines the public stored object modules with ownership, cloning, APPID renaming and terminal erasure. Twelve scenarios cover all six admitted typed profiles in ASCII and binary output. Each scenario writes an original drawing, a mapped copy, an erased drawing and an explicit source/destination handle map: 36 drawings and 12 maps in total.

The graph contains ordered LAYER_FILTER names with case distinctions, exact duplicates, Unicode and a literal DXF Unicode escape; an OBJECT_PTR with an extension dictionary; an exact SPATIAL_INDEX timestamp; and a VBA_PROJECT with 132 bytes stored in five chunks, including three empty chunks. R2007 and later additionally contain a LIGHTLIST with three references to the same external LIGHT, independent stored names and an explicit stored version. The graph also includes hard and soft dictionary aliases, internal and external reactors, XData 1005 references, repeated/null IDBUFFER entries and an XRECORD whose semantic pointers must remap while its arbitrary group 320 value stays unchanged.

Clone destinations contain extra symbol records so copied identities differ from source identities. External object and LIGHT mappings use exact source objects. Renaming the destination APPID to `FOURTH_APP_COPY` changes its dictionary keys and bindings while the original drawing keeps `FOURTH_APP`.

An outside hard pointer first prevents erasure. Rejected erasure preserves identities, owners, registration and allocation state. After removing the blocker, terminal erasure removes the owned closure and aliases, keeps tombstone handles, rejects reattachment and retains both external objects. The last compatible instance of each known object CLASS leaves a zero instance count where that profile writes counts; private CLASS declarations remain governed by separate compatibility tests.

The mandatory [independent verifier](../../tools/verify_fourth_mixed_modules.py) parses every drawing with ezdxf 1.4.4, checks the actual clone maps and complete relevant stored records, and rejects missing or extra expected graph members. It compares spatial and external LIGHT floating-point values by their exact bits. Two corruption controls per scenario alter a VBA byte and replace a remapped hard pointer with its old source identity; all 24 controls must fail for their intended record mismatch. All 36 unmodified drawings must audit without errors or repairs.

Seven additional [declared ownership/erasure integration cases](../../tests/netDxf.Conformance/ErasureDeclaredOwnershipTests.cs) guard adoption of terminal objects and declared descendants. Reflection-based corrupt-state cases verify internal consistency checks; they do not qualify a public editable TABLE model.

This evidence covers stored values and graph lifecycle. It does not establish native CAD execution, VBA execution, filter or spatial evaluation, LIGHT rendering, or unknown private dependency semantics.
