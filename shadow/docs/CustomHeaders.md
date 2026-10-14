# Custom-header synchronization

The source adapter exposes the public `HeaderVariables.CustomValues()` collection as a detached, writable `CustomVariables` sequence. Entries retain stable source identities and their exact name, group code and typed value. Standard header properties continue through their existing accessors; no private fields are reflected. Snapshot capture, external-change detection, optimistic graph adoption, source-backed history and isolated branches use the same data.

`CustomHeaderEdits.Set(snapshot, name, groupCode, value)` and `Remove(snapshot, name)` return immutable graph patches. Execute them through `EditorSession.Execute` for canonical source validation and undo/redo. Names are matched ordinally without case; same-code updates preserve the existing spelling and identity. A type/code replacement preserves position, prunes unreachable old entries and is one history transaction. No-op writes and missing removals do not create history.

```csharp
var patch = CustomHeaderEdits.Set(editor.Snapshot, "$MSLTSCALE", 70,
    new ShadowValue.Atom("System.Int16", "1"));
editor.Execute("Enable model annotation linetype scaling", patch);
editor.Undo();
editor.Redo();
```

The adapter validates every replacement entry before clearing the live dictionary: known-variable collisions, duplicate names, invalid packet separators, incompatible primitive types, nonfinite coordinates and malformed strings are rejected. Header group 20/30 follows netDxf's Vector2/Vector3 convention. Numeric, Boolean, string, handle and bounded binary values retain their primitive representation. Object-typed values are converted only through an explicit CLR primitive/vector whitelist, never through assembly/type loading. Byte arrays are reconstructed from detached byte atoms.

A synthetic accessor returns a BCL list, not an array of netDxf objects: the latter would be treated as a reference object by the existing capture rules and allocate a fresh identity on each read. New snapshots remain structurally shared on no-op refresh. Capturing existing invalid public source data remains separate from accepting an invalid edit; the adapter does not normalize the source during a read.

The existing netDxf header writer determines version-specific persistence and special-variable handling. This change does not invent semantics for unknown variables, decode annotation scale dictionaries, or bypass existing writer exclusions. Atomicity is at the serialized adapter boundary; it does not suppress arbitrary external event handlers. The source's rollback/fail-stop policy remains unchanged.

The added executable cases cover typed values (including 64-bit integers beyond double precision), structural and scalar history, code changes, removal, conflict detection, source save/reload and isolated graph adoption. All previous source/merge/allocation assertions remain enabled.
