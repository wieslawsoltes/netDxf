import test from 'node:test';
import assert from 'node:assert/strict';
import { Underlay, UnderlayDefinition, UnderlayPdfDefinition, UnderlayDgnDefinition, UnderlayDwfDefinition,
  UnderlayType, Vector2, Vector3, Matrix3, ClippingBoundary, XData, XDataCode, XDataRecord, ApplicationRegistry } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
import { SetSupportFileSystem } from '../../runtime/SupportFileSystem.js';
const definitions = [UnderlayPdfDefinition, UnderlayDgnDefinition, UnderlayDwfDefinition];
const files = ['file.pdf','file.dgn','file.dwf'];

test('underlay definitions validate extensions without opening referenced documents', () => {
  const prior = SetSupportFileSystem({ReadAllBytes(){throw Error('unexpected IO');},Exists(){throw Error('unexpected IO');},DirectorySeparators:'/',InvalidPathChars:'\0'});
  try {
    for (let i=0;i<definitions.length;i++) {
      const d=new definitions[i]('folder/'+files[i]);assert.equal(d.Name,'file');assert.equal(d.File,'folder/'+files[i]);
      assert.equal(d.HasReferences(),false);assert.equal(d.GetReferences(),null);
      assert.throws(()=>{d.File='wrong.txt';},{name:'ArgumentException',ParamName:'value'});assert.equal(d.File,'folder/'+files[i]);
      assert.throws(()=>{d.File=null;},ArgumentNullException);
    }
    assert.throws(()=>new UnderlayDefinition('D','a.pdf',UnderlayType.PDF),NotSupportedException);
  } finally { SetSupportFileSystem(prior); }
});
test('definition metadata, unchecked construction names and clone names preserve source distinctions', () => {
  const pdf=new UnderlayPdfDefinition(' a/b ','a.pdf');assert.equal(pdf.Name,'a/b');assert.equal(pdf.Page,'1');pdf.Page=null;assert.equal(pdf.Page,'');
  const dgn=new UnderlayDgnDefinition('D','a.dgn');assert.equal(dgn.Layout,'Model');dgn.Layout=null;assert.equal(dgn.Clone().Layout,null);
  const dwf=new UnderlayDwfDefinition('D','a.dwf');assert.equal(dwf.Clone('NEW').Name,'NEW');assert.equal(dwf.Name,'D');
  assert.throws(()=>{pdf.Name='x/y';},ArgumentException);assert.equal(pdf.Name,'a/b');
});
test('definition file validation follows the explicitly chosen host path profile', () => {
  const prior=SetSupportFileSystem({ReadAllBytes(){},Exists(){return false;},DirectorySeparators:'/\\',InvalidPathChars:'\0|'});
  try {
    assert.equal(new UnderlayPdfDefinition('folder\\a.pdf').Name,'a');
    assert.throws(()=>new UnderlayPdfDefinition('D','|a.pdf'),ArgumentException);
    assert.equal(new UnderlayPdfDefinition('D','a|b.pdf').File,'a|b.pdf');
  } finally { SetSupportFileSystem(prior); }
});
test('underlay constructor scale and vector setter have intentionally different validation', () => {
  const d=new UnderlayPdfDefinition('a.pdf');assert.throws(()=>new Underlay(d,Vector3.Zero,0),ArgumentOutOfRangeException);
  const p=new Underlay(d,Vector3.Zero,Number.MIN_VALUE);assert.equal(p.Scale.X,Number.MIN_VALUE);
  assert.throws(()=>{p.Scale=new Vector2(Number.MIN_VALUE,1);},ArgumentOutOfRangeException);
  p.Scale=new Vector2(-2,3);assert.equal(p.Scale.X,-2);p.Scale.X=99;assert.equal(p.Scale.X,-2);
  assert.throws(()=>new Underlay(null),ArgumentNullException);
});
test('underlay event substitutions retain proposed type code and exact failure order', () => {
  const initial=new UnderlayPdfDefinition('a.pdf'),proposed=new UnderlayDgnDefinition('a.dgn'),replacement=new UnderlayDwfDefinition('a.dwf');
  const p=new Underlay(initial);p.UnderlayDefinitionChanged.Add((sender,e)=>{assert.equal(sender,p);assert.equal(p.Definition,initial);e.NewValue=replacement;});
  p.Definition=proposed;assert.equal(p.Definition,replacement);assert.equal(p.CodeName,'DGNUNDERLAY');
  assert.equal(p.Clone().CodeName,'DWFUNDERLAY');
  const q=new Underlay(initial);q.UnderlayDefinitionChanged.Add(()=>{throw new InvalidOperationException();});
  assert.throws(()=>{q.Definition=proposed;},InvalidOperationException);assert.equal(q.Definition,initial);assert.equal(q.CodeName,'PDFUNDERLAY');
});
test('underlay contrast and fade rejections are atomic', () => {
  const p=new Underlay(new UnderlayPdfDefinition('a.pdf'));assert.equal(p.Contrast,100);assert.equal(p.Fade,0);
  for(const [member,invalid] of [['Contrast',[19,101,-1]],['Fade',[-1,81,32767]]]) {
    const before=p[member];for(const value of invalid)assert.throws(()=>{p[member]=value;},ArgumentOutOfRangeException);assert.equal(p[member],before);
  }
});
test('underlay clones own definitions, boundaries and both XData graphs', () => {
  const d=new UnderlayPdfDefinition('a.pdf'),p=new Underlay(d,new Vector3(1,2,3));
  for(const target of [d,p]) {const x=new XData(new ApplicationRegistry('APP'));x.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(1,2)));target.XData.Add(x);}
  p.ClippingBoundary=new ClippingBoundary(0,0,3,4);p.ProxyGraphics=Uint8Array.of(9);p.AddReactor(p);p.Handle='ABC';
  const q=p.Clone();assert.notEqual(q.Definition,d);assert.notEqual(q.ClippingBoundary,p.ClippingBoundary);assert.equal(q.Handle,null);assert.equal(q.Reactors.Count,0);
  q.Definition.XData.get_Item('APP').XDataRecord.Clear();q.XData.get_Item('APP').XDataRecord.Clear();assert.equal(d.XData.get_Item('APP').XDataRecord.Count,1);assert.equal(p.XData.get_Item('APP').XDataRecord.Count,1);
  q.Definition.Page='2';assert.equal(d.Page,'1');assert.deepEqual([...q.ProxyGraphics],[9]);
});
test('underlay transforms preserve clipping and reference metadata while moving placement', () => {
  const p=new Underlay(new UnderlayPdfDefinition('a.pdf'),new Vector3(1,2,3),2),boundary=new ClippingBoundary(0,0,3,4);
  p.ClippingBoundary=boundary;p.TransformBy(Matrix3.Identity,new Vector3(3,4,5));assert.deepEqual([p.Position.X,p.Position.Y,p.Position.Z],[4,6,8]);
  assert.equal(p.ClippingBoundary,boundary);assert.equal(p.Scale.X,2);assert.equal(p.Definition.File,'a.pdf');
});
