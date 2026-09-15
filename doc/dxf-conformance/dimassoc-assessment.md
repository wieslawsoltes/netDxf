# DIMASSOC stored-object assessment

This is the evidence assessment for the bounded [immutable stored DIMASSOC
model](stored-dimassoc.md). The complete association schema and evaluator gaps
remain open. The proposal below records the evidence and boundaries that led
to the first stored slice.

## Pinned sources

The primary description is Autodesk's [2009 DXF reference](https://damassets.autodesk.net/content/dam/autodesk/www/developer-network/platform-technologies/autocad-dxf-archive/acad_dxf_2009.pdf),
printed pages 184–186, PDF pages 192–194 when counted from one. It specifies
AcDbDimAssoc with a dimension pointer (330), association mask (90), trans-space
flag (70), rotated-dimension type (71), and AcDbOsnapPointRef records introduced
by group 1. Each reference can contain an osnap type (72), geometry pointers
(331/332), subentity types (73/74), marker indexes (91/92), external-reference
strings (301/302), a parameter (40), a WCS point (10/20/30), and a last-point flag
(75). The low four association bits identify the first through fourth point
references. The documentation describes automatic dimension updates; this
assessment proposes stored data and reference handling only.

Independent implementation sources are pinned separately:

- [LibreDWG dwg2.spec at 34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg2.spec)
  has a variable DIMASSOC reference sequence and labels this family unstable.
  It supports repeated main-object handles, conditional intersection fields,
  external-reference paths, and up to six references when last-point state is
  present. Its group-74 use is not sufficient evidence to invent a public count
  field: its code labels both an intersection count and a subentity type with 74.
- [IxMilia ObjectsSpec.xml at 3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567](https://github.com/ixmilia/dxf/blob/3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567/src/IxMilia.Dxf.Generator/Specs/ObjectsSpec.xml)
  declares DxfDimensionAssociativity as a flat single-reference object and sets
  its minimum version to R2004. That model cannot establish fidelity for the
  repeated native reference bundles below. Its minimum version is contradicted
  by positive AC1015 native records. It is not a malformed-input oracle.

## Positive native evidence

An independent raw text/binary inventory found 64 actual DIMASSOC objects in
nine files in the pinned LibreDWG test corpus. All 67 candidate files were
Git-blob-verified; 66 were parsed. The remaining R1.4 file starts with an older
EXTENTS record format and is explicitly outside the scanner's pair-tag input.
CLASS declarations were counted separately and do not establish object support.

| Pinned repository path | DXF profile | Actual objects |
| --- | --- | ---: |
| test/test-data/2000/TS1.dxf | AC1015 | 2 |
| test/test-data/2018/TS1.dxf | AC1032 | 2 |
| test/test-data/example_2000.dxf | AC1015 | 8 |
| test/test-data/example_2004.dxf | AC1018 | 8 |
| test/test-data/example_2007.dxf | AC1021 | 8 |
| test/test-data/example_2010.dxf | AC1024 | 8 |
| test/test-data/example_2013.dxf | AC1027 | 8 |
| test/test-data/example_2018.dxf | AC1032 | 8 |
| test/test-data/2018/Dynblocks.dxf | AC1032 | 12 |

There are 130 reference bundles. Every native group-1 marker is
AcDbOsnapPointRef. Masks 2, 3, 7 and 15 occur in 16, 36, 6 and 6 objects
respectively and correspond to one, two, three and four ordered bundles. The
observed osnap values are 1, 3 and 13; every group 75 is zero. No actual 301/302
external-reference strings, 332 intersection targets, or last-point variants
were found in these 64 objects.

The ordinary bundle sequence is
`1,72,331,73,91,40,10,20,30,75`. Six bundles repeat 331 once. Those belong to
object 42F in the six example files and retain the ordered identity path
`POLYLINE 41A, VERTEX 41E`. The other native source entities are LWPOLYLINE,
LINE, ARC and CIRCLE. Several stored points contain finite Z values of 2e50;
they must remain stored values without normalization or interpretation.

Every one of the 64 objects is hard-owned by a DIMENSION extension dictionary
entry named ACAD_DIMASSOC, using dictionary group 360. The dictionary's common
owner is that dimension; the DIMASSOC's post-subclass 330 also points to the
dimension. Native DIMENSION packets contain a persistent-reactor backlink to
the association. Common object ownership, subclass dimension references and
reactor references are separate fields even when they repeat identities.

These observations establish real native presence in the six AC1015–AC1032
profiles. They do not establish that complete original drawings can be loaded
and resaved unchanged, or that all association variants are publicly modeled.
An independent unchanged-file probe of the two TS1 files stopped on unrelated
features before DIMASSOC: AC1015 rejected an OLE2FRAME group-73 variant, and
AC1032 rejected modern ACIS SAB/ACDSDATA. Qualified selected-packet extraction
must therefore remain distinct from complete original-file interoperability.

## Bounded implementation proposal

A first stored model admits the qualified one-main-object AcDbOsnapPointRef
variant with the observed osnap types 1, 3 and 13, an explicit low-four-bit mask,
and ordered references matching the set bits. The model retains all
independent stored integers, finite geometry parameters and points. Dimension
and geometry links bind to real source identities and participate in source-
document validation, incoming-reference checks and ordinary entity/block removal
guards. The initial API is immutable and source-version-bound. Generic cloning
and owned-subtree erasure reject until the complete dimension association
lifecycle is supported; explicit maps do not make a partial clone safe.

The first implementation preserves the whole object opaquely when it
contains repeated main-object handle paths, intersection or external-reference
variants, last-point extensions, or unqualified private classes. The current
POLYLINE readers collapse VERTEX records into value collections and cannot bind
the native 41E identity. Replacing it with a vertex index or a generated object
would invent a relationship. The opaque branch is chosen before partially
binding references, and opaque graph cloning remains unsupported. Common owner
chains and exposed standard pointer fields are still guarded during ordinary
removal; hidden private dependencies remain unqualified.

The existing generic dictionary and object graph APIs store the native
extension-dictionary ownership. The immutable slice retains and validates the
DIMENSION and source-entity reactor backlink state without adding, dropping or
updating it. Ordinary entity/block removal has an explicit integration for
typed dependencies and all DIMASSOC owner chains. Generic object cloning alone
does not reconnect a complete DIMENSION association graph, so this slice rejects
it before any destination registration.

Qualification uses complete selected packets and ownership context from the
pinned originals, with an explicit identity map and disclosed synthetic carrier
resources. Native-backed cases remain distinct from independently authored
structural mutations. No geometry evaluation, automatic updates, external file
access or invented base transforms are provided.
