# Seventh mixed-family qualification plan

This work combines stored UCS base references, native DIMASSOC packets, and native
TABLE roundtrip ownership/TABLECONTENT. The initial source baseline is
`1a8f9e368c9c0e7add271f9729f2c9b88f64bb1b`. TABLECONTENT's final carrier closure and
public backing-content API splice are pending qualification. This document is a
plan; no new test or output qualification is claimed at this checkpoint.

Two focused graphs will exercise the intersections without multiplying unrelated
module test matrices:

- R2004: exact native composite wrapper 13DF, its TABLECONTENT/TABLEGEOMETRY and
  DATATABLE ownership closure, native DIMASSOC application packets, a UCS base
  relationship, and stored FIELD dependencies sharing actual objects.
- R2018: an exact native TABLECONTENT roundtrip graph, native DIMASSOC application
  packets, a UCS base relationship, and stored FIELD dependencies, with SECTION
  settings and a SUN extension XRECORD where those existing APIs permit the
  relationships. Both DXF transports are required.

Native geometry and association packets will come from the committed DIMASSOC
extractions. Their named resource/display-block scaffolds are already explicitly
synthetic. Any integration mapping will name the exact external model-space owner
and resource targets; native point data, association owner/reactor edges, and
TABLE payloads must remain unchanged except for explicitly declared identity
mapping. The UCS base relationship and additional FIELD/SECTION/SUN links are
authored integration scaffolds, not claims of new native evaluator behavior.

The lifecycle assertions will concentrate on shared references: removing one
mutable UCS or SECTION consumer must not release a target still referenced by an
immutable FIELD or DIMASSOC. Ordinary entity/resource removal, containing-object
erasure, and ancestor cloning must reject before allocation or membership changes.
This also checks that promotion of TABLECONTENT from an opaque object preserves
the earlier clone/erase boundary. Successful local clones of supported mutable
owners must remap their owned identities while retaining external native targets;
tearing down those copies must leave the original graph unchanged.

Additional cases will exercise canonical source-handle spelling, a discarded or
missing shared source target, and profile conversion rejected before touching an
output stream or reserving handles. The exact raw gate will validate emitted
native bodies, ownership/reactor edges, UCS base pointers and per-view origins,
cross-family references, clones and teardown. Its corruption controls will mutate
actual parsed output and invoke the same validators used for successful files.

Production edits are outside this task. A demonstrated production defect will be
reported separately to the owning agent before changing the mixed tests' scope.
