from pathlib import Path
import json,sys
root=Path.cwd()
p=root/'doc/dxf-conformance/coverage.json';d=json.loads(p.read_text())
commit=sys.argv[1]
d['source'].update(commit=commit,tree='365eec7a7c69da87838a2f8e7cfc286b7dc84a27')
d['qualification'].update(test_cases=9140,test_failures=0,last_implementation_pr=50,ci_run=34780590182)
evidence={
 'HPIX':('hatch pixel size','hatch-pixel-size.md'),
 'HXDATA':('hatch ACAD XData preservation','hatch-xdata-preservation.md'),
 'HCLOSE':('hatch polyline closure','hatch-polyline-closure.md'),
 'HPOLY':('hatch polyline input','hatch-polyline-input.md'),
 'HPAT':('hatch pattern list grammar','hatch-pattern-lists.md'),
 'HFLAGS':('hatch boundary classification','hatch-boundary-flags.md'),
 'HORDER':('hatch pattern metadata order','hatch-pattern-order.md'),
 'HEDGE':('hatch edge packet validation','hatch-edge-packets.md'),
 'HGAPS':('remaining hatch source audit','hatch-remaining-audit.md'),
}
for key,(label,path) in evidence.items():
 d['evidence'][key]={'label':label,'path':'doc/dxf-conformance/'+path}
rows={row['id']:row for row in d['features']}
def status(mark):return {p['id']:mark if p['typed'] else 'X' for p in d['profiles']}
f=rows['hatch-pixel-size']
f.update(area='Implemented field-level increments',status=status('T'),scope='Nullable group47 retains absence/zero/finite values; clone/INSERT and export. Sampling hint is stored, not evaluated.',evidence=['HPIX'])
f=rows['typed-hatch-solid-patterned']
f.update(scope='Double flag, seeds, pixel size, ACAD XData, closure, classification, sparse bulges, counted edge/pattern packets and final metadata conversion tested separately. Outer grammar, geometry, fit metadata and dependencies remain partial.',evidence=['HDOUBLE','HSEED','HPIX','HXDATA','HCLOSE','HPOLY','HPAT','HFLAGS','HORDER','HEDGE'])
f=rows['typed-hatch-spline-fit-boundary-data']
f.update(status=status('L'),scope='Typed spline has no fit/tangent storage. Existing 2010+ packet is validated then discarded; writer emits zero fit count. Earlier typed profiles do not consume that packet. Historical availability is not inferred.',evidence=['HEDGE','HGAPS'])
f=rows['typed-hatch-gradients']
f.update(scope='2000 output drops gradient payload. Later typed payload is partial: fractional shift becomes a Boolean endpoint; gradient rotation is overwritten by pattern angle. Color-stop/tint and packet fidelity remain open.',evidence=['HGAPS','B'])
new=[
 ('hatch-acad-xdata-origin','HATCH ACAD XData and pattern-origin projection','Scoped origin update preserves unrelated ACAD records without mutating source XData; no general application-schema validation.','HXDATA'),
 ('hatch-polyline-closure','HATCH polyline closure','Group73 retained through loading/cloning/INSERT; open paths stay open and conversion does not invent a closing segment.','HCLOSE'),
 ('hatch-polyline-input','HATCH optional bulges and counted polyline lists','Optional42 defaults zero; checked components/counts/references, comments, adjacent data and incremental storage. Writer normalizes zero bulges.','HPOLY'),
 ('hatch-pattern-lists','HATCH counted pattern lines and dashes','Group78/53 lines, keyed43-46 fields and counted79/49 dash packets; rejects negative/duplicate/surplus data; local comments allowed.','HPAT'),
 ('hatch-boundary-flags','HATCH boundary classification','Exact group92 bits through read/clone/transform; only structural Polyline bit follows representation changes; constructor defaults unchanged.','HFLAGS'),
 ('hatch-pattern-order','HATCH pattern metadata ordering','Intact pattern packets and scalar metadata can reorder; PAT-local conversion happens once final angle/scale are known. Gradient packet remains separate.','HORDER'),
 ('hatch-edge-packets','HATCH edge dispatch and counted packets','Rejects non-progressing unknown kinds; checked LINE/ARC/ELLIPSE/SPLINE components and incremental lists. Fit/tangent framing is not retention or geometric validation.','HEDGE'),
]
for id,label,scope,ref in new:
 assert id not in rows
 d['features'].append({'id':id,'area':'Implemented field-level increments','pipeline':'typed','feature':label,'status':status('T'),'scope':scope,'evidence':[ref]})
for id,label,scope in [
 ('hatch-gradient-shift','HATCH fractional gradient shift','Source audit: group461 is reduced to bool Centered and export emits only0/1. Fractional values are not retained; 2000 omits the gradient payload.'),
 ('hatch-gradient-rotation','HATCH gradient rotation','Source audit: group460 is read into gradient Angle, then overwritten by group52 patternAngle/default zero in ReadHatch. Not fixed by pattern-order support.'),
]:
 assert id not in rows
 d['features'].append({'id':id,'area':'Confirmed next field-level work','pipeline':'typed','feature':label,'status':status('L'),'scope':scope,'evidence':['HGAPS']})
for row in d['next_work']:
 if row['id']=='field-fidelity':
  row['scope']='HATCH fit/tangent retention, gradient shift/rotation/color metadata, outer path grammar and geometric/affine validity; MESH overrides/writer checks; MTEXT columns; complete UCS/VIEW/VPORT contexts. Each is an isolated PR.'
p.write_text(json.dumps(d,indent=2,ensure_ascii=False)+'\n')
p=root/'doc/dxf-conformance/README.md';s=p.read_text()
a=s.index('Recent implementation evidence:');b=s.index('\n\n',a)
s=s[:a]+'Recent implementation evidence: [HATCH edge packets](hatch-edge-packets.md), [pattern ordering](hatch-pattern-order.md), [boundary classification](hatch-boundary-flags.md), [pattern lists](hatch-pattern-lists.md), [sparse polyline bulges](hatch-polyline-input.md), [closure](hatch-polyline-closure.md), [ACAD XData](hatch-xdata-preservation.md), [pixel size](hatch-pixel-size.md), [seed points](hatch-seed-points.md), and [MESH input validation](mesh-read-validation.md). The matrix also links raw preservation, legacy profiles and all earlier increments.'+s[b:]
s=s.replace('The [13 September execution checkpoint](checkpoint-2026-09-13.md) records merged PRs #38–#41, exact CI runs and the next unimplemented scopes.', 'The [latest HATCH execution checkpoint](checkpoint-hatch-2026-09-13.md) records PRs #45–#50 and the integrated 9,140-case production snapshot. The [earlier checkpoint](checkpoint-2026-09-13.md) remains historical. [Remaining HATCH findings](hatch-remaining-audit.md) distinguish source-observed losses from implemented packet validation.')
p.write_text(s)
print('Updated',len(d['features']),'rows')
