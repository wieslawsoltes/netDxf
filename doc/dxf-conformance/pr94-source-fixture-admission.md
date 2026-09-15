# PR94 source-identity fixture integration

The bounded `DxfOpaqueEntity` reader changes how the older synthetic identity
fixtures are admitted. Their `FUTURE_ENTITY` and `DICTIONARY` records in ENTITIES
have no physical group 330 BLOCK_RECORD owner. They are malformed envelopes,
not valid retained opaque entities. The reader now rejects the whole input
before reference late binding. The four historical TABLESTYLE carriers that
previously saved after silently skipping those records therefore become explicit
rejection tests. Existing historical qualification receipts describe their
original source revision and are not rewritten as evidence for this increment.

`SourceReferenceRejectMalformedEntity` is used only by these deliberately
ownerless records. Debug must throw the exact `InvalidDataException` admission
message; Release's existing public Load wrapper returns null for the same input.
The helper also checks the missing owner premise and caller stream lifetime. It
does not broaden any late reference rejection helper. This boundary applies to
DATATABLE, FIELD, composite TABLE ownership and generic source-reference tests.

The generated TABLE and RASTERVARIABLES identity negatives still exercise late
source binding: their lexical same-handle decoy is now the already qualified
PDFUNDERLAY with a null definition, which the managed reader discards. Missing
physical identities and private payload group 5 identities still must not bind
to synthesized managed objects. The source-reference tests retain their named
reference diagnostic; the four generated DATATABLE identity cases require the
exact unresolved-cell diagnostic in Debug. DATATABLE's binary source-identity variants now explicitly
write binary input, correcting their formerly implicit ASCII serialization.

Four new TABLESTYLE positives use a separately declared standalone opaque
`QUALIFIED_SOURCE_TARGET` packet with its actual registered model-space
BLOCK_RECORD owner. These are synthetic schema fixtures, not producer evidence.
Normal and zero-padded lowercase physical handle spellings run through both
input transports and save to the opposite transport. The tests require the
actual registered `DxfOpaqueEntity` in TABLESTYLE's references, unchanged actual
STYLE resource identities, removal protection and the same identity after reload.
The target's private payload is inert storage; no geometry semantics are claimed.

The mandatory TABLESTYLE verifier replaces the four obsolete malformed output
names with four explicit `table-style-opaque-identity-*` names. Its exact inventory
remains 70 outputs. It pins the complete opaque packet, physical identity,
registered BLOCK_RECORD owner and private payload. Four separately inventoried
`qualified-style-source-*` inputs pin the supplied normal or zero-padded lowercase
physical group 5 and TABLESTYLE group 340 spellings. Both public code-value readers
canonicalize handles before typed tag capture, so output uses exactly `C0FFEE01`
at those two slots. All other target packet fields remain exact. Separate source
and output link, identity, owner and payload controls enforce this boundary.
The gate has 118 controls, replacing four old link controls with twenty-four
controls on the four new positive source/output pairs. Other original unresolved
reference output carriers remain required. No production code changes are made.

The frozen fixture revision is `7b40a5e`. Debug and Release each pass 769 cases:
DATATABLE 467, TABLESTYLE 93, FIELD 76, composite TABLE 49 and generic source
references 84. The net addition is four cases. Both configurations pass all four
associated gates: TABLESTYLE 70 outputs plus four inputs / 118 controls;
DATATABLE 34 outputs / two controls; FIELD 20 outputs plus 12 maps / 100 controls;
composite TABLE eight outputs plus four maps / 80 controls. The four new opaque
output carriers audit with zero errors or repairs in each configuration.

An independent reviewer checked the fixture/gate source and separately replayed
the final Release TABLESTYLE gate in 9.385 seconds. Its immutable receipt clearly
separates that replay from the owner's 769-case C# runs. The accompanying
qualification JSON pins source and DLL hashes, test results, gate logs and all
generated DXF hashes. The new four input/output pairs per configuration are
archived losslessly; existing unrelated generated drawings remain reproducible
from the unchanged fixture definitions and are represented by their manifest.
