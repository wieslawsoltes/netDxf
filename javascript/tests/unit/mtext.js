import test from 'node:test';
import assert from 'node:assert/strict';
import { MText, MTextColumns, MTextColumnType, MTextColumnStorage, MTextFormattingOptions, MTextParagraphOptions, MTextBackgroundFill,
  Vector3, Matrix3, TextStyle, Culture, AciColor } from '../../index.js';
import { ArgumentException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
import { utf16Wire } from '../../tools/mtext-wire.mjs';
const text=()=>{const t=new MText('ABCD');const c=new MTextColumns();c.Count=2;c.Width=10;c.Gutter=2;t.Columns=c;return t;};
const snapshot=t=>({position:[t.Position.X,t.Position.Y,t.Position.Z],height:t.Height,width:t.RectangleWidth,columns:[t.Columns.Width,t.Columns.Gutter,t.Columns.DefinedHeight,t.Columns.TotalHeight],value:t.Value});
test('all MText vector constructor overloads copy values and retain constructor-only width behavior',()=>{
  const p=new Vector3(1,2,3),t=new MText('T',p,2,-1);p.X=99;assert.equal(t.Position.X,1);assert.equal(t.RectangleWidth,-1);
  assert.throws(()=>t.Clone(),ArgumentOutOfRangeException);assert.throws(()=>{t.RectangleWidth=-1;},ArgumentOutOfRangeException);
  assert.throws(()=>new MText('T',p,0),{name:'ArgumentOutOfRangeException',ParamName:'height',ActualValue:'T'});
});
test('MText formatting emits commands in original order including RGB byte order',()=>{
  const t=new MText(),o=new MTextFormattingOptions();o.Color=AciColor.FromTrueColor(0x123456);o.Bold=true;o.Italic=true;o.Underline=true;o.HeightFactor=2;
  t.Write('value',o);assert.equal(t.Value,`{\\H2x;\\C${o.Color.Index};\\c5649426;\\L\\Fsimplex.shx|b1|i1;value\\l}`);assert.equal(t.PlainText(),'value');
  const p=new MTextParagraphOptions();p.FirstLineIndent=-3;p.LeftIndent=2;t.Value=null;t.StartParagraph(p);assert.match(t.Value,/\\pi-2,l2,r0,b0,a0;/);
});
test('PlainText retains exact malformed-command and stack termination behavior',()=>{
  const cases=[['before\\Sbad','before'],['before\\Sbad\\','before'],['before\\Sbad^','before'],['\\S1\\;2;tail','1;2tail'],['\\SA^B;','A^B'],['\\SA^ B;','AB'],['a\\~b','a'],['x{a}{b}y','xaby']];
  for(const [source,wanted] of cases)assert.equal(new MText(source).PlainText(),wanted);
  assert.equal(new MText('\ud800\\P\udc00').PlainText(),'\ud800'+Culture.NewLine+'\udc00');
});
test('UTF-16 oracle transport preserves unpaired code units without rewriting valid surrogate pairs',()=>{
  assert.equal(utf16Wire('日本😀'),'日本😀');assert.deepEqual(utf16Wire('\ud800A'),{utf16:[0xd800,65]});assert.deepEqual(utf16Wire('A\udc00'),{utf16:[65,0xdc00]});
  const t=new MText('\ud800A');assert.equal(t.Clone().Value,t.Value);
});
test('column metadata invalidation happens only after successful dimension edits',()=>{
  for(const member of ['Count','Width','Gutter']){
    const c=new MTextColumns();c.StoredTotalWidth=123;assert.throws(()=>{c[member]=-1;},ArgumentOutOfRangeException);assert.equal(c.StoredTotalWidth,123);
    c[member]=c[member];assert.equal(c.StoredTotalWidth,null);
  }
});
test('embedded placement values are copied both directions and validated lazily',()=>{
  const c=new MTextColumns(),v=new Vector3(1,2,3);c.EmbeddedInsertionPoint=v;v.X=99;c.EmbeddedInsertionPoint.Y=99;
  assert.equal(c.EmbeddedInsertionPoint.X,1);assert.equal(c.EmbeddedInsertionPoint.Y,2);
  c.EmbeddedTextDirection=Vector3.Zero;assert.throws(()=>c.Validate(),{name:'ArgumentOutOfRangeException',ParamName:'EmbeddedTextDirection'});
  c.ResetEmbeddedPlacement();c.Validate();assert.equal(c.EmbeddedInsertionPoint,null);
});
test('automatic column width validation and clone checks preserve authored optional metadata',()=>{
  const c=new MTextColumns();c.Type=MTextColumnType.Dynamic;c.AutoHeight=true;c.Count=2;c.Width=10;c.Gutter=2;c.StoredTotalWidth=22;c.Validate();
  assert.equal(c.Clone().StoredTotalWidth,22);c.StoredTotalWidth=23;assert.throws(()=>c.Validate(),InvalidOperationException);
  c.StoredTotalWidth=null;c.PendingHandles.Add('AB');assert.throws(()=>c.Clone(),InvalidOperationException);
});
test('manual columns permit a zero final height but reject other zeros, surplus heights and defined height',()=>{
  const c=new MTextColumns();c.Type=MTextColumnType.Dynamic;c.Count=2;c.Heights.AddRange([10,0]);c.Validate();
  c.Heights.set_Item(0,0);assert.throws(()=>c.Validate(),{name:'ArgumentOutOfRangeException',ParamName:'Heights'});c.Heights.set_Item(0,10);
  c.DefinedHeight=1;assert.throws(()=>c.Validate(),InvalidOperationException);c.DefinedHeight=0;c.Heights.Add(1);assert.throws(()=>c.Validate(),InvalidOperationException);
});
test('embedded-to-linked conversion creates detached independent columns and read-only result',()=>{
  const t=text(),all=t.ConvertToLinkedColumns(['AB','CD']),a=all.get_Item(0),b=all.get_Item(1);
  assert.equal(all.Add,undefined);assert.equal(a.Owner,null);assert.equal(b.Owner,null);assert.equal(b.Columns,null);assert.equal(b.Position.X,12);
  assert.equal(a.Columns.LinkedColumns.get_Item(0),b);assert.equal(a.ConvertToEmbeddedColumns().Value,t.Value);
  const clone=a.Clone();clone.Columns.LinkedColumns.get_Item(0).Value='changed';assert.equal(b.Value,'CD');assert.equal(t.Value,'ABCD');
});
test('column partitions preserve exact source text and formatting conflicts fail explicitly',()=>{
  const t=text();for(const parts of [[],['ABCD'],['AB',null],['AB','XX']])assert.throws(()=>t.ConvertToLinkedColumns(parts),ArgumentException);
  const all=t.ConvertToLinkedColumns(['AB','CD']);all.get_Item(1).Style=new TextStyle('OTHER','font.shx');assert.throws(()=>all.get_Item(0).ConvertToEmbeddedColumns(),NotSupportedException);
});
test('linked, cyclic and duplicate graphs reject cloning without recursion or partial mutation',()=>{
  for(const variant of ['null','duplicate','nested']){
    const c=new MTextColumns();c.Storage=MTextColumnStorage.LegacyLinked;c.Count=variant==='duplicate'?3:2;const linked=new MText('X');
    if(variant==='nested')linked.Columns=c;c.LinkedColumns.Add(variant==='null'?null:linked);if(variant==='duplicate')c.LinkedColumns.Add(linked);
    assert.throws(()=>c.Clone(),InvalidOperationException);
  }
});
test('column transform guards are atomic including overflow and forbid linked-entity movement',()=>{
  const t=text(),before=snapshot(t);
  for(const m of [Matrix3.Scale(2,1,1),Matrix3.Scale(-1,1,1),Matrix3.Scale(0),Matrix3.Scale(1e308)]){
    assert.throws(()=>t.TransformBy(m,new Vector3(9,8,7)));assert.deepEqual(snapshot(t),before);
  }
  const a=t.ConvertToLinkedColumns(['AB','CD']).get_Item(0);assert.throws(()=>a.TransformBy(Matrix3.Identity,Vector3.UnitX),NotSupportedException);
  t.Columns.EmbeddedInsertionPoint=Vector3.UnitX;t.TransformBy(Matrix3.Scale(2),new Vector3(1,2,3));assert.equal(t.Columns.Width,20);assert.equal(t.Columns.EmbeddedInsertionPoint,null);
});
test('relinking cloned column graphs uses the supplied object identity map',()=>{
  const source=text().ConvertToLinkedColumns(['AB','CD']),a=source.get_Item(0),b=source.get_Item(1),ca=a.Clone(),cb=b.Clone();
  MText.RelinkClonedColumns(new Map([[a,ca],[b,cb]]));assert.equal(ca.Columns.LinkedColumns.get_Item(0),cb);
  assert.throws(()=>MText.RelinkClonedColumns(new Map([[a,a.Clone()]])),InvalidOperationException);
});
test('MText clone preserves background and common metadata without sharing mutable objects',()=>{
  const t=text();t.BackgroundFill=MTextBackgroundFill.FromDrawingWindow(2);t.BackgroundFill.Transparency=-2147483648;t.ProxyGraphics=Uint8Array.of(7,8);
  const q=t.Clone();q.BackgroundFill.Transparency=0;q.Columns.Width=4;assert.equal(t.BackgroundFill.Transparency,-2147483648);assert.equal(t.Columns.Width,10);
  assert.deepEqual(q.ProxyGraphics,Uint8Array.of(7,8));
});
