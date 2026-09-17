# Detached hatch patterns and PAT files

`HatchPattern` and `HatchGradientPattern` mirror the identically named pinned C# files. They are native JavaScript model objects, not replacements for the still-unported typed `Hatch`, `DxfDocument`, boundary geometry or typed DXF reader/writer.

## Portable model and text usage

```js
import {
  HatchPattern, HatchGradientPattern, HatchGradientPatternType, AciColor, Vector2,
} from './javascript/index.js';

const pattern = HatchPattern.Net; // Every preset access creates fresh data.
pattern.Origin = new Vector2(4, 5);
pattern.Angle = 37;
pattern.Scale = 2;
pattern.IsDouble = true;
const independentCopy = pattern.Clone();

const gradient = new HatchGradientPattern(
  AciColor.Red, 0.35, HatchGradientPatternType.Linear, 'Authored gradient',
);
gradient.Shift = 0.375;
gradient.Color1AciIndex = 17;  // Explicit metadata, independent of Color1 RGB.
gradient.Color2AciIndex = null; // Explicit absence, not automatic derivation.
gradient.Tint = 0.75;          // Recomputes Color2 only in single-color mode.

const text = '*EXAMPLE,Parallel lines\n0,0,0,0,0.125,0.25,-0.125\n';
const parsed = HatchPattern.LoadText(text, 'example');
console.log(parsed.LineDefinitions.Count, parsed.ToPatString());
```

`NamesFromText`, `LoadText` and `ToPatString` are explicitly JavaScript conveniences. They share the production PAT implementation with the original `NamesFromFile`, `Load` and `Save` methods. The default entry imports no Node modules. PAT file access without an explicitly installed host throws `NotSupportedException`; portable text operations work without a host.

## Node file usage

After the existing offline/internal package installation:

```js
import { HatchPattern } from '@netdxf/javascript/node';
const names = HatchPattern.NamesFromFile('patterns.pat');
const pattern = HatchPattern.Load('patterns.pat', 'ANSI31');
if (pattern !== null) pattern.Save('authored.pat');
```

`Save` **appends**, rather than replacing existing content. It emits UTF-8 without a BOM and uses the host's newline. PAT reading detects UTF-8, UTF-16 LE/BE and UTF-32 LE/BE BOMs, with replacement decoding for malformed byte sequences. UTF-8 output rejects unpaired UTF-16 surrogates. This adapter is separate from raw DXF atomic publication; it does not silently make PAT saves atomic or require the Windows replacement addon.

A custom synchronous host may be installed through `SetPatternFileSystem` in `runtime/PatternFileSystem.js`, providing `ReadAllText(file)`, `AppendAllText(file, text)` and `NewLine` (`\n` or `\r\n`). Passing `null` removes the host. Filesystem access/sharing, permissions, large-file limits and failure-time side effects are not claimed to have complete `System.IO` equivalence.

## Preserved source semantics

Names retain their original spelling. Only the exact name `SOLID` selects a solid fill in the base constructor; the source's XML comment saying names become uppercase is not implemented by the pinned C# body. Presets `Solid`, `Line`, `Net` and `Dots` are independent predefined objects.

Pattern origins use value-copy semantics. Constructors copy a supplied line collection but keep its line references; `Clone` deep-copies each line and dash list. `LineDefinitions` is a live reference collection with `Count`, iteration and `get_Item`/`set_Item` adapters. `IsDouble` retains metadata without inventing a second set of lines. Scale keeps the source's nonpositive-only guard: it does not gain an undocumented finite-value restriction.

Gradient RGB stops, dialog mode, tint, shift and optional ACI values remain separate. ACI state is automatic until assigned, an exact Int16 when explicitly assigned, or absent after assigning `null`. The reset methods restore automatic derivation. RGB replacement, tint edits and cloning preserve those three states. Setting `Color2` selects two-color mode only after successful validation; setting `Color1` does not regenerate Color2. Tint and shift must be finite in [0, 1]; rejected edits leave state unchanged. `Centered` projects exact zero and explicitly assigns 0 or 1. Clone retains authored RGB stops without replaying editing setters, even in single-color mode, and deep-copies dormant line definitions.

PAT name enumeration deliberately does not trim each line; lookup does. Lookup is ordinal-ignore-case, uses the first matching header and stops its definition on a blank line, comment or subsequent header. Numeric fields use invariant `NumberStyles.Float` behavior rather than permissive JavaScript `parseFloat`. Missing commas, short rows and a matching terminal header fail rather than silently inventing content. PAT does not serialize model origin, rotation, scale, double flags or gradient state. In particular, saving a zero-line solid/gradient model produces only a header, which the original loader rejects at EOF; no synthetic line is appended to hide that source behavior.

## Verification and completion accounting

The 52 added original cases retain their exact identities and complete model test bodies: 23 ACI API cases, 8 shift/default/rejection cases, 20 color-state API cases and one double-pattern clone case. The typed wire, nested `Hatch`/`Block`/`Insert`, legacy export and typed-document cases from those source files remain **unregistered and unported**, not shortened or counted as passing.

The new supplemental unit coverage exercises constructor/value/reference behavior, signed zero, input guards, parser termination, malformed numbers, both unchanged PAT support files (72 and 83 listed patterns), Unicode encodings, append behavior, and a child-process import hook that rejects Node-module dependencies from the portable entry.

