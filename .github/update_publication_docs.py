from pathlib import Path
root=Path('.')
base=root/'doc/dxf-conformance'
prs={
 'spline-knot-reversal':(69,15919,34859831437),
 'atomic-file-save':(70,16075,34861184112),
 'raw-handle-index':(71,16235,34862523340),
 'raw-handle-operations':(72,16315,34863629586),
 'empty-hatch-retention':(73,16507,34865164429),
}
for name,(pr,count,ci) in prs.items():
 p=base/(name+'.md'); s=p.read_text()
 first,body=s.split('\n',1)
 first=first.replace(' — unmerged implementation','').replace(' (unmerged implementation)','').replace(' — unmerged','')
 notice=(f'\n> Merged as [PR #{pr}](https://github.com/wieslawsoltes/netDxf/pull/{pr}). '
         f'Final-head [CI {ci}](https://github.com/wieslawsoltes/netDxf/actions/runs/{ci}) '
         f'passed Linux/Windows Debug/Release, actual netstandard2.0 builds, ledger checks and the Linux source audit; '
         f'the retained Linux Debug report has **{count:,} passed / zero failed**. '
         'The evidence section below retains the original standalone local experiment, not the later accumulated suite count. '
         'See the [merged execution checkpoint](checkpoint-merged-2026-09-14.md) for source hashes and qualification limits.\n')
 s=first+'\n'+notice+body
 s=s.replace('This increment is **local and unmerged**, not an existing remote PR.',
             'The standalone local experiment below preceded the PR publication recorded above.')
 s=s.replace('Actual SDK/MSBuild, Windows, netstandard2.0 CI and native AutoCAD are pending.',
             'This original local experiment did not execute SDK/MSBuild or Windows; the merged PR subsequently passed those CI gates as recorded above. Native AutoCAD remains unexecuted.')
 s=s.replace('Windows, SDK/MSBuild, netstandard2.0 and native AutoCAD runs have not been executed for this unmerged feature.',
             'This original experiment did not execute SDK/MSBuild or Windows. The merged PR subsequently passed those CI gates as recorded above; native AutoCAD remains unexecuted.')
 s=s.replace('Windows/SDK/netstandard\nCI and native AutoCAD are not executed.',
             'Windows/SDK/netstandard CI was not executed in that original experiment. The merged PR subsequently passed those gates; native AutoCAD remains unexecuted.')
 s=s.replace('Native AutoCAD, actual Windows, SDK/MSBuild and netstandard2.0 CI are not executed.',
             'The original local experiment did not execute Windows/SDK/netstandard CI. The merged PR subsequently passed those gates as recorded above. Native AutoCAD remains unexecuted.')
 if name in ('raw-handle-index','raw-handle-operations'):
  s+='\n## Embedded-object follow-up\n\nMerged [PR #75](raw-embedded-handle-context.md) keeps the recognized group-101 embedded-object tail opaque through the record boundary. Its private fields, including 100/102/1001-looking values, must not become enclosing identities or references. This is conservative exposure/remapping safety, not a private class schema.\n'
 p.write_text(s)

p=base/'README.md';s=p.read_text()
start=s.index('Recent implementation evidence:');end=s.index('\n\n## Updating the comparison',start)
s=s[:start]+'''Recent implementation evidence: [SPLINE reversal](spline-knot-reversal.md), [atomic saves](atomic-file-save.md), [raw handle indexing](raw-handle-index.md), [guarded remapping](raw-handle-operations.md), [empty HATCH retention](empty-hatch-retention.md), and [embedded-object safety](raw-embedded-handle-context.md). The comparison also reconciles [ACI metadata](hatch-gradient-aci.md), [MESH output validation](mesh-write-validation.md), [stored SPLINE clones](spline-clone-state.md), [Bezier domains](bezier-knot-parameterization.md), [periodic input](spline-periodic-input.md), [typed HELIX](helix.md) and [analytic authoring](helix-authoring.md).

The [merged 14 September checkpoint](checkpoint-merged-2026-09-14.md) records PRs #69–#75, 1,173 additional cases since PR #68, the 16,765-case production suite and exact final-head CI/source checks. The current comparison has 189 scoped rows across nine profiles. The [earlier 14 September checkpoint](checkpoint-2026-09-14.md), [13 September HATCH checkpoint](checkpoint-hatch-2026-09-13.md), [earlier checkpoint](checkpoint-2026-09-13.md) and [PR #50 HATCH audit](hatch-remaining-audit.md) remain historical, not current missing-feature lists.''' + s[end:]
p.write_text(s)
p=base/'checkpoint-2026-09-14.md';s=p.read_text();a,b=s.split('\n',1)
p.write_text(a+'\n\n> Historical PR #58 snapshot. For current merged support and PR #69–#75 verification, use the [later merged checkpoint](checkpoint-merged-2026-09-14.md). The original results below are retained, not replaced by later suite totals.\n'+b)

p=root/'README.md';s=p.read_text()
s=s.replace('The [14 September checkpoint](doc/dxf-conformance/checkpoint-2026-09-14.md) records the 173-row/nine-profile comparison and verified 12,907-case production snapshot after PR #58.',
'''The [merged 14 September checkpoint](doc/dxf-conformance/checkpoint-merged-2026-09-14.md) records the 189-row/nine-profile comparison and verified 16,765-case production snapshot after PR #75.''')
s=s.replace('It supports exact unedited same-transport saves and scoped raw edits. It does not add those historical versions to typed `DxfDocument`, evaluate unknown entities, repair handle dependencies, or provide an automatic fallback inside typed load/save.',
'''It supports exact unedited same-transport saves, scoped raw edits, an immutable contextual handle index, selected outgoing traversal and guarded simultaneous remapping of exposed interpreted handles. It does not add historical versions to typed `DxfDocument`, evaluate unknown entities, resolve private dependencies, produce dependency-complete cross-document imports, or provide an automatic fallback inside typed load/save.''')
needle='Full AutoCAD DXF capability is not yet achieved.'
s=s.replace(needle,'''Both pipelines now offer an explicit [SaveAtomic API](doc/dxf-conformance/atomic-file-save.md) for destination-byte protection through sibling-file staging and replacement. Existing `Save` overloads remain nontransactional. [Handle operations](doc/dxf-conformance/raw-handle-operations.md) reject ambiguous/colliding changes and affected opaque slots, including [embedded-object tails](doc/dxf-conformance/raw-embedded-handle-context.md); this does not infer references hidden in private strings or binary data.

'''+needle)
p.write_text(s)
