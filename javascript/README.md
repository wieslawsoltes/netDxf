# netDxf JavaScript port

Work-in-progress native JavaScript port of the pinned C# implementation. This directory is isolated from the existing .NET library. Do not interpret a passing subset of tests as full parity.

## Source contract

- Repository: `wieslawsoltes/netDxf`
- Baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`
- C# library: `netDxf/`; JavaScript library: `javascript/netDxf/`.
- C# conformance suite: `tests/netDxf.Conformance/`; JavaScript suite: `javascript/tests/netDxf.Conformance/`.
- C# examples: `TestDxfDocument/`; JavaScript examples: `javascript/TestDxfDocument/`.
- Original fixtures and support files are shared, not silently rewritten or replaced.
- Relative source filenames, public class/member names and test names are retained. Language-specific adaptations must be documented.

## Completion gates

1. Every source file and API member is accounted for; an unimplemented declaration is not a completed port.
2. Original test cases run in JavaScript under their original names, with no missing or silently skipped cases.
3. Direct .NET/JavaScript comparison checks success and rejection behavior, object graphs, ordered DXF tags and exact emitted bytes.
4. Cross-runtime round trips cover both directions, all supported versions, text/binary transports, original fixtures and generated cases.
5. Exact-byte equality is reported separately from any explicitly normalized comparison. Raw byte retention is not typed editing parity.
6. Numeric precision, ownership, cloning, reference identity, handle lifecycles, encoding, malformed-input behavior and atomic-save guarantees are verified.
7. Browser/Node packaging and repeatable performance measurements are validated before release.

The PR remains a draft while these gates are incomplete. The .NET implementation is the behavioral oracle, not an implied certificate of complete AutoCAD compatibility.
