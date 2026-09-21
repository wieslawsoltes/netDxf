# DSTYLE resolution in all typed containers

Typed loading previously resolved DIMENSION and LEADER ACAD/DSTYLE overrides
through `doc.Entities.Dimensions` and `doc.Entities.Leaders`. Those are active
layout shortcuts, so paper layouts and ordinary blocks were skipped. The raw
XData could remain present while the typed override dictionary was empty;
cloning or rewriting could then lose its effective settings.

Resolution now enumerates all registered blocks, including every layout block,
unreferenced blocks and multiply instanced nested blocks. It processes each
stored entity once rather than walking INSERT instances recursively. Each
entity family is snapshotted before reference-adoption callbacks execute.
Resolution remains late, after block, linetype and text-style objects exist.
The active entity layout is not changed. The existing parser, validation,
default fallback and reference-adoption mechanisms are reused; previously
skipped invalid overrides can now fail instead of being silently ignored.

The follow-up was discovered by the DIMLFAC fidelity matrix: its initial hosted
Linux/Release result retained all 66,665 baseline cases and passed the 60 new whole-document
ELLIPSE cases, but failed all 168 block-placement DIMLFAC cases. The original
596 new tests and their required 1,008 drawings are retained, not restricted to
modelspace. Initial incomplete artifacts are not accepted as a complete corpus.

## Added qualification

A 72-case matrix covers six typed profiles, text/binary input and both output
transports, and six placements: modelspace, two different paper layouts, a
referenced block, repeated two-level block instances, and an unreferenced block.
Each drawing contains all seven implemented DIMENSION families and a LEADER.
Nine typed overrides include real, boolean, indexed-color, text-style, linetype
and arrow-block values. Checks include exact scalars, canonical reference
identity, reference-use bookkeeping, detached clones, clone isolation, retained
handles, unrelated XData, following entities and object validation. Twelve
additional cases require invalid zero overrides in a block to reject without
closing the input stream.

The independent checker requires all 216 source/output drawings, verifies
physical override packets and referenced table/BlockRecord handles, loads all
1,728 dimension/leader records with ezdxf, checks owners and all seven dimension
kinds, and audits without repairs. Actual-tag corruption and filesystem
inventory controls must reject. The main DIMLFAC checker remains unchanged.

No typed historical version gate, writer policy, drawing operation, public
API, parser fallback, or private-cache regeneration is added here. Fixed labels
and synthetic drawings establish stored-data/reference behavior, not native
AutoCAD visual output, font equivalence or exhaustive DWG/DXF parity. Final
execution counts and source/artifact hashes belong to the PR qualification.
