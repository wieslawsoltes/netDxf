# Context-aware raw handle index (unmerged implementation)

`DxfRawHandleIndex.Create(raw, options, cancellationToken)` builds an immutable index without changing tags or original bytes. Identities use numeric hexadecimal keys, so case and leading zeroes do not hide collisions. `FindDefinitions` returns every candidate instead of overwriting duplicate identities; `FindReferences` and `GetOccurrences` expose exact source-tag positions and records.

Common owner slots, subclass pointers, top-level persistent reactors, extension dictionaries, XData handles, arbitrary nontranslated handles, HEADER seeds and uninterpreted occurrences are distinct. DIMSTYLE identity is group 105; the obsolete DIMBLK group-5 name is not a handle. Common owner/identity fields are interpreted in the pre-subclass envelope. Nested application groups and XRECORD's ordinary-code application payload remain opaque. HEADER and unknown-section values never manufacture database identities. A null handle is not a missing reference.

Diagnostics include duplicate/null identities, multiple identities or common owners, unresolved/ambiguous references, malformed control groups and cycles in uniquely resolved common-owner links. Cycle detection is iterative and bounded by the indexed graph, not the process call stack. Explicit occurrence/diagnostic budgets fail rather than silently truncating evidence. Queries and parallel independent indexing are read-only; foreign snapshot records reject.

The 160 feature cases exercise every admitted raw profile (AC1009/1012/1014/1015/1018/1021/1024/1027/1032) in text and binary, authored and decoded spelling, custom nested controls, DIMSTYLE/XRECORD, numeric aliases, duplicates and nulls, budgets/cancellation, stale records, concurrent lookup, actual typed output and a 12,000-record owner chain. Synthetic raw fixtures test lexical/profile transport, not historical legality of every included record.

An initial assertion confused the normalized handle spelling produced by the pre-existing transport codec with the original authored spelling. The final tests separately verify exact current-tag spelling after load and exact authored-tag spelling before serialization; production codec behavior is unchanged.

This index does not discover handles hidden in ordinary strings, binary blobs, class-private payloads or external files. Empty diagnostics are not a full schema or semantic dependency certificate. XRECORD payload classification is deliberately conservative; interpreting it requires an application-specific contract. No native AutoCAD execution is claimed.

Primary references: Autodesk's [group-code table](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm) and [common entity control groups](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm). Current raw and typed admission remains unchanged.

## Executed evidence

Base is merged PR #68 (`cf533ba32d6c732e475192ee021b78f938eda0cb`). The complete signed-library .NET 8 suite passes **15,752 / zero failures** in both Debug and Release. This includes HEADER seed/reference/opaque distinctions and actual XData following XRECORD ordinary-code payloads. These new APIs do not compile against the old assembly; no artificial red-test total is claimed. Windows, SDK/MSBuild, netstandard2.0 and native AutoCAD runs have not been executed for this unmerged feature.
