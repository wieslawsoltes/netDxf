"""One-shot source-pinned ledger refresh; removed from the published documentation tree."""
import json
from pathlib import Path
import sys

commit, tree, ci_run = sys.argv[1:]
p = Path('doc/dxf-conformance/coverage.json')
d = json.loads(p.read_text())
assert len(d['features']) == 173
profiles = [x['id'] for x in d['profiles']]
features = {x['id']: x for x in d['features']}

def evidence(key, name, path):
    d['evidence'][key] = {'label': name, 'path': 'doc/dxf-conformance/' + path + '.md'}

for key, name, path in [
    ('HACI', 'gradient optional ACI retention', 'hatch-gradient-aci'),
    ('MWRITE', 'MESH writer topology preflight', 'mesh-write-validation'),
    ('SCLONE', 'stored SPLINE cloning', 'spline-clone-state'),
    ('TCOM', 'typed comment handling', 'typed-comments'),
    ('BEZK', 'Bezier knot parameterization', 'bezier-knot-parameterization'),
    ('SFLAG', 'SPLINE output flags', 'spline-export-flags'),
    ('SPER', 'standard periodic input', 'spline-periodic-input'),
    ('HELIX', 'typed HELIX parameters', 'helix'),
    ('HAUTH', 'analytic HELIX authoring', 'helix-authoring'),
    ('SREV', 'knot-aware SPLINE reversal', 'spline-knot-reversal'),
    ('ATOMIC', 'explicit atomic file saving', 'atomic-file-save'),
    ('HINDEX', 'contextual raw handle index', 'raw-handle-index'),
    ('HOPS', 'selected traversal and simultaneous remapping', 'raw-handle-operations'),
    ('HEMPTY', 'empty HATCH input and safe export rejection', 'empty-hatch-retention'),
    ('EMBED', 'opaque embedded-object handle context', 'raw-embedded-handle-context')
]:
    evidence(key, name, path)

def set_row(identity, title, scope, refs, pipeline='typed', default='T', overrides=None):
    row = features.get(identity)
    if row is None:
        row = {'id': identity}
        features[identity] = row
        d['features'].append(row)
    row.update(area='Raw preservation pipeline' if pipeline == 'raw' else 'Implemented field-level increments',
               pipeline=pipeline, feature=title,
               status={x: ('X' if pipeline == 'typed' and not d['profiles'][i]['typed'] else (overrides or {}).get(x, default))
                       for i, x in enumerate(profiles)}, scope=scope, evidence=refs.split())

