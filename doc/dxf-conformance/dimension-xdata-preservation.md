# Non-destructive DSTYLE serialization

DIMENSION, ARC_DIMENSION and LEADER now serialize a replacement view of their
ACAD XData, without clearing or attaching records on the source entity.
TOLERANCE uses the same mechanism for its text-height override. The existing
codecs still generate all supported style variables; this change isolates the
application/subsection boundary, not a second dimension formatter.

## Ownership and preservation

A DSTYLE subsection is a top-level string marker, followed by an opening 1002
control string, scalar identifier/value pairs, and a closing control string.
Other top-level records and balanced nested lists are retained in their original
order. A DSTYLE lookalike inside another list is opaque, not a style override.
The actual subsection's marker spelling and delimiters are retained. Both
reader paths use the same boundary finder before their existing semantic decode.

For dimensions and leaders, fields materialized by the existing typed override
reader are owned by StyleOverrides. Their present values replace stored values;
removed overrides remove those stored fields even when the dictionary becomes
empty. Unsupported identifiers, including the ignored DIMDLI identifier 43,
are retained in order with their original value records. TOLERANCE owns only
identifier 140; other pairs remain opaque even when dimension entities would
interpret them. New fields are appended within the subsection; unknown pairs
keep their relative positions, and existing managed fields are replaced in place.
An empty resulting subsection is removed. An otherwise empty ACAD application
is omitted from output, but its source object is not deleted or modified.

Existing application IDs, XData objects, record objects, binary payloads and
application order on the source remain unchanged. No XData registry events are
fired. Generated records are detached serialization buffers. This replaces the
old incidental side effect of publishing serialized DSTYLE into source XData.
Directly authored managed DSTYLE fields are not an alternative to the typed
StyleOverrides API on save; the typed dictionary is authoritative. Opaque-only
fields remain supported through XData. Loading continues to materialize complete
composite fields, as documented in the affix and composite-override contracts.

## Malformed and ambiguous input

For these hosts, a top-level DSTYLE marker must introduce exactly one properly
framed list of complete scalar pairs. Duplicate identifiers, multiple top-level
DSTYLE lists, nested values inside DSTYLE, null records, mismatched brackets and
wrong record types for managed identifiers reject with FormatException in the
codec. Public Load/Save retain their existing Debug/Release failure conventions.
Unrecognized scalar identifiers are not decoded or validated as native settings.
Unbalanced surrounding ACAD lists also reject, because their top-level scope is
not well-defined. This intentionally tightens earlier partial/ambiguous decoding;
it does not silently choose a conflicting list or erase malformed user data.

XData remains unchanged after a rejected save or an injected output failure.
This is **not** whole-document transactional serialization: handle assignment,
other entity writers, output bytes already written, document registration,
Update and adoption retain their existing separate behavior. Use the raw APIs
for exact opaque transport of data outside this typed admission contract.

## Verification

The 317-case focused harness contains 40 all-family/source-preservation cases,
184 version/transport/container cases, 18 opaque-only/empty-list cases,
72 malformed physical-input/output cases, and three injected output failures.
All eight implemented dimension families, LEADER and TOLERANCE are exercised.
The wire matrix covers linear dimensions, arc-length dimensions, leaders and
tolerances in modelspace, paper space, referenced and unreferenced blocks across
six typed versions and both transports. ARC_DIMENSION keeps its R2004+ gate.

The wire corpus contains 920 drawings: initial source plus both output formats
after editing and clearing typed overrides. The independent checker compares
complete physical application packets and ezdxf-loaded XData, including binary
bytes, Unicode, handle references, signed zero and nested lookalikes. It checks
ownership, sentinel geometry, fixed primary labels and graph integrity without
repairs. It deliberately inspects the independent reader's actual XData rather
than a named-list convenience lookup, which may not distinguish nested or
mixed-case markers. Packet and complete-inventory corruptions must reject.
These synthetic drawings are not native producer or renderer qualification.

The final unchanged harness was executed against isolated old and new production
assemblies: 3/317 before and 317/317 after. These include strengthened admission
contracts, not 314 distinct underlying bugs. An initial checker used the generic
scalar cast on binary payloads; normalizing ASCII hex and binary bytes at that
input boundary corrected the checker without changing production or assertions.
Complete suite results and source/artifact hashes are retained in the accompanying
qualification report. Local execution does not imply hosted or native qualification.

## Primary reference and remaining boundaries

[Autodesk Dimension Style Overrides](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-6A4C31C0-4988-499C-B5A4-15582E433B0F.htm)
defines the named DSTYLE subsection and paired values inside ACAD extended data.
This change does not interpret every unknown identifier, regenerate native
private caches, remap opaque dependencies during imports, change version gates,
or qualify alternate/tolerance/fit/font rendering. Other application-specific
XData writers, historical typed dialects/pre-R11, general version conversion and
native AutoCAD open/AUDIT/save/reopen remain separate work.
