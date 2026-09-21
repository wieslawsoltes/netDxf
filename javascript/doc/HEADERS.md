# Drawing header variables — c2d3581

## Published scope and source pin

Runtime commit `3a9d56a1fa60c632b73040a7ffea8cf682c22077` and verification commit `c2d35810443788fad62ffc5be45d6001b9161964` remain on draft PR #98, branch `codex/javascript-port`. The verification tree is `7cc18a81ed25f19f18c25065ab19a1c206c9ba6e`; it exactly matches the locally staged source. No merge, force push or npm publication occurred.

The unchanged C# reference is `3496ab91893a1e4ec9261b4833479f1799149cdc`, with SDK 8.0.425, runtime 8.0.31 and Node 22.16.0. Original C# sources, all 399 DXF fixtures and support assets are unmodified. The restored f90fa9f source archive reproduced tree `56c050cd71e29ef19d2bc03dc2c87a3562049790`. Archive SHA-256: `51233bdc779e0cbb5aed551eb4048126e8db12203e1d991c381e889e758b8d11` (Actions source artifact 10623937025). No surviving uncommitted checkout or archived HeaderVariables implementation was found; this increment was rebuilt from the pinned C# implementation, not recovered from unavailable bytes.

## Model and language adaptations

`netDxf/Header/HeaderVariables.js` mirrors all 40 built-in entries, their source ordering and group codes, setters, CurrentUCS reference and custom-variable methods. Name/value lists are independently mutable snapshots containing the same live HeaderVariable entries. Editing an entry changes the model; removing it from a snapshot does not remove its internal dictionary entry. Custom variables use the existing ordinal-ignore-case dictionary, including duplicate admission and removed-slot reuse.

Validation intentionally preserves source behavior. For example, positive-scale setters reject nonpositive values but admit NaN/infinity where C# does; name properties are not silently trimmed or sanitized. Architectural/engineering LUnits set InsUnits to inches. Switching back to decimal does not restore a previous insertion unit. Direct edits to known entries bypass property setters and retain the source's boxed-value cast failures.

An object-valued HeaderVariable cannot represent CLR integer/enum distinctions with JavaScript Number alone. Known short values therefore use `BoxedScalar('Int16', value)` and known enums use immutable `HeaderEnum` adapters; typed model getters/setters still use ordinary numbers. The existing generated HeaderVariable body is unchanged. Shared display formatting was extracted into `runtime/DisplayFormatting.js`; invariant boolean spelling and boxed scalar display were corrected without replacing numeric formatting.

`HeaderDateTime` and `HeaderTimeSpan` are immutable **value adapters**, not complete implementations of System.DateTime/System.TimeSpan. They preserve 100 ns ticks as BigInt, DateTimeKind, calendar components, signed Int64 duration endpoints and invariant display strings. DateTime equality follows ticks, not Kind. The optional `HeaderDateTime.FromDate` conversion starts from JavaScript's millisecond Date precision; it cannot recover ticks that were never supplied.

Browser defaults use local/UTC millisecond clocks and an empty username because browser code cannot read the operating-system login. `SetHeaderEnvironment` or the optional JavaScript constructor host argument supplies UserName/Now/UtcNow functions. The explicit Node entry installs the Node host username. Independent processes' live clock values are not claimed bit-identical. Locale-specific date formatting, full timezone/DST conversion and complete CLR time APIs remain outside this increment.

## Independent verification

The 665-scenario input corpus contains no expected models. C# and JavaScript snapshots observe their own production implementations; exact binary64 bits, strings, ticks, enum/box types, exceptions and reference relationships are compared without tolerances. Each scenario that observes header dates/usernames explicitly assigns the same input values through the APIs first. This is not comparator normalization. Constructor clock call order, immutable tick storage, host injection, browser defaults and Node identity are separately tested.

| Category | Scenarios | Operations |
| --- | ---: | ---: |
| Known variables, defaults and setters | 401 | 3,786 |
| Timestamp and duration storage | 105 | 1,050 |
| Custom names, dictionaries and snapshots | 33 | 408 |
| Box types, invalid casts and formatting | 126 | 1,352 |
| **Total** | **665** | **6,596** |

The shared test-only model interpreter is split into descriptor/type resolution, snapshots and operations. The C# reflection runner retains its partial-class state and original operations. Existing corpus ordering and source-generated expected results are preserved. The enlarged browser corpus requires **128,754 comparisons**; header comparisons are mandatory in full Node qualification and both browser modes. A dedicated workflow also executes headers on Linux/Windows in Debug and Release.

## Completed local evidence

Both complete, unchanged C# suites pass **35,309 original cases**. All **2,819 mirrored original JavaScript cases**, **649 supplemental tests** and the **394-file offline installed package** pass. No new original C# case identities are counted here: the 28 added tests are supplemental. All **665 / 6,596** header comparisons pass in both Debug and Release. Native source regeneration, fixed foundations, exact randomized geometry and both 30,904-comparison independent development-reference MPFR audits pass.

Release Chromium 144.0.7559.96 executes all **128,754** inline-native-ESM comparisons with no page errors and zero header mismatches. It retains **69 existing failing scenarios**. Debug executes the same **128,754** comparisons with no page errors or header mismatches, retaining five failures: the Bezier result and four unavailable native viewport observations. Local HTTP-origin navigation is blocked by policy and is not treated as a pass or replaced by inline evidence.

The full differential runs retain these existing failures: Release has 128 entity-output observations, eight INSERT observations and three UCS observations; Debug retains one Bezier tangent observation and four unavailable native viewport observations caused by original Debug assertions. The local globalization profile is rejected in both configurations. These remain failures; no allowlist, tolerance or removed case was added. Descriptive benchmark completion is not broad performance acceptance.

The dedicated hosted header workflow passes all four Linux/Windows Debug/Release jobs at code commit c2d3581. [Downloaded hosted evidence and artifact identities](HEADERS_HOSTED.md) records the exact source-bound results. This does not qualify unrelated platform APIs or turn the full-port gate green.

## Completion ledger and remaining work

**283/510 library mirrors; 52/193 conformance-file mirrors; 2,819/35,309 original cases.** Missing: **227 library paths, 141 conformance paths and 32,490 original cases**. File presence is not exhaustive member/signature/behavioral equivalence. HeaderVariables adds one original-path library mirror; the runtime and test adapters are not additional C# file mirrors.

Typed DxfDocument, registered table/entity/database ownership, the complete typed reader/writer, remaining APIs and original tests/examples, exact cross-platform results and performance acceptance remain incomplete. These detached header APIs do not load or save typed DXF documents, and no native AutoCAD qualification is claimed. The package stays private and full-port completion stays blocked.

## Reproduce

From `javascript/`, select the pinned .NET toolchain and, when necessary, an unchanged C# checkout using NETDXF_SOURCE_ROOT:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:headers
node --test tests/unit/headers.js tests/unit/header-time.js
npm test
npm run test:unit
npm run test:package
npm run test:differential # Retains existing failures.
node tools/browser-corpus.mjs
python tools/browser-check.py
python tools/browser-inline-check.py
npm run verify # Writes the complete ledger even when qualification fails.
```

Reports are under `artifacts/header-differential/<configuration>/`, `artifacts/conformance/`, `artifacts/unit/`, `artifacts/package/` and `artifacts/verification/<configuration>/`.

Runtime fingerprint: `5a253e79e2675b8635f8f60f82a4eaceedf847da14e58eb425c05bcfa1f63f14`.

POSIX verifier: `35fe90660169c6d8fbcb61bb62636ed7ca439ff8540a4cc8e5220b58b2fbed76`.