set_row('typed-spline', 'SPLINE', 'Partial typed family. Stored clone state, Bezier domains, flag composition, supported periodic input, tangent transforms and knot-aware reversal are tested separately; full NURBS/degenerate schemas remain incomplete.', 'B STANGENT SCLONE BEZK SFLAG SPER SREV', default='P')
set_row('typed-mesh-subdivision-mesh', 'MESH: subdivision mesh', 'Partial typed family. Core topology, creases, blend flags, counted input and mutable writer preflight are tested; subentity overrides and subdivision evaluation remain incomplete.', 'MVER MBLEND MREAD MWRITE', default='P', overrides={'AC1015':'V','AC1018':'V','AC1021':'V'})
set_row('typed-helix', 'HELIX', 'PR67/68: inherited SPLINE and independent finite HELIX parameters, clones, conservative2007+ output and explicit analytic authoring. No implicit constraint solving; inherited complete SPLINE grammar remains partial.', 'HELIX HAUTH', default='P', overrides={'AC1015':'V','AC1018':'V'})
set_row('hatch-gradient-color-state', 'HATCH authored gradient RGB, tint and mode', 'Authored RGB endpoints, dormant tint, finite tint validation and independent clone/mode state. Optional ACI retention is separately implemented by PR60. AC1015 still downgrades gradients.', 'HCOLOR', overrides={'AC1015':'L'})
set_row('hatch-gradient-aci-preservation', 'HATCH gradient optional ACI metadata', 'PR60: exact per-stop Int16 value/absence independent of RGB, tint and mode; clones retain automatic/explicit/absent authoring state. AC1015 output still omits gradients.', 'HACI', overrides={'AC1015':'L'})
for identity, title, scope, refs, overrides in [
 ('mesh-mutable-output-validation','MESH mutable output preflight','PR61: finite vertices/creases, face sizes, serialized Int32 limits and topology indices across registered blocks. Stream preflight, not manifoldness or subdivision evaluation.','MWRITE',{'AC1015':'V','AC1018':'V','AC1021':'V'}),
 ('spline-stored-clone-state','SPLINE clone without refitting','PR62: independent exact control/fit representations, weights, knots, tolerances, tangents and common metadata, without regeneration.','SCLONE',{}),
 ('typed-global-ascii-comments','Typed ASCII comment skipping','PR63: skip group999 in typed text parsers while retaining the leading preamble. Raw codecs retain tags; binary comment rejection unchanged.','TCOM',{}),
 ('bezier-composite-knot-domains','Composite Bezier knot domains','PR64: equal normalized spans for authored quadratic/cubic chains and constructor validation. Imported and explicitly supplied knots unchanged.','BEZK',{}),
 ('spline-output-flag-composition','SPLINE output flag composition','PR65: closure, periodic/rational and compatibility flags composed by OR, not ordinal addition. No universal historical flag or geometry-regeneration claim.','SFLAG',{}),
 ('spline-standard-periodic-input','Standard periodic SPLINE input','PR66: standard bit2 independent of legacy2048; retain exact degree-fold overlap/weights in supported compact layouts; reject nonrepresentable layouts.','SPER',{}),
 ('helix-parameter-persistence','HELIX separate parameter persistence','PR67: AcDbHelix and inherited AcDbSpline remain independently authored. Conservative2007+ export, class counts, clones and selected transforms.','HELIX',{'AC1015':'V','AC1018':'V'}),
 ('helix-analytic-authoring','HELIX analytic authoring','PR68: cylindrical/tapered/fractional helices and planar spirals; signed pitch/zero radii; explicit cubic approximation with segment budget and truncation bound excluding floating-point error.','HAUTH',{'AC1015':'V','AC1018':'V'}),
 ('spline-knot-aware-reversal','SPLINE knot-aware reversal','PR69: reflect full knot domain and reverse periodic control/weight phase plus fit/tangent state. Preflight before mutation; no refitting or automatic HELIX parameter reconciliation.','SREV',{}),
 ('hatch-empty-input-retention','Empty HATCH typed input retention','PR73: preserve identity, fill metadata, seeds, elevation and XData on zero/absent boundary lists; clones remain independent. Typed output rejection is a separate row.','HEMPTY',{}),
]:
    set_row(identity, title, scope, refs, overrides=overrides)
set_row('typed-transactional-save', 'Explicit typed atomic file replacement (SaveAtomic)', 'PR70: sibling staging, flush and single replacement/move, cancellation and failure cleanup. Opt-in destination-byte protection, not all-document rollback, power-loss durability or hostile-filesystem isolation.', 'ATOMIC')
set_row('typed-legacy-save-nontransactional', 'Existing Save file-path behavior', 'The original Save overload still creates/truncates before serialization. Deterministic disposal is not a transaction; SaveAtomic is a separate explicit API.', 'FILE ATOMIC', default='L')
set_row('hatch-empty-boundary-preservation', 'Empty HATCH typed export', 'PR73: reject zero-boundary HATCH in all registered blocks before preprocessing or caller-stream writes. Input is retained; no invented boundaries or empty-HATCH rendering guarantee.', 'HEMPTY', default='V')
set_row('raw-graph-remapping', 'Raw dependency-closed clone/import/remapping', 'Partial: contextual index, selected outgoing traversal and guarded simultaneous exposed-handle renaming exist. Lexical aggregate extraction, cross-document import, private references and typed integration remain missing.', 'HINDEX HOPS EMBED', pipeline='raw', default='P')
for identity, title, scope, refs in [
 ('raw-atomic-file-save','Raw atomic file replacement','PR70: same-transport preservation or explicit conversion staged beside the destination before replacement/move. Failure cleanup and cancellation; no destructive fallback or full durability guarantee.','ATOMIC'),
 ('raw-contextual-handle-index','Contextual raw handle index','PR71/75: identities, common owners, pointers, reactors, dictionaries, XData, arbitrary handles and seeds with diagnostic/occurrence budgets. Embedded/private payloads stay opaque; no historical-schema certification.','HINDEX EMBED'),
 ('raw-selected-reference-traversal','Selected raw outgoing reference traversal','PR72: selectable interpreted outgoing links with unresolved, ambiguous and opaque evidence. Not a standalone drawing extractor or proof of private dependency completeness.','HOPS EMBED'),
 ('raw-simultaneous-handle-remap','Guarded raw simultaneous handle remapping','PR72/75: permutations, collision/ambiguity/dangling-capture/affected-opaque guards, seed advancement, immutable snapshots and numeric no-op behavior. Private strings/binary and arbitrary handles are not inferred references.','HOPS EMBED'),
 ('raw-embedded-handle-isolation','Raw embedded-object handle isolation','PR75: recognize the exposed101 Embedded Object separator outside established private controls/payloads; retain its entire tail as opaque until the record boundary. No typed columns or implicit post-embedded XData grammar.','EMBED'),
]:
    set_row(identity, title, scope, refs, pipeline='raw')

