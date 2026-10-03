from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1]).resolve()
here = Path(__file__).resolve().parent
for relative in ('tools/verify_r12_selection_graphs.py', 'tests/dxf_coverage/test_r12_selection_graphs.py'):
    target = root / relative
    if target.exists():
        raise SystemExit('Refusing to overwrite existing graph verification source: ' + relative)
    shutil.copyfile(here / target.name, target)

guide = root / 'docs/r12-selection-interchange.md'
if guide.exists():
    raise SystemExit('Refusing to overwrite an existing selection guide')
guide.write_text('''# R12 selection interchange

`DxfR12Codec` supports a strict subset of AC1009 selections, now including reachable
acyclic BLOCK/INSERT graphs, array references, ATTDEF/ATTRIB and SEQEND sequences.
It preserves shared block identity rather than exploding geometry. Source resource
handles and ownership are not changed by export. Malformed selected packets and
unsupported selected semantics reject instead of being silently discarded.

## Reusable conversion plans

```csharp
using netDxf.Header;
using netDxf.IO;

var plan = DxfR12SelectionPlan.Prepare(selectedEntities);
using (var output = System.IO.File.Create("selection.dxf"))
{
    plan.Save(output, DxfVersion.AutoCad2018, binary: false);
}
var independentDocument = plan.CreateDocument(DxfVersion.AutoCad2007);
```

A plan captures an immutable normalized AC1009 selection. Mutating the original
entities after preparation does not change the plan. Every `CreateDocument` call
creates an independent editable graph in a new unitless model-space document.
Shared layers, linetypes, styles, block definitions and attribute definitions remain
canonical within each output, including settings for Layer 0 and Standard.

`Save` supports R12 and the six typed database families represented by AutoCad2000,
AutoCad2004, AutoCad2007, AutoCad2010, AutoCad2013 and AutoCad2018. These are database
families, not a claim of acceptance by every application release.

## Attributes and graph policies

Classic attribute flags, supported text alignment, OCS placement, text generation
flags, values/prompts and shared style/layer references survive interchange.
Orphan attribute instances and explicit empty sequences are preserved. Attribute
positions are not regenerated and INSERT arrays are not expanded. Block graph
validation and modern-document adoption are iterative; regressions include
512-level chains. This is not a performance benchmark or unlimited depth claim.

Same-named source block objects must be the same instance. Distinct same-named
blocks require a collision policy and reject. Recursive graphs, xrefs, layout
blocks, nonunitless block settings, external metadata and unsupported modern
attribute fields require a different profile. Nonzero legacy attribute field
length is rejected because the typed model has no corresponding setting.

## Stream and resource limits

Preparation enumerates an entity selection once and must not overlap source
mutation. Completed plans support concurrent readers. The complete output is
staged before the first caller-stream write. `DxfRawOptions` bounds encoded bytes,
tag count and decoded string length; it does not impose a total managed-heap cap.
Cancellation is observed between phases and during output copying.

Validation failure or cancellation before copying leaves the destination untouched.
An IO failure or cancellation during copying may leave partial output. Caller
streams remain open; existing suffixes are not truncated. Atomic file replacement
remains the caller's responsibility. The sample above is intentionally a new file,
not an atomic replacement recipe.

## Remaining boundaries

This is selection conversion, not whole-document historical loading or lossless
all-version conversion. Unselected HEADER/OBJECTS/layout information and lexical
formatting remain only in the original raw document. Original handle identities
are not retained in newly generated drawings. No tessellation, complex-linetype
flattening, proxy reconstruction, ACIS kernel, dynamic-block evaluator or native
AutoCAD acceptance claim is added by this change. R13/R14 typed conversion and
pre-R12 typed interchange remain outside this API.

## Verification

The C# tests export 26 drawings: six block round-trips, six attribute round-trips,
and fourteen text/binary selections across seven database families. The independent
checker requires that exact inventory, compares complete R12 packets, inspects
modern graph geometry/resources and requires zero independent audit repairs or
errors. Checker self-tests protect packet fields and missing/extra inventory.
Oracle self-tests are not substitutes for actual C# execution or full CI qualification.

Primary contracts: Autodesk DXF references for BLOCK, INSERT, ATTDEF, ATTRIB and
HEADER database families. Full-source CI remains the release gate.
''', encoding='utf-8')
