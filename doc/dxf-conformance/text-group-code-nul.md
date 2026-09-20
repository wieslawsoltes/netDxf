# Reject NUL-contaminated text group codes

The text codec now checks the group-code line for NUL before numeric parsing.
The previous `short.TryParse` check accepted trailing NUL characters on the
executed runtime, although the value-line numeric parsers already rejected
NUL. This allowed malformed raw text DXF to pass the initial structural scan.

The change rejects NUL anywhere in a group-code line with FormatException,
before consuming its associated value line. It preserves the accepted ordinary
integer forms, including ASCII padding, an explicit plus sign and leading zeros.
It does not change binary framing, string-value rules or caller stream ownership.

Regression coverage exercises primitive code categories, single/repeated NULs,
whitespace followed by NUL, all nine raw DXF profiles in LF and CRLF, valid
integer controls, and verification that the value line remains unread.
Before/after runs use the same final harness and the actual preceding DLL.

This is lexical validation, not a new dialect, recovery mode or historical typed
reader. The independent raw LINE task exercises the normal valid text/binary
paths; the complete prior codec suites remain enabled. Native AutoCAD execution
and complete DXF parity are not established by this correction.

```sh
DXF_TEST_FILTER=text-code-nul/ dotnet run --project tests/netDxf.Conformance -c Release
```
