# Deterministic support-folder lookup without process-directory mutation

Baseline: `def5bede1a5fbf5d37fb12c3b6ca897d51380fb2`, after merged PR #31.

## Corrected defects

`SupportFolders.FindFile` changed `Environment.CurrentDirectory` to the drawing's working directory, performed relative file checks, and restored it afterward. Other threads could observe the temporary directory or resolve another drawing's resource. A restoration at the end cannot make a process-global mutation safe. Later support-folder matches also overwrote an already valid direct path, and the last matching folder won rather than the first.

Resolve paths against an explicit working-folder base without changing process state. An existing supplied path wins. Otherwise, search its basename in support-folder order and return the first match. Relative support folders use the drawing base; null/empty entries admitted by construction or AddRange are ignored. Null/empty filenames and missing files return an empty string. Results are full paths. The resource's contents are not opened or interpreted.

## Compatibility and path semantics

An absolute WorkingFolder is recommended for independently configured concurrent documents. A relative WorkingFolder is resolved once at lookup entry against the process directory; changes to that process directory by unrelated application code can still affect such relative configuration. The mutable collection does not become safe for concurrent mutation.

The working directory need not physically exist to resolve an absolute resource or search an absolute support folder. Invalid paths can propagate argument/path exceptions without changing process state. Directory results are not files. Parent traversal, absolute paths, network paths and symbolic links are not blocked: this resolver is not a resource sandbox or an authorization check, and a returned path is not a guarantee that a later file open sees the same filesystem object.

The private explicit-base resolver uses APIs available in the existing .NET Framework/netstandard2.0 targets, including Windows drive-relative, drive-rooted, UNC and device-base handling. Other-drive relative paths resolve at that drive's root rather than consulting a process-wide per-drive working directory. Relative dot segments joined to a device base are normalized within the root; already fully qualified device paths retain the platform's verbatim behavior. Modern-runtime path-oracle tests compare this compatibility code against `Path.GetFullPath(path, basePath)` without opening UNC/device resources.

```csharp
var folders = new SupportFolders(new[] { "fonts", "../shared/fonts" })
{
    WorkingFolder = Path.GetFullPath("drawings")
};
string resource = folders.FindFile("symbols.shx");
// Drawing-local symbols.shx wins; otherwise fonts precedes ../shared/fonts.
// Environment.CurrentDirectory is never assigned by FindFile.
```

## Version and regression coverage

The resolver is independent of the DXF wire version. Loaded documents are tested in all six admitted families (AC1015, AC1018, AC1021, AC1024, AC1027 and AC1032), text and binary, with relative support folders and Unicode resource filenames. No new dialect, XREF interpretation, font parser, image decoder or network policy is introduced.

The 18 behavioral tests against unchanged production code reported **5,427 passed / 17 failed**, in both Debug and Release. Failures include direct-path shadowing, incorrect folder priority, process-directory observation, independent concurrent lookup races, missing working directories and all twelve loaded-document cases. One path/control case passed as expected.

Two further tests exercise the new explicit-base resolver against the platform API and verify failure-path directory invariance. The complete corrected signed-library suite reports **5,446 passed / 0 failed**, local Debug and Release on .NET 8. This is 20 new registered cases over the 5,426 baseline, not a claim that two new private-helper tests ran against an API that did not yet exist. The observer is synchronized and joined; concurrent readers use separately configured collections, not concurrent mutations. Linux/Windows final-head SDK CI, netstandard2.0 compilation and source-audit checks are additional merge gates. Older target compilation is not older-runtime execution evidence.

## Primary references

- Microsoft documents that the current directory is shared by all threads and advises shared libraries against changing it: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setcurrentdirectory
- Microsoft explicit-base path resolution: https://learn.microsoft.com/en-us/dotnet/api/system.io.path.getfullpath
- Microsoft Windows path forms, including drive-relative and device paths: https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats
- Official .NET Windows path implementation inspected for explicit-base semantics, `Path.Windows.cs`, blob `f6976429fcbd3d4422eaa41125203b977864acb7`: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/Path.Windows.cs
