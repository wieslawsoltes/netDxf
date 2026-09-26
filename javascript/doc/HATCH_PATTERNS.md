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

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/HATCH_PATTERNS.md). Current published scope is maintained in the [README](../README.md).
