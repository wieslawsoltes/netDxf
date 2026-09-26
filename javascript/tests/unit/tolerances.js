import test from 'node:test';
import assert from 'node:assert/strict';
import {Tolerance,ToleranceEntry,ToleranceValue,DatumReferenceValue,DimensionStyle,Vector2,Vector3,Matrix3,Matrix4,Culture,Block} from '../../index.js';
import {ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException,NullReferenceException} from '../../runtime/Errors.js';
import {doubleBits,fromBits} from '../../tools/wire.mjs';
const entry=()=>{const e=new ToleranceEntry();e.GeometricSymbol=1;e.Tolerance1=new ToleranceValue(true,'0.25',1);e.Datum1=new DatumReferenceValue('A',2);return e;};
test('TOLERANCE constructors snapshot vector values and retain the supplied entry reference',()=>{
 const p=new Vector2(2,3),e=entry(),t=new Tolerance(e,p);p.X=99;t.Position.Y=88;
 assert.deepEqual(t.Position.ToArray(),[2,3,0]);assert.equal(t.Entry1,e);assert.equal(t.Entry2,null);assert.equal(t.TextHeight,DimensionStyle.Default.TextHeight);
});
test('TOLERANCE emits exact five-cell framing and projected-zone symbols',()=>{
 const t=new Tolerance(entry());t.ProjectedToleranceZoneValue='12';t.ShowProjectedToleranceZoneSymbol=true;t.DatumIdentifier='ID';
 assert.equal(t.ToStringRepresentation(),'{\\Fgdt;j}%%v{\\Fgdt;n}0.25{\\Fgdt;m}%%v%%vA{\\Fgdt;l}%%v%%v^J12{\\Fgdt;p}^JID');
 const p=Tolerance.ParseStringRepresentation(t.ToStringRepresentation());assert.equal(p.Entry1.Tolerance1.Value,'0.25');assert.equal(p.Entry1.Datum1.MaterialCondition,2);assert.equal(p.ProjectedToleranceZoneValue,'12');assert.equal(p.DatumIdentifier,'ID');
});
test('all fourteen geometric symbols retain their source letter mapping',()=>{
 const letters='jrifbagceudkht';for(let i=0;i<letters.length;i++){const e=new ToleranceEntry();e.GeometricSymbol=i+1;const text=new Tolerance(e).ToStringRepresentation();assert.ok(text.startsWith('{\\Fgdt;'+letters[i]+'}'));assert.equal(Tolerance.ParseStringRepresentation(text).Entry1.GeometricSymbol,i+1);}
});
test('TOLERANCE parser retains source physical line positions rather than compacting entries',()=>{
 const t=Tolerance.ParseStringRepresentation('plain^J%%v2^J%%vignored');assert.equal(t.Entry1,null);assert.equal(t.Entry2.Tolerance1.Value,'2');assert.equal(t.DatumIdentifier,'');
 assert.equal(Tolerance.ParseStringRepresentation('%%v1^Jlast').DatumIdentifier,'last');
});
test('a projected zone on the final line follows the source datum-identifier rule',()=>{
 const t=new Tolerance();t.ProjectedToleranceZoneValue='12';t.ShowProjectedToleranceZoneSymbol=true;
 const p=Tolerance.ParseStringRepresentation(t.ToStringRepresentation());assert.equal(p.ProjectedToleranceZoneValue,'');assert.equal(p.ShowProjectedToleranceZoneSymbol,false);assert.equal(p.DatumIdentifier,'12{\\Fgdt;p}');
});
test('TryParse null clears the output value; empty input is a successful empty annotation',()=>{
 const out={value:'old'};assert.equal(Tolerance.TryParseStringRepresentation(null,out),false);assert.equal(out.value,null);
 assert.throws(()=>Tolerance.ParseStringRepresentation(null),{name:'ArgumentNullException',ParamName:'input'});assert.equal(Tolerance.TryParseStringRepresentation('',out),true);assert.equal(out.value.ToStringRepresentation(),'');
});
test('TOLERANCE style notifications run before replacement and can substitute null',()=>{
 const t=new Tolerance(),old=t.Style;let count=0;t.ToleranceStyleChanged.Add((_,e)=>{count++;assert.equal(t.Style,old);e.NewValue=null;});
 assert.throws(()=>{t.Style=null;},ArgumentNullException);assert.equal(count,0);t.Style=new DimensionStyle('Proposed');assert.equal(t.Style,null);assert.throws(()=>t.Clone(),NullReferenceException);
});
test('throwing style notifications leave the original resource assigned',()=>{
 const t=new Tolerance(),old=t.Style;t.ToleranceStyleChanged.Add(()=>{throw new InvalidOperationException();});assert.throws(()=>{t.Style=new DimensionStyle('Proposed');},InvalidOperationException);assert.equal(t.Style,old);
});
test('TOLERANCE cloning isolates entries cells styles XData and common stored bytes',()=>{
 const e=entry(),t=new Tolerance(e);t.Entry2=e;t.Handle='A';t.ProxyGraphics=Uint8Array.of(1,2);t.ColorName='Book';
 const q=t.Clone();assert.equal(q.Handle,null);assert.notEqual(q.Entry1,e);assert.notEqual(q.Entry1,q.Entry2);assert.notEqual(q.Style,t.Style);q.Entry1.Tolerance1.Value='edit';assert.equal(e.Tolerance1.Value,'0.25');assert.equal(q.Entry2.Tolerance1.Value,'0.25');assert.deepEqual([...q.ProxyGraphics],[1,2]);assert.equal(q.ColorName,'Book');
});
test('text-height guards are transactional and admit source-valid NaN payloads',()=>{
 const t=new Tolerance();for(const n of [-1,0,-0])assert.throws(()=>{t.TextHeight=n;},ArgumentOutOfRangeException);t.TextHeight=fromBits('FFF8000000001234');assert.equal(doubleBits(t.Clone().TextHeight),'FFF8000000001234');
});
test('TOLERANCE affine transforms update position orientation and text height',()=>{
 const t=new Tolerance(null,new Vector3(1,2,3));t.TextHeight=2;t.TransformBy(Matrix3.Scale(2),new Vector3(7,8,9));assert.deepEqual(t.Position.ToArray(),[9,12,15]);assert.equal(t.TextHeight,4);assert.equal(t.Rotation,0);
 t.TransformBy(Matrix4.Identity);assert.deepEqual(t.Position.ToArray(),[9,12,15]);
});
test('TOLERANCE prefix classification does not alter ordinal cell text',()=>{
 const old=Culture.Current;try{Culture.Current='en-US';const t=Tolerance.ParseStringRepresentation('\u00ad%%v1');assert.equal(t.Entry1.Tolerance1.Value,'1');assert.equal(Tolerance.ParseStringRepresentation('%%V1').DatumIdentifier,'%%V1');}finally{Culture.Current=old;}
});
test('detached TOLERANCE can be admitted and cloned through real block ownership',()=>{
 const b=new Block('B'),t=new Tolerance(entry());b.Entities.Add(t);const q=b.Clone('Copy');assert.equal(q.Entities.get_Item(0).Owner,q);assert.notEqual(q.Entities.get_Item(0).Entry1,t.Entry1);assert.equal(t.Owner,b);
});
test('tolerance corpus is deterministic complete and contains no golden outputs',async()=>{
 const {toleranceCorpus}=await import('../../tools/tolerance-corpus.mjs');const c=toleranceCorpus();assert.deepEqual(c,toleranceCorpus());assert.equal(c.length,634);assert.equal(new Set(c.map(p=>p.name)).size,634);assert.equal(c.reduce((n,p)=>n+p.request.steps.length,0),4998);assert.ok(c.every(p=>!Object.hasOwn(p,'expected')));
});
