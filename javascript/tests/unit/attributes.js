import test from 'node:test';
import assert from 'node:assert/strict';
import { Attribute, AttributeDefinition, AttributeCollection, AttributeDefinitionDictionary, EntityCollection,
  AttributeDefinitionDictionaryEventArgs, EntityCollectionEventArgs, AttributeChangeEventArgs,
  DxfObject, EntityObject, TextStyle, Text, Layer, Line, Vector3, Matrix3, Matrix4, XData, XDataRecord, XDataCode, ApplicationRegistry } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException, NullReferenceException } from '../../runtime/Errors.js';
import { doubleBits } from '../../tools/wire.mjs';
import { attributeCorpus } from '../../tools/attribute-corpus.mjs';
import { attributeCollectionCorpus } from '../../tools/attribute-collection-corpus.mjs';
const xyz=p=>[p.X,p.Y,p.Z];

test('ATTDEF and ATTRIB have the original DxfObject inheritance and distinct style event names',()=>{
  const d=new AttributeDefinition('TAG'),a=new Attribute(d);
  assert.ok(d instanceof DxfObject);assert.ok(a instanceof DxfObject);
  assert.equal(d instanceof EntityObject,false);assert.equal(a instanceof EntityObject,false);
  assert.equal(d.CodeName,'ATTDEF');assert.equal(a.CodeName,'ATTRIB');
  assert.equal(d.Type,undefined);assert.equal(a.Reactors,undefined);
  assert.equal(typeof d.TextStyleChange.Add,'function');assert.equal(typeof a.TextStyleChanged.Add,'function');
  assert.equal(d.TextStyleChanged,undefined);assert.equal(a.TextStyleChange,undefined);
});
test('attribute constructor ordering, style-height inheritance and normalized empty strings follow C#',()=>{
  const s=new TextStyle('S','txt.shx');s.Height=2.5;s.WidthFactor=.5;s.ObliqueAngle=-30;
  const d=new AttributeDefinition('TAG',s);assert.equal(d.Height,2.5);assert.equal(d.WidthFactor,.5);assert.equal(d.ObliqueAngle,-30);
  assert.equal(new AttributeDefinition('TAG',4,s).Height,4);
  s.Height=1e-13;assert.equal(new AttributeDefinition('TAG',s).Height,1);
  assert.throws(()=>new AttributeDefinition(null,null),{name:'ArgumentNullException',ParamName:'style'});
  assert.throws(()=>new AttributeDefinition(null,2,null),{name:'ArgumentNullException',ParamName:'tag'});
  assert.throws(()=>new AttributeDefinition('TAG',0,null),{name:'ArgumentNullException',ParamName:'style'});
  assert.throws(()=>new AttributeDefinition('TAG',0,s),{name:'ArgumentOutOfRangeException',ParamName:'textHeight'});
  d.Prompt=null;d.Value=null;assert.equal(d.Prompt,'');assert.equal(d.Value,'');
  const a=new Attribute(d);a.Value='   ';assert.equal(a.Clone().Value,'   ');a.Value=null;assert.equal(a.Value,'');
});
test('attribute width-factor setters retain their wider domain instead of borrowing Text guards',()=>{
  const d=new AttributeDefinition('T'),a=new Attribute(d);
  for(const item of [a,d])for(const value of [.00001,101,1e300,Infinity,NaN]){item.WidthFactor=value;assert.ok(Object.is(item.WidthFactor,value));}
  for(const item of [a,d])for(const value of [-1,-0,0])assert.throws(()=>{item.WidthFactor=value;},ArgumentOutOfRangeException);
  assert.throws(()=>{new Text().WidthFactor=101;},ArgumentOutOfRangeException);
});
test('attribute creation shares mutable style resources but copies vectors and proxy bytes and not XData',()=>{
  const d=new AttributeDefinition('T');d.Layer=new Layer('SOURCE');d.Position=new Vector3(1,2,3);d.ProxyGraphics=Uint8Array.of(1,2);
  const data=new XData(new ApplicationRegistry('QA'));data.XDataRecord.Add(new XDataRecord(XDataCode.String,'definition'));d.XData.Add(data);
  const a=new Attribute(d);assert.equal(a.Definition,d);assert.equal(a.Layer,d.Layer);assert.equal(a.Color,d.Color);assert.equal(a.Style,d.Style);
  assert.equal(a.XData.Count,0);a.Position.X=99;assert.equal(a.Position.X,1);
  d.ProxyGraphics=Uint8Array.of(9);assert.deepEqual([...a.ProxyGraphics],[1,2]);
  d.Layer.Name='SHARED';assert.equal(a.Layer.Name,'SHARED');
});
test('attribute clones independently own definitions, resources, metadata and XData, without handle ownership',()=>{
  const d=new AttributeDefinition('T');d.ColorName='Book';d.ShadowMode=0;d.ProxyGraphics=Uint8Array.of(1,2);
  const a=new Attribute(d);a.Handle='AB';a.Owner={CodeName:'INSERT'};
  const data=new XData(new ApplicationRegistry('A'));data.XDataRecord.Add(new XDataRecord(XDataCode.String,'attribute'));a.XData.Add(data);
  const b=a.Clone();assert.equal(b.Owner,null);assert.equal(b.Handle,null);
  assert.notEqual(b.Definition,d);assert.notEqual(b.Layer,a.Layer);assert.notEqual(b.Definition.Layer,b.Layer);
  b.XData.get_Item('A').XDataRecord.Clear();assert.equal(data.XDataRecord.Count,1);
  b.ClearProxyGraphics();assert.deepEqual([...a.ProxyGraphics],[1,2]);assert.equal(b.ColorName,'Book');assert.equal(b.ShadowMode,0);
});
test('attribute optional proxy bounds and null-versus-empty presence survive cloning',()=>{
  const d=new AttributeDefinition('T'),a=new Attribute(d);
  assert.equal(Attribute.MaximumProxyGraphicsBytes,16777216);assert.equal(AttributeDefinition.MaximumProxyGraphicsBytes,16777216);
  for(const item of [a,d]){
    item.ProxyGraphics=new Uint8Array(0);assert.equal(item.Clone().ProxyGraphics.length,0);
    assert.throws(()=>{item.ProxyGraphics=new Uint8Array(16777217);},ArgumentOutOfRangeException);
    assert.equal(item.ProxyGraphics.length,0);item.ColorName='';assert.equal(item.Clone().ColorName,'');
    item.ShadowMode=0;assert.throws(()=>{item.ShadowMode=4;},ArgumentOutOfRangeException);assert.equal(item.ShadowMode,0);
    item.ClearProxyGraphics();assert.equal(item.Clone().ProxyGraphics,null);
  }
});
test('attribute event substitution, rejection and deferred invalid states preserve source setter ordering',()=>{
  for(const item of [new AttributeDefinition('D'),new Attribute(new AttributeDefinition('A'))]){
    const original=item.Layer,sub=new Layer('SUB'),proposed=new Layer('PROPOSED');
    const rewrite=(_,e)=>{assert.equal(e.OldValue,original);assert.equal(e.NewValue,proposed);e.NewValue=sub;};
    item.LayerChanged.Add(rewrite);item.Layer=proposed;assert.equal(item.Layer,sub);item.LayerChanged.Remove(rewrite);
    const failure=()=>{throw new InvalidOperationException();};item.LayerChanged.Add(failure);
    assert.throws(()=>{item.Layer=original;},InvalidOperationException);assert.equal(item.Layer,sub);item.LayerChanged.Remove(failure);
    item.LayerChanged.Add((_,e)=>{e.NewValue=null;});item.Layer=proposed;assert.equal(item.Layer,null);assert.throws(()=>item.Clone(),NullReferenceException);
  }
});
test('attribute geometry uses owner MIRRTEXT when available and ignores Matrix4 bottom row',()=>{
  const d=new AttributeDefinition('T'),a=new Attribute(d),matrix=new Matrix4(-1,0,0,3,0,1,0,4,0,0,1,5,9,8,7,6);
  a.Position=new Vector3(1,2,3);a.Owner={Owner:{Record:{Owner:{Owner:{DrawingVariables:{MirrText:true}}}}}};
  a.TransformBy(matrix);assert.deepEqual(xyz(a.Position),[2,6,8]);assert.equal(a.IsBackward,true);
  d.Owner={Record:{Owner:{Owner:{DrawingVariables:{MirrText:true}}}}};d.TransformBy(Matrix3.Scale(-1,1,1),Vector3.Zero);assert.equal(d.IsBackward,true);
});
test('attribute geometry leaves declared width, definition and opaque cache unchanged',()=>{
  const d=new AttributeDefinition('T'),a=new Attribute(d);a.Width=9;a.ProxyGraphics=Uint8Array.of(7);
  a.TransformBy(Matrix3.Scale(2),new Vector3(1,2,3));assert.equal(a.Height,2);assert.equal(a.Width,9);assert.equal(a.Definition,d);assert.equal(d.Height,1);assert.deepEqual([...a.ProxyGraphics],[7]);
});
test('attribute scalar storage preserves signed zero and warmed angle NaN bits',()=>{
  const d=new AttributeDefinition('T');d.ObliqueAngle=-0;assert.equal(doubleBits(d.Clone().ObliqueAngle),'8000000000000000');
  for(let i=0;i<3000;i++){d.Rotation=i;d.Rotation=Infinity;}
  assert.equal(doubleBits(d.Rotation),'FFF8000000000000');assert.equal(doubleBits(new Attribute(d).Rotation),'FFF8000000000000');
});
test('internal ATTRIB packet constructor does not invent defaults, and its clone retains original validation failure',()=>{
  const a=Attribute.CreateOverload('string','T');assert.equal(a.Tag,'T');assert.equal(a.Height,0);assert.equal(a.Layer,null);assert.equal(a.Value,'');
  assert.throws(()=>a.Clone(),NullReferenceException);assert.throws(()=>new Attribute(null),ArgumentNullException);
});
test('attribute read-only collection snapshots references and searches only bound definitions',()=>{
  const a=new Attribute(new AttributeDefinition('TAG')),raw=Attribute.CreateOverload('string','TAG'),input=[raw,a],list=new AttributeCollection(input);
  input.length=0;assert.equal(list.Count,2);assert.equal(list.AttributeWithTag('tag'),a);assert.equal(list.AttributeWithTag(''),null);
  a.Value='changed';assert.equal(list.get_Item(1).Value,'changed');assert.equal(AttributeCollection.IsReadOnly,true);assert.equal(list.Add,undefined);
  assert.throws(()=>new AttributeCollection([null,a]).AttributeWithTag('tag'),NullReferenceException);
});
test('attribute/entity CopyTo exposes Array.Copy parameter diagnostics and retains destination on rejection',()=>{
  for(const list of [new AttributeCollection([new Attribute(new AttributeDefinition('T'))]),new EntityCollection()]){
    if(list instanceof EntityCollection)list.Add(new Line());const out=[null];
    assert.throws(()=>list.CopyTo(null,0),{name:'ArgumentNullException',ParamName:'destinationArray'});
    assert.throws(()=>list.CopyTo(out,-1),{name:'ArgumentOutOfRangeException',ParamName:'destinationIndex'});
    assert.throws(()=>list.CopyTo(out,1),{name:'ArgumentException',ParamName:'destinationArray'});assert.deepEqual(out,[null]);
    list.CopyTo(out,0);assert.equal(out[0],list.get_Item(0));
  }
});
test('entity insertion raises the source removal event but retains the prior element and forbids appending',()=>{
  const list=new EntityCollection(),a=new Line(),b=new Line();list.Add(a);const events=[];
  for(const key of ['BeforeRemoveItem','BeforeAddItem','RemoveItem','AddItem'])list[key].Add((_,e)=>events.push([key,e.Item,list.Count]));
  list.Insert(0,b);assert.deepEqual(Array.from(list),[b,a]);assert.deepEqual(events.map(x=>x[0]),['BeforeRemoveItem','BeforeAddItem','RemoveItem','AddItem']);
  assert.equal(events[2][2],1);assert.equal(events[2][1],a);
  assert.throws(()=>list.Insert(list.Count,b),ArgumentOutOfRangeException);assert.throws(()=>new EntityCollection().Insert(0,b),ArgumentOutOfRangeException);
});
test('entity collection replacement raises add before remove and cancellation preserves identity and iterators',()=>{
  const list=new EntityCollection(),a=new Line(),b=new Line();list.Add(a);const events=[];const it=list.GetEnumerator();it.MoveNext();
  const cancel=(_,e)=>{e.Cancel=true;};list.BeforeAddItem.Add(cancel);list.set_Item(0,b);assert.equal(list.get_Item(0),a);assert.equal(it.MoveNext(),false);
  list.BeforeAddItem.Remove(cancel);list.AddItem.Add(()=>events.push('add'));list.RemoveItem.Add(()=>events.push('remove'));
  list.set_Item(0,b);assert.deepEqual(events,['add','remove']);assert.throws(()=>it.MoveNext(),InvalidOperationException);
});
test('entity self-ranges expose partial mutation and iterator invalidation rather than silently snapshotting',()=>{
  const list=new EntityCollection(),a=new Line(),b=new Line();list.Add(a);list.Add(b);
  assert.throws(()=>list.AddRange(list),InvalidOperationException);assert.deepEqual(Array.from(list),[a,b,a]);
  assert.throws(()=>list.Remove(list),InvalidOperationException);assert.deepEqual(Array.from(list),[b,a]);
});
test('prepared SECTION collection hooks bypass notifications while ordinary null entries retain source behavior',()=>{
  const list=new EntityCollection(),a=new Line();let events=0;list.AddItem.Add(()=>events++);list.RemoveItem.Add(()=>events++);
  list.AddPreparedSection(a);list.RemovePreparedSection(a);assert.equal(events,0);list.Add(null);assert.equal(list.get_Item(0),null);assert.equal(events,1);
  assert.throws(()=>list.set_Item(0,null),ArgumentNullException);
});
test('attribute dictionary keys are ordinal-ignore-case, retain original spelling and reuse removed slots',()=>{
  const list=new AttributeDefinitionDictionary(),a=new AttributeDefinition('A'),b=new AttributeDefinition('b'),c=new AttributeDefinition('C');
  list.Add(a);list.Add(b);assert.equal(list.get_Item('a'),a);assert.throws(()=>list.Add(new AttributeDefinition('a')),ArgumentException);
  list.Remove('a');list.Add(c);assert.deepEqual(Array.from(list.Tags),['C','b']);
  assert.throws(()=>list.set_Item('missing',new AttributeDefinition('missing')),{name:'KeyNotFoundException'});
  assert.throws(()=>list.set_Item('WRONG',a),ArgumentException);
});
test('attribute dictionary views and enumerators observe replacement/removal while new insertion invalidates them',()=>{
  const list=new AttributeDefinitionDictionary(),a=new AttributeDefinition('A'),b=new AttributeDefinition('b');list.Add(a);list.Add(b);
  const keys=list.Tags,values=list.Values,it=list.GetEnumerator();it.MoveNext();
  const replacement=new AttributeDefinition('B');list.set_Item('b',replacement);assert.equal(it.MoveNext(),true);assert.equal(it.Current.Value,replacement);
  list.Remove('A');assert.equal(it.MoveNext(),false);assert.deepEqual(Array.from(keys),['b']);assert.deepEqual(Array.from(values),[replacement]);
  list.Add(a);assert.throws(()=>it.MoveNext(),InvalidOperationException);assert.throws(()=>keys.Clear(),NotSupportedException);
});
test('attribute dictionary same-reference replacement is silent and after-add failure retains committed mutation',()=>{
  const list=new AttributeDefinitionDictionary(),a=new AttributeDefinition('A');list.Add(a);let before=0;list.BeforeAddItem.Add(()=>before++);
  list.set_Item('a',a);assert.equal(before,0);
  const b=new AttributeDefinition('B');list.AddItem.Add(()=>{throw new InvalidOperationException();});assert.throws(()=>list.Add(b),InvalidOperationException);assert.equal(list.get_Item('b'),b);
});
test('attribute dictionary explicit interface adapters ignore Add keys but require identity for pair removal',()=>{
  const list=new AttributeDefinitionDictionary(),a=new AttributeDefinition('A');list.Add('ignored',a);assert.equal(list.ContainsTag('ignored'),false);assert.equal(list.get_Item('a'),a);
  assert.equal(list.RemovePair({Key:'a',Value:new AttributeDefinition('A')}),false);assert.equal(list.ContainsPair({Key:'a',Value:a}),true);
  assert.throws(()=>list.RemovePair({Key:'missing',Value:a}),{name:'KeyNotFoundException'});assert.equal(list.RemovePair({Key:'a',Value:a}),true);
});
test('collection event argument Item references are read-only while Cancel stays mutable',()=>{
  const item=new Line();for(const Type of [AttributeChangeEventArgs,EntityCollectionEventArgs,AttributeDefinitionDictionaryEventArgs]){
    const e=new Type(item);assert.equal(e.Item,item);assert.throws(()=>{e.Item=null;},TypeError);
    if(Type!==AttributeChangeEventArgs){assert.equal(e.Cancel,false);e.Cancel=true;assert.equal(e.Cancel,true);}
  }
});
test('attribute comparison corpus has stable unique identities and no stored expected answers',()=>{
  const probes=[...attributeCorpus(),...attributeCollectionCorpus()];assert.equal(probes.length,1107);assert.equal(new Set(probes.map(p=>p.name)).size,1107);
  assert.equal(probes.reduce((n,p)=>n+p.request.steps.length,0),12272);assert.ok(probes.every(p=>!Object.hasOwn(p,'expected')));
});
