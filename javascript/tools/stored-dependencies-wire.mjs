import { BinaryCodeValueReader } from '../netDxf/IO/BinaryCodeValueReader.js';
// Test-only retained construction and observations. Production models do all validation.
import * as api from '../index.js';
import { DecodeTableText } from '../runtime/TablePayload.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
import { InvalidOperationException, NullReferenceException } from '../runtime/Errors.js';
import { tableStyleSnapshot } from './table-style-wire.mjs';
import { doubleBits } from './wire.mjs';
const ref = item => item == null ? null : {type: item.constructor.name, handle: item.Handle, owner: item.Owner?.Handle ?? null};
const text = value => value == null ? null : {utf16: Array.from({length:value.length}, (_, i) => value.charCodeAt(i))};
const real = value => ({double:doubleBits(value)});
export function dependencySnapshot(value) {
  if (value == null) return null;
  const common = item => ({common:ref(item),version:item.SourceVersion,erased:item.IsErased,database:item.Database?.Document.Handle ?? null});
  if (value instanceof api.DxfStoredField) return {kind:'field',...common(value),payload:Array.from(value.Payload,tableStyleSnapshot),
    evaluator:text(value.EvaluatorId),code:text(value.FieldCode),children:Array.from(value.Children,ref),objects:Array.from(value.ReferencedObjects,ref),
    references:Array.from(value.References,ref),declared:Array.from(value.DeclaredOwnedObjects,ref)};
  if (value instanceof api.DxfStoredDimAssocPoint) return {kind:'point',index:value.PointIndex,osnap:value.OsnapType,handle:text(value.GeometryHandle),
    geometry:ref(value.Geometry),subentity:value.SubentityType,marker:value.MarkerIndex,parameter:real(value.NearParameter),point:value.Point.ToArray().map(real)};
  if (value instanceof api.DxfStoredDimAssoc) return {kind:'association',...common(value),tags:Array.from(value.Tags,tableStyleSnapshot),dimension:ref(value.Dimension),
    mask:value.AssociativityMask,trans:value.IsTransSpace,rotated:value.RotatedDimensionType,points:Array.from(value.PointReferences,dependencySnapshot),references:Array.from(value.References,ref)};
  if (value instanceof api.DxfStoredSunStudy) return {kind:'study',...common(value),payload:Array.from(value.Payload,tableStyleSnapshot),version0:value.Version,
    name:text(value.Name),description:text(value.Description),output:value.OutputType,sheet:text(value.SheetSetName),subset:text(value.SheetSubsetName),useSubset:value.UseSubset,
    selectDates:value.SelectDates,dateCount:value.DateCount,selectRange:value.SelectDateRange,start:value.StartTime,end:value.EndTime,interval:value.Interval,
    hours:Array.from(value.RawHourFlags),page:ref(value.PageSetup),view:ref(value.View),visual:ref(value.VisualStyle),style:ref(value.TextStyle),references:Array.from(value.References,ref)};
  if (value instanceof api.DxfObject) return ref(value);
  if (value instanceof api.Vector3) return value.ToArray().map(real);
  if (value instanceof api.DxfTag) return tableStyleSnapshot(value);
  if (typeof value === 'object' && value[Symbol.iterator]) return Array.from(value,dependencySnapshot);
  return value;
}
function handle(value, read) {
  if (value && typeof value === 'object' && 'handleOf' in value) {
    const item = read({ref:value.handleOf});
    if (item == null || item.Handle == null) throw new NullReferenceException();
    return '0'.repeat(value.pad ?? 0) + (value.lower ? item.Handle.toLowerCase() : item.Handle);
  }
  return read(value);
}
export function dependencyNew(step, source, read) {
  if (step.kind === 'point') return new api.DxfStoredDimAssocPoint(...step.args.map(v => handle(v,read)));
  const tags = step.tags.map(([code,value]) => new api.DxfTag(code,handle(value,read)));
  if (step.kind === 'field') return new api.DxfStoredField(source,tags,read(step.evaluator),read(step.code));
  if (step.kind === 'association') return new api.DxfStoredDimAssoc(source,tags,handle(step.dimension,read),step.mask,step.trans,step.rotated,step.points.map(read));
  if (step.kind === 'study') return new api.DxfStoredSunStudy(source,tags,DecodeTableText);
  throw new Error('Unknown dependency fixture kind.');
}
export function dependencyRegister(step, target, read) {
  const document=read(step.document),owner=read(step.owner);
  target.Owner=owner;
  if (Object.hasOwn(step,'handle')) target.Handle=handle(step.handle,read);
  document.Objects.Register(target,step.preserve ?? false);
  if (owner instanceof api.DxfDictionary && step.name != null) owner.AddLoaded(step.name,target,step.hard ?? true);
}
export function dependencyResolve(step, target, read, values) {
  const document=read(step.document),trace=[]; if(step.trace)values.set(step.trace,trace);
  const overrides=(step.overrides ?? []).map(pair => [handle(pair[0],read),read(pair[1])]);
  const missing=(step.missing ?? []).map(v => handle(v,read));
  const resolve=value => {
    trace.push(value);
    for (const hook of step.hooks ?? []) if(hook.at === trace.length) {
      if(hook.kind === 'throw') throw new InvalidOperationException('Injected source resolver failure.');
      const subject=read(hook.target);
      if(hook.kind === 'set')subject[hook.member]=read(hook.value);
      else subject[hook.member](...(hook.args ?? []).map(read));
    }
    if(missing.includes(value))return null;
    const override=overrides.find(pair=>pair[0]===value);
    if(override)return override[1];
    return step.ownerHeld ? document.StoredTableHandleTarget(value) : document.GetObjectByHandle(value);
  };
  if(target instanceof api.DxfStoredField)target.Resolve(step.children.map(v=>handle(v,read)),step.objects.map(v=>handle(v,read)),resolve);
  else target.Resolve(resolve);
}
export function dependencyValidate(step, target, read) {
  const errors=new ReferenceList(); target.ValidateDatabaseSchema(read(step.database),errors); return Array.from(errors);
}

/** Exact low-level binary error observations; supplied bytes are identical in .NET. */
export function dependencyCodec(step) {
  const input=new api.MemoryStream(Uint8Array.from(step.bytes));input.Position=step.origin??0;
  const reader=new BinaryCodeValueReader(input,undefined,step.legacy??false);
  let error=null,message=null;
  try { for(let i=0;i<(step.reads??1);i++)reader.Next(); }
  catch(e) {error=e.name;message=e.message;}
  return {error,message,code:reader.Code,position:reader.CurrentPosition,canRead:input.CanRead};
}
