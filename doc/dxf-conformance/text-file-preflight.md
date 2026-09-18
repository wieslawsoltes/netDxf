# Text preflight before conventional file creation

This task extends the separate text-entity string guard to the conventional
`DxfDocument.Save(string, bool)` entry point. The stream writer's early rejection
alone is insufficient: the filename overload previously called `File.Create`
and changed the document name and working folder before invoking that writer.
Consequently, malformed text could truncate an existing file or create an empty
new file even though `Save` subsequently failed.

The existing opaque-entity filename preflight is now named
`PreflightFileSaveEntities` and also performs the text-entity validation before
opening the destination. It remains an internal method. In-memory setters,
valid file-save behavior and the separate `SaveAtomic` API are unchanged.

## Focused evidence

The same **1,008 file-system tests** pass **504 before** and **1,008 after** the
correction. The 504 existing `SaveAtomic` controls already pass; the 504
conventional filename cases expose and then close the truncation/name/folder
regression. Tests cover the six typed DXF profiles, both transports, existing
and absent destinations, six text fields in nested blocks, actual NUL, lone high
and low surrogates, and CRLF in the text transport. All output paths use an
isolated temporary directory and a Unicode filename.

After rejection, tests compare destination existence and exact original bytes,
check name/working folder and document-object inventories, assert that no
staging file or owned file descriptor remains, and check unchanged source text.
After repairing the content, they save successfully, verify the existing
name/folder update semantics and parse the resulting DXF profile. Temporary
files are removed in `finally` blocks. No native application is invoked.

```sh
DXF_TEST_FILTER=entity-text-file/ dotnet run --project tests/netDxf.Conformance -c Debug
```

Debug conventional saves throw `InvalidDataException`; Release conventional
saves return false. `SaveAtomic` propagates the exception in both configurations,
matching its existing contract.

## Deliberately bounded guarantee

This correction prevents destination/path changes for the covered text/opaque
preflight refusals. It does **not** turn every conventional `Save(string)`
serialization failure into an atomic transaction. For general serialization
failures, use the existing `SaveAtomic` API and its documented filesystem,
metadata, concurrency, durability and document-state limitations.

The guard visits TEXT/MTEXT content, ATTRIB values, ATTDEF values/prompts and
DIMENSION user text. It is not a complete policy for all private record strings,
all entity metadata or all filesystems. Final execution receipts distinguish
actual local Linux tests from unperformed Windows/remote CI qualification.
Historical typed dialects and full AutoCAD/DXF compatibility remain separate.
