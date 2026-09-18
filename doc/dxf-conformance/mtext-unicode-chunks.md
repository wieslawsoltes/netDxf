# MTEXT Unicode continuation records

This C# task starts from merged commit `9f302ed680cb3dba682b6d12d52d321d0109a139`
(tree `6c17b36eae19af8ab6d1dffd0fdea1a38c45af0c`). The JavaScript port is unchanged.

## Corrections

The writer previously sliced text at every 250 UTF-16 code units. A supplementary
Unicode character crossing that boundary was split into two unpaired surrogates.
Encoding each DXF value separately then replaced its halves, permanently changing
the text. Even BMP-only Unicode could exceed a byte-oriented consumer's string
budget because a UTF-8 character can occupy multiple bytes.

The writer now scans complete Unicode scalars and emits continuation group 3
values followed by exactly one terminal group 1. Each value is at most 250 UTF-8
bytes and at most 250 UTF-16 units. A scalar that will not fit starts the next
value; a surrogate pair is never split. Empty content still emits one empty
terminal value. A terminal ASCII value can still contain exactly 250 characters,
retaining the established ASCII behavior.

Before R2007, the existing encoder first emits ASCII `\U+hhhh` escapes. This task
does not change those escape bytes or their 250-character chunk boundaries.
Escapes are decoded after concatenation on read, including any escape crossing a
record boundary. R2007 and later retain literal UTF-8. Combining marks are not
normalized, and formatting instructions are not reinterpreted or rewritten.

Both directions now avoid repeated copying of the whole remaining/accumulated
string: the writer advances indices, and the reader appends to a StringBuilder
and decodes once. Chunk traversal/assembly is linear in the text length. This is
not a claim that the entire DXF save/load pipeline has linear complexity, or a
measured whole-application speedup.

## Executed focused verification

The same 314 new harness cases pass **192 before / 314 after**, with 122 observed
pre-fix failures and zero post-fix failures. Coverage includes all six typed
profiles in both transports, two alternating save/reload cycles, ASCII boundaries,
supplementary characters positioned around chunk edges, Polish/CJK/combining text,
formatting sequences, nested blocks, linked/embedded columns, following LINE
records and trailing XData. The large-text cases use 196,608 UTF-16 code units.

The independent `verify_mtext_unicode_chunks.py` gate reconstructs its inputs
separately, reads physical tag values, checks exact joined text and UTF-8 byte
budgets, and independently loads and audits 302 emitted drawings. It checks 4,020
chunks, rejects 1,450 actual-packet corruptions and two missing/extra inventory
controls, and reports zero audit errors/repairs with ezdxf 1.4.4. Legacy model text
is compared after explicit DXF escape decoding and strict UTF-16 pair assembly;
this is not font rendering qualification.

Full-suite results belong in the task's execution receipt, rather than being
inferred by adding the focused count to a previous run. No baseline assertions,
fixtures, verifier gates or version admission checks are removed.

```sh
DXF_TEST_FILTER=mtext-unicode/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_mtext_unicode_chunks.py artifacts/conformance
```

## References and boundaries

Autodesk's [MTEXT reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-5E5DB93B-F8D3-4433-ADF7-E92E250D2BAB.htm)
describes groups 3/1 in terms of 250-character chunks. The independent maintainer's
[MTEXT internals](https://ezdxf.readthedocs.io/en/stable/dxfinternals/entities/mtext.html)
notes the byte-oriented limit and multibyte-character issue. The 250-byte envelope
used here is an explicit conservative interoperability policy; it does not claim
that the two references give an identical Unicode counting rule.

Wire qualification covers R2000/R2004/R2007/R2010/R2013/R2018. Historical typed
profiles, invalid UTF-16 input policy, arbitrary text-control framing, native font
layout and AutoCAD open/AUDIT/save/reopen are not established by this task. The
scope is standalone MTEXT content, also when it belongs to a block or column
entity; it does not redefine FIELD, TABLE or embedded attribute string schemas.
