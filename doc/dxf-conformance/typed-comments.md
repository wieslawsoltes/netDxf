# Ignore interstitial ASCII comments in every typed parser

## Defect and correction

Group 999 is an ASCII DXF comment, not part of an entity's or section's grammar. The existing typed reader ignored it in some loops but still expected adjacent noncomment tags elsewhere. A comment between SECTION and its group-2 name caused invalid casts; comments inside XData could terminate its parser and trip a Debug assertion in the caller.

After retaining the existing leading-comment preamble, `DxfReader` opts its `TextCodeValueReader` into skipping comment pairs in `Next`. This iterative codec-local loop preserves physical line accounting and truncated-pair exceptions. Every typed parser therefore sees the same noncomment sequence, including section names, counts, subclass packets and XData. The binary path has no additional wrapper or dispatch.

The option defaults to false. Standalone codecs, version probing and `DxfRawDocument` still receive every tag. Raw round trips preserve the original bytes, including interstitial comments. The typed document preserves only its existing leading `Comments` preamble; it does not newly retain comments at arbitrary positions. Binary comment rejection remains unchanged.

## Version comparison

All six typed profiles (AC1015, AC1018, AC1021, AC1024, AC1027, AC1032) receive this correction for ASCII input. Binary behavior is unchanged. The three historical raw profiles (AC1009, AC1012, AC1014) do not gain typed admission, and no raw preservation behavior changes.

## Executed evidence

Base: merged PR #62, `78857600b444972f47d3ccdd53a7f8001d74cc8d`, tree `1e17d6686d13d493f19b809f0661ccf458b49720`.

`TypedCommentTests.cs` adds 78 cases: comments after every tag, long consecutive comment runs, targeted section/header/table/subclass/geometry/XData/EOF positions, leading-preamble controls, binary controls and invalid-count controls. Input bytes are independently encoded. Comments contain structural-looking text which must remain inert.

**Unchanged production in Release: 14,377 passed / 24 failed.** The unchanged Debug process **terminates at the existing HATCH assertion** on the first XData-comment case after three earlier invalid-cast failures. That aborted run has no complete case report; no Debug red total is inferred. Its retained log records the assertion and call stack.

**Corrected signed production: 14,401 passed / zero failed in both local .NET 8 configurations.** The final implementation uses the opt-in text-codec loop rather than the initially tested delegating wrapper. All 15 Python ledger-integrity tests pass separately. Final-head CI must execute Linux/Windows Debug/Release, build netstandard2.0, and retain the source audit before merge.

`verify_typed_comments.py` independently reads all six fully commented ASCII fixtures using ezdxf 1.4.4, checking two pattern lines, boundaries, seeds, scalar fields, XData and the following LINE; zero audit errors and zero repairs. This validates input interpretation, not just output produced by the corrected writer.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_typed_comments.py artifacts/conformance
```

## Primary references and limits

- [Autodesk numerical group codes](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm), group 999: comments are honored on input and ignored.
- [Autodesk binary DXF](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm): group 999 is not used in binary DXF.

No new malformed-data recovery, grammar relaxation, whole-file resource quota, arbitrary comment-position persistence in the typed model, native AutoCAD execution or full-standard certification is claimed. Comments do not hide invalid counts or synthesize missing required fields.
