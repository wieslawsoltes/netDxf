# Strict text DXF record framing

A physical end of stream must not fabricate a DXF group-0/EOF record. The previous text codec did so repeatedly: a missing final EOF marker was accepted, and an unterminated CLASSES section could repeatedly read physical EOF rather than terminate.

The codec now throws `EndOfStreamException` for a missing group-code or value line, with physical line information and the group code for missing values. Invalid group-code text raises `FormatException`. Null readers are rejected at construction. Valid empty strings, space-padded numeric group codes, CR/LF/CRLF, and an explicit EOF value without a final newline remain accepted. Source IO errors propagate unchanged.

This is intentionally stricter for truncated input. The public document API retains its existing conventions: Debug propagates parsing failures, Release returns null, and caller-owned streams remain open. The change does not fix every numeric-value validator or implement arbitrary unknown-section preservation.

## Regressions

87 added registered cases cover missing values across 42 representative group codes, diagnostics on initial/subsequent physical lines, newline variants, reader ownership/errors, all six supported versions with a missing final EOF marker, and unterminated HEADER/CLASSES sections in text and binary.

The unchanged production implementation with these tests produced **468 passed / 59 failed**. Six text CLASSES cases triggered the bounded EOF-read guard, proving nontermination without relying on abandoned tasks. Six complete text documents with their required final EOF record removed were incorrectly accepted. The corrected signed library produced **527 passed / 0 failed** in Debug and Release.

A guard stream stops the broken baseline after repeated empty reads; it is not part of production code. Existing text/binary document, XData, thumbnail, UCS and named-view regressions remain enabled. Final-head cross-platform CI and netstandard2.0 builds are required before merge.

## Primary references

- Autodesk's code/value line structure: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-D939EA11-0CEC-4636-91A8-756640A031D3.htm
- Autodesk's requirement for the EOF item: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-5D1DFE5C-94FC-43B7-B535-43001D1662C1.htm