`npm run test:hatch` compares 363 scenarios and 2,533 operations against the actual pinned production assembly, including all 155 listed support-file patterns, 64 seeded color-state scenarios and 174 expected successful PAT text comparisons. The oracle only wraps the original API and serializes results; expected values never come from JavaScript. Numeric comparisons retain exact double bits. Evidence is written to `artifacts/hatch-differential/<configuration>/results.json`, includes input hashes and code fingerprints, and is a required independent stage in both `test:differential` and `verify`.

## Completed checkpoint evidence — bd1a42f

Implementation commit `bd1a42f9a9692fdb98da71f41a84fea799dc4885` advances `06ecd49f3bd51aecc3a2ea50c134838cdfa01176` without rewriting prior work. The restored starting tree was verified against GitHub tree `4ebf3cdfa689331634cd647a20cea5b261bbc078`; the implementation tree is `be6ebd064a6ff4e4ac5766bf9fca110bc1dbf2ef`. No additional uncommitted files from the earlier session were available. Original C# source and shared fixture bytes remain unchanged.

[CI run 35210680084](https://github.com/wieslawsoltes/netDxf/actions/runs/35210680084) completed the following checks. Debug, Release and Windows artifacts were downloaded, SHA-256 checked and inspected. These results apply to the implementation commit, not merely to a later documentation-triggered rerun.

| Verification | Observed result |
| --- | --- |
| Complete pinned .NET suite | 35,309 passed in each Debug and Release configuration |
| Mirrored original JavaScript cases | 2,017 passed in each configuration; zero unexpected identities |
| New hatch/PAT differential | 363 scenarios, 2,533 operations and 174 exact PAT text comparisons in each configuration; zero mismatches |
| Supplemental suite | 110 passed on Linux and Windows; no skipped/TODO cases |
| Windows native-host integration | 5/5 passed |
| Original raw/helper atomic-save cases | 82/82 passed on Windows and Linux |
| Filesystem differential | 1,782 comparisons and 1,686 exact byte comparisons across 399 fixtures on each platform; zero mismatches |
| Raw / handle / OBJECTS byte comparisons | 8,263 / 1,140 / 1,068, with zero mismatches in each configuration |
| Detached typed lifecycle | 523 scenarios, 15,563 operations and 256 byte comparisons; zero mismatches in each configuration |
| Offline package install | Passed; 178 files on Linux, 180 with built Windows host/metadata |
| Existing real Chromium regression corpus | Release: 6,268 comparisons passed. Debug: the same 6,268 comparisons executed with the existing single Bézier NaN-sign mismatch. No page errors in either configuration. |

The browser regression corpus is not claimed to contain the newly added hatch differential corpus; that new corpus was compared using Node and the real .NET assembly. The portable-import dependency guard is supplemental evidence, not a substitute for a browser differential run of every new API.

Local validation independently passed all 2,017 original JavaScript cases, all 110 supplemental cases, offline package import/roundtrip, and the documentation example. No local .NET SDK was available; the live .NET and browser evidence above comes from CI.

**The overall JavaScript workflow still fails.** The 166 foundation mismatches and 142 randomized-geometry mismatches are unchanged, including their complete counterexample records. Debug additionally retains `BezierCurveCubic/CalculateTangent/double/5` in the baseline geometry and browser checks. No tolerance, expected-failure allowlist or removed comparison hides those results. Windows filesystem and the new hatch/PAT categories pass independently of those remaining failures.

The source ledger is **144/510 library mirrors (366 absent), 2,017/35,309 original cases (33,292 absent), and 22/193 conformance file mirrors (171 absent)**. File presence is not exhaustive member or behavioral qualification. Complete typed HATCH/document IO, entity ownership, the remaining original APIs/tests/examples and full platform/performance qualification remain unfinished. The package remains private and PR #98 remains a draft.

### Retained proof

Runtime fingerprint: `1292fdd20f4fc9c0e9b1827ae0052aabd9d5db4f2fd6ff8f7abb6f3046e41513`.

Verifier fingerprints were independently reproduced from the committed bytes: POSIX `a6e7a77d8f75bea35b0a8fce31811dfff7eda03ca90668ac90004d7e4568207f`; Windows `7ce94c3f38e6e6cd7aa60ecbd4070dc4d1f45f50451a3b7e40589e14fba47268`. The verifier sorts native paths before normalizing separators, which accounts for the platform-specific strings. Documentation-only changes do not change these fingerprints.

Downloaded artifact ZIP hashes:

- Release `10492320978`: `33d3c61e3f0c6afe54cb1b41777c1d3ef8d83b586504a73a7969f5a995b8da6e`.
- Debug `10492240694`: `4243079110b67b929fbac0c6d0afa676972296f5d65ea46b8e5b837a98ea3c73`.
- Windows `10492011358`: `cd2e86dd86a414e2fa202188a0822ccc6dc616c7e164a946fed5b55a1ec56b80`.

Recovery input was the exact `06ecd49` source checkpoint artifact `10490821649`, SHA-256 `76e7331a105533b44c7ca04aa53d78092cd9b26b778481c2dbd6687c807ab408`. It restored committed source; it did not reveal additional uncommitted edits.
