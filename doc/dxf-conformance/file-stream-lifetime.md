# Deterministic lifetime for file-path overloads

Baseline: `b2541b7dc52a27d8df34e1b31bfdad39055d63c5`, after merged PR #29.

## Corrected defect

`DxfDocument.Load(string, supportFolders)` opened the input file before constructing/enumerating support folders, outside its cleanup scope. Setup exceptions leaked file handles in both configurations. Debug builds additionally bypassed `Close()` on reader errors and writer errors in `Save(string, bool)`. Waiting for finalization is not deterministic cleanup and can leave file locks in place.

Place each owned file stream in a `using` scope that includes setup and parsing/writing, and use the same lifetime pattern in the file-path version probe. Retain the existing Debug exception / Release null-or-false conventions and the always-thrown unsupported-version exception. Setup/open errors still propagate, rather than being silently changed to Release null. Stream overloads still leave caller-owned streams open.

This is resource-lifetime correction, not transactional output. `File.Create` still truncates existing destinations before serialization, file names and support-folder metadata retain their existing behavior, and IO errors during disposal can propagate. File replacement, global working-directory mutation and reader/writer wrapper disposal are separate concerns.

## Version and test coverage

| File operation | 2000 / AC1015 | 2004 / AC1018 | 2007 / AC1021 | 2010 / AC1024 | 2013 / AC1027 | 2018 / AC1032 |
|---|---|---|---|---|---|---|
| Load, probe, save and reload | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary |
| Setup and malformed-load failures | Tested | Tested | Tested | Tested | Tested | Tested |
| Failed-export cleanup | Tested | Tested | Tested | Tested | Tested | Tested |

65 new registered cases exercise null and throwing support-folder enumerables, truncated inputs, deterministic CLASS preflight failures, unsupported declared versions, open failures, successful metadata/geometry, exclusive reopening and caller-owned streams. Linux tests inspect `/proc/self/fd` because deleting a Unix file or checking FileShare alone is not an adequate leak oracle. Windows tests immediately reopen with exclusive sharing. Finalization is prevented where possible until after the ownership assertion; failed red tests clean up afterward.

With unchanged production code: **5,259 passed / 50 failed in Debug**, and **5,285 passed / 24 failed in Release**. With owned-stream scopes: **5,309 passed / 0 failed**, both configurations, using the local signed-library .NET 8 workbench. Final-head Linux/Windows SDK, netstandard2.0 and source-audit CI are additional merge gates. Existing targets and strong naming are unchanged; compiling older targets is not older-runtime execution evidence.

## Primary reference

Microsoft's C# using statement guarantees disposal on block exit, including exceptions and early returns: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/using
