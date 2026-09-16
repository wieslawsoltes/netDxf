# Stored CELLSTYLEMAP entry-name editing

`DxfStoredCellStyleMap.ReplaceEntryNames(IEnumerable<string>)` replaces decoded
entry names in stored order. It requires exactly one non-null string per entry.
Empty names, duplicate names and marker-like strings are allowed, matching the
existing stored grammar. Entry identifiers and types, entry order and count,
all formatting packets, dependencies, ownership and common metadata stay fixed.

```csharp
DxfStoredCellStyleMap map = style.StoredCellStyleMap;
if (map != null)
{
    string[] names = map.Entries.Select(entry => entry.Name).ToArray();
    names[0] = "Custom title";
    map.ReplaceEntryNames(names);
}
```

The method supports the existing loaded R2004–R2018 maps. It does not construct
maps, interpret formatting values, assign fixed title/header/data roles,
regenerate tables, or synchronize application-defined references to names.
Changing the stored name is an explicit packet edit. Opaque variants keep their
existing preservation contract and have no projected entry-name API.

Enumeration and disposal complete before source validation. Every source
identity, owner, dependency and version must still be valid. A nested call,
even one whose exception is caught by the caller's enumerator, causes the outer
request to reject. A failed call leaves map snapshots unchanged; independent
document changes made by caller callbacks are not rolled back. The method can
be used normally after a rejected request.

A successful edit replaces immutable `Payload` and `Entries` snapshots. Earlier
snapshots remain unchanged. Equivalent requests retain the current snapshot
objects and lexical spelling. Only selected entry-name tags are encoded anew;
all other tags, including native format packets and handle spelling, survive.
Literal backslashes are escaped to prevent `\\U+`-like text being reinterpreted.
R2004 non-ASCII characters use DXF Unicode escapes. Names must be valid UTF-16
without NUL, with at most 1,048,576 code units after encoding. CR/LF values retain
the ordinary binary/text save contract. The complete record including common
metadata must remain within the existing 1,048,576-tag admission limit.

The conformance tests cover five source versions and both transports, all five
existing native map fixture families, Unicode and literal escape round trips,
untouched format packets and identities, immutable snapshots, equivalent edits,
empty maps, malformed names/counts, throwing enumeration/disposal, caught
reentry, version mutation during enumeration, and recovery after rejection.
The independent packet verifier compares emitted native maps with the original
producer packets and checks actual output corruptions. Native AutoCAD process
execution and visual table regeneration are not established by these checks.