# Reconcile broad descriptions as well as narrow support cells.
d['status_definitions']['P'] = 'The named pipeline has a partial implementation or lacks comprehensive verification.'
for identity, scope, extra in [
 ('typed-target-version-downgrade-diagnostics', 'Selected guards for MESH, HELIX, HATCH, MTEXT and VIEW. No centralized complete schema legality or per-loss report. Explicit SaveAtomic is separately available; existing Save remains nontransactional.', ['ATOMIC','HELIX','HEMPTY']),
 ('typed-hatch-solid-patterned', 'Tested subsets include seeds, pixel size, XData, flags, closure, bulges, counted packets, fit metadata, outer path framing and empty-input retention. Empty output rejects. General geometry, affine validity and associations remain partial.', ['HEMPTY']),
 ('typed-hatch-gradients', 'AC1015 output drops gradient payload. Later profiles retain rotation, continuous shift, authored RGB/tint/mode, checked two-stop packets and exact optional ACI metadata. Reserved future forms and complete appearance/evaluation remain partial.', ['HACI']),
 ('file-disposal', 'Owned file streams dispose on all success/setup/read/write/probe exits; caller streams stay open. Existing Save remains nontransactional; explicit SaveAtomic is a separate operation.', ['ATOMIC'])
]:
    features[identity]['scope'] = scope
    features[identity]['evidence'] = list(dict.fromkeys(features[identity]['evidence'] + extra))

d['audit_date'] = '2026-09-14'
d['source'].update(commit=commit, tree=tree)
d['qualification'].update(full_standard_complete=False, autocad_executed=False,
    dotnet_runtime='8.0', test_cases=16765, test_failures=0, independent_reader='ezdxf 1.4.4',
    last_implementation_pr=75, ci_run=int(ci_run))
d['next_work'] = [
 {'id':'opaque-graph','title':'Class-specific dependencies and typed integration','scope':'Embedded/private payload grammars, standalone BLOCK/POLYLINE aggregate extraction, cross-document clone/import and typed unknown-record preservation. Selected raw traversal/remapping is implemented, not complete semantic closure.'},
 {'id':'field-fidelity','title':'Finish partial typed records','scope':'MTEXT columns and linked legacy contexts; HATCH inner scalar ordering, spline relations, full affine geometry and associative source closure; MESH subentity overrides; extended UCS/VIEW/VPORT relationships. Existing Save remains nontransactional; SaveAtomic is opt-in.'},
 {'id':'new-entities','title':'Missing typed entity families','scope':'MULTILEADER, structured TABLE, LIGHT/SECTION and inert OLE/proxy/ACIS/surface payloads. HELIX is already implemented with scoped limitations, not a missing family.'},
 {'id':'new-objects','title':'Missing object families','scope':'Generic XRECORD/dictionary variants, draw order/spatial filters, MLEADERSTYLE/TABLESTYLE, FIELD/DIMASSOC/GEODATA, MATERIAL/VISUALSTYLE/rendering and sun families.'},
 {'id':'historical-typed','title':'Historical typed dialects and schema-aware down-save','scope':'R12/R13/R14 typed grammar, earlier raw profiles, headerless inference and explicit per-property downgrade reports. Marker safety on raw profiles is not proof of historical feature legality.'},
 {'id':'qualification','title':'Native interoperability and robustness','scope':'Native AutoCAD open/AUDIT/save/reopen remains unexecuted. Expand independent corpora, resource accounting, fuzzing and actual older-runtime execution; netstandard2.0 compilation is not execution on every consumer runtime.'}
]
assert len(d['features']) == 189
p.write_text(json.dumps(d, indent=2, ensure_ascii=False) + '\n')
