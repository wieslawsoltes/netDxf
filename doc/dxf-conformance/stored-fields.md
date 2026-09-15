# Stored FIELD objects

`DxfStoredField` retains a loaded `FIELD` object's immutable subclass payload in its source document and DXF profile. It exposes the proven leading evaluator text, ordered child fields and ordered object references. Evaluator code, numeric flags, data values, cached results and format strings are preserved without execution or recomputation. The API has no public constructor or field-expression authoring surface.

## Evidence and scope

The [Autodesk FIELD reference](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-51B921F2-16CA-4948-AC75-196198DD1796.htm) describes leading evaluator/code strings, repeated child ownership and object pointers, followed by keyed data/cache packets. Its printed object name is `ACAD_FIELD`; the pinned producer records use `FIELD` with `AcDbField`. Only the observed `FIELD` spelling is projected. `ACAD_FIELD` remains opaque rather than being promoted by an assumed alias.

The source evidence is two complete TS1 drawings published in [LibreDWG commit 34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43](https://github.com/LibreDWG/libredwg/tree/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/test/test-data): one AC1015/R2000 and one AC1032/R2018. Each contains four FIELD objects and a populated FIELDLIST. The R2000 example establishes that an assumed R2004 minimum would reject an actual published packet. The source files are compressed in `tests/fixtures/field-oracle`; their decompressed SHA256 and Git blob identities are pinned in its manifest.

| Source profile | Decompressed SHA256 | FIELD graph |
|---|---|---|
| AC1015 | `d55726ba862a45572a6b72493e3139b809d0a806e7eba6e4cae578a1a4227303` | 14E → 14F and 15E → 15F |
| AC1032 | `ef795fb7c4c9cfc4486ff3cc0254d38cbdb870a326e7e8cccd78b0e046868d14` | 14E → 14F and 15E → 15F |

Parents use evaluator `_text`; children use `AcSm`. The children contain stored sheet-number code and unresolved cached display text. R2018 wraps values in richer ACVALUE packets; R2000 uses a shorter format and an additional group 4. The complete payload preserves those differences. These examples do not establish external sheet-set resolution or working evaluator behavior.

## Public contract

| Member | Meaning |
|---|---|
| `SourceVersion` | Original DXF profile, required for output |
| `Payload` | Immutable ordered typed tags from `AcDbField` through the end of subclass data, excluding XData |
| `EvaluatorId` | Decoded leading group 1 |
| `FieldCode` | Decoded group 2 plus ordered group 3 chunks, joined before Unicode escape decoding |
| `Children` | Ordered non-null, unique FIELD targets in the leading 360 ownership sequence |
| `ReferencedObjects` | Ordered leading 331 targets, including repeated targets and null slots |
| `References` | Non-null exposed semantic payload dependencies; arbitrary 320–329 values are excluded |

The recognized leading header is evaluator/code, child count and sequence, then object-reference count and sequence. Count validation never allocates from the declared count. Later reused codes belong to stored data/cache packets and receive no scalar projection. An unfamiliar leading prefix, private common-header data, additional subclass or application control group keeps the whole record as `DxfOpaqueObject`. A malformed count in a recognized leading sequence rejects explicitly. The minimum packet schema is deliberately narrower than every possible FIELD layout.

Each typed dependency resolves through the physical source record and the accepted retained object instance. Generated defaults, skipped entities, records in ignored sections and unresolved non-null handles cannot supply substitute identities. Child fields must have reciprocal common ownership; duplicate children, wrong target types, owner cycles and undeclared owned children reject. An exposed reference to an unregistered metadata-only object is outside this bounded projection and rejects rather than silently binding a substitute.

Common metadata remains editable. Resource renaming retains the same dependency identities and leaves evaluator/cache text untouched. This does not assert that cached values remain current. Removing a referenced entity, block member, layer, style or APPID is blocked; source host removal is also blocked through the complete owner ancestry. Removing an owning dictionary alias retains the FIELD object and causes validation/save to reject until the alias is restored.

The payload's `DxfTag` objects copy binary arrays on access. The collection itself is read-only. Exactness means typed group/value preservation: the existing reader canonicalizes handle spelling and normalizes numeric lexical formatting. Saving does not rewrite opaque evaluator strings or infer handles embedded in those strings.

Graph cloning, foreign adoption and source-profile conversion reject before output or handle allocation. Erasing an ownership tree that contains `DxfStoredField` rejects, including SECTION's dedicated erasure path. This preserves the prior opaque-record restriction while the evaluator's complete private reference grammar remains unknown. Incoming exposed pointers still protect external resources from erasure. FIELDLIST remains an opaque object; no FIELDLIST authoring or evaluation API is introduced.

## Native packet qualification boundary

The qualification carrier retains exact FIELD subclass packets for 14E/14F/15E/15F, exact owner-dictionary payloads for 14C/14D/15C/15D, and the exact populated FIELDLIST 150 payload. Source ATTDEF handles 14B/15B are represented by explicit synthetic LINE hosts with the same numeric handles and reciprocal extension dictionaries. FIELDLIST's common owner/reactor target C is rebound to the carrier's named dictionary root. This is a scoped packet-and-ownership fixture, not a claim of native ATTDEF compatibility or complete source application semantics. The same native payloads are checked before and after allowed common XData edits.

Unmodified source loading was probed separately. AC1015 reaches an existing unsupported OLE2FRAME group 73; AC1032 reaches the existing modern ACIS SAB/ACDSDATA boundary. Neither complete source document is qualified by this module. No AutoCAD open/AUDIT/save/reopen or field evaluation was executed.

## Verification

`RegisterStoredFieldTests` covers native extraction, all six admitted profiles in text/binary synthetic graphs, immutable binary data, resource and host lifecycle guards, alias-loss validation, clone/adoption/profile refusal before mutation, TABLE/FIELD cross references, private fallback, malformed counts/owners and exact source-identity decoys. Semantic pointer/owner families, 390–399 and 480/481 are distinguished from arbitrary 320/329 controls.

`tools/verify_stored_fields.py` requires all 20 emitted DXFs and 12 synthetic graph maps. It independently parses text/binary records, checks exact native FIELD/owner/FIELDLIST payloads against the compressed source hashes, checks actual target types and ownership, validates CLASS metadata, and runs 100 real packet/ownership corruption controls. Missing drawings or maps fail the gate. Its synthetic graphs are schema-shaped test data, separate from the eight native-packet outputs. The final qualified build/run counts are recorded in the accompanying qualification receipt.
