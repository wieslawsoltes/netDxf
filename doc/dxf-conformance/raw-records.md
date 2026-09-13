# Ordered raw record indexes and scoped edits

Baseline: `7d3d27b3877c2557c0838ea04c367ab140707209`, after merged PR #30. Builds on the raw preservation API from #29; typed DxfDocument serialization is unchanged.

## API and boundary contract

`DxfRawSection.Records` exposes immutable `DxfRawRecord` indexes. HEADER records start at group 9; other sections use group 0. Each index retains the exact record name, marker code, section name, absolute start/exclusive end, full tag slice and content excluding the opening marker. Duplicate names remain distinct, in order. A SECTION entity inside ENTITIES is a record, not a nested file section.

`ContentStartTagIndex` locates the section body. `Preamble` retains every body tag preceding the first record marker. Marker-free sections, including THUMBNAILIMAGE, have no records and their entire body is the preamble. Thus `Preamble` followed by all record tag slices partitions the complete section content without losing or copying tags. Comments before a marker remain in the preceding lexical range; that convention does not infer object ownership. Comments between SECTION and its group-2 name remain in the complete document Tags.

Record indexing is lazy per section and uses thread-safe publication. Unedited raw load/save does not eagerly allocate a record object for every group-0 marker. Once requested, the cached index costs O(section tags) time and O(record count) metadata; it does not copy strings or byte payloads. This is not a hard bound on the process heap, and it does not make the mutable typed geometry API thread-safe.

```csharp
using System.Linq;
using netDxf.IO;

using var input = File.OpenRead("plant.dxf");
var original = DxfRawDocument.Load(input);
var entities = original.Sections.Single(s => s.Name == "ENTITIES");
var line = entities.Records.First(r => r.Name == "LINE");
var edited = original.WithRecord(line,
    line.Tags.Select(tag => tag.Code == 11 ? new DxfTag(11, 42.125) : tag));

using var output = File.Create("plant-edited.dxf");
edited.Save(output); // Unknown records outside this range remain untouched at tag-value level.
```

`WithRecord` requires one complete replacement with the same marker code and a nonempty name. Empty, null, multi-record and section/document-terminating replacements are rejected. `WithoutRecord` is the explicit removal operation. Both return new immutable snapshots, preserve every tag outside the selected range, enforce existing profile/budget restrictions and reject foreign or stale record indexes before enumerating replacement data. Retrieve indexes from the newly returned document before a subsequent edit.

## Fidelity and version matrix

| Capability | AC1015 / 2000 | AC1018 / 2004 | AC1021 / 2007 | AC1024 / 2010 | AC1027 / 2013 | AC1032 / 2018 |
|---|---|---|---|---|---|---|
| HEADER and other record indexes | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary |
| Replace/remove unknown records | Tested | Tested | Tested | Tested | Tested | Tested |
| Duplicate names and stale-index rejection | Tested | Tested | Tested | Tested | Tested | Tested |
| Raw edit followed by typed geometry load | Tested | Tested | Tested | Tested | Tested | Tested |
| Independent edited LINE/CIRCLE audit | Exact values | Exact values | Exact values | Exact values | Exact values | Exact values |

This indexes lexical units, not complete semantic aggregates. TABLE/ENDTAB, BLOCK/ENDBLK and VERTEX/SEQEND remain separate records. Removing an owner does not remove children or incoming references. No handle remapping, dependency closure, schema validation, dynamic-block evaluation or legacy document dialect is added. Record edits invalidate original-byte output; normalized serialization preserves ordered tag values, not the original whitespace or numeric spelling of unrelated records. The original unedited snapshot still saves byte-identically.

## Executed evidence

117 new registered cases extend the suite from 5,309 to **5,426 passed / 0 failed**, local signed-library Debug and Release. Tests cover every admitted format/transport, exact partition indexes, shared immutable tag identity outside edits, unknown data/repeated codes, duplicate names, marker-free and empty sections, HEADER profile restrictions, comment placement, binary-array isolation, concurrent lazy publication, iterator disposal, bounded infinite replacement sequences and semantic geometry round trips. This is new API coverage; no fictitious pre-existing-API red count is asserted.

The independent ezdxf 1.4.4 verifier checks all 12 retained edited drawings: exact binary64 LINE coordinates, untouched CIRCLE data, **zero audit errors and zero repairs**. It does not modify drawings or load external resources. Run `python tools/verify_raw_record_edits.py artifacts/conformance --output audit.json` after the conformance suite. The optional Python dependency is development-only. Final-head Linux/Windows SDK, netstandard2.0 and source-audit CI remain merge gates; AutoCAD itself has not been executed.

## Primary references

- Autodesk group 0 entity types, group 9 HEADER variable identifiers and comment semantics: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm
- Autodesk tagged-record representation: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-235B22E0-A567-4CF6-92D3-38A2306D73F3.htm
