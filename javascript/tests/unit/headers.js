import test from 'node:test';
import assert from 'node:assert/strict';
import {userInfo} from 'node:os';
import {HeaderVariables,HeaderVariable,HeaderDateTime,HeaderTimeSpan,HeaderEnum,SetHeaderEnvironment,
  AciColor,UCS,Vector3,LinearUnitType,DrawingUnits,DxfVersion,HeaderVariableCode} from '../../index.js';
import {GetHeaderEnvironment} from '../../runtime/HeaderTime.js';
import {BoxedScalar} from '../../runtime/BoxedScalar.js';
import {Copy,Format} from '../../runtime/GeometryRuntime.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidCastException,
  NullReferenceException,NotSupportedException} from '../../runtime/Errors.js';
const fixed=Object.freeze({UserName:()=> 'Test user',Now:()=>new HeaderDateTime(638000000000000123n,2),UtcNow:()=>new HeaderDateTime(638000000000000124n,1)});
const make=()=>new HeaderVariables(fixed);
const entry=(h,name)=>[...h.KnownValues()].find(v=>v.Name===HeaderVariableCode[name]);

test('header defaults expose forty ordered named entries and the documented codes',()=>{
  const h=make(),names=[...h.KnownNames()];assert.equal(names.length,40);assert.equal(new Set(names).size,40);
  assert.deepEqual(names.slice(0,5),['$ACADVER','$DWGCODEPAGE','$LASTSAVEDBY','$HANDSEED','$ANGBASE']);
  assert.deepEqual(names.slice(-5),['$TDCREATE','$TDUCREATE','$TDUPDATE','$TDUUPDATE','$TDINDWG']);
  assert.equal(h.AcadVer,DxfVersion.AutoCad2000);assert.equal(h.DwgCodePage,'ANSI_1252');assert.equal(h.CMLScale,20);
  assert.equal(h.SplineSegs,8);assert.equal(h.SurfU,6);assert.equal(h.SurfV,6);
  assert.equal(entry(h,'CeColor').GroupCode,62);assert.equal(entry(h,'CeLweight').GroupCode,370);assert.equal(entry(h,'InsBase').GroupCode,10);
});
test('header instances have independent colors, UCS objects and custom dictionaries',()=>{
  const a=make(),b=make();assert.notEqual(a.CeColor,b.CeColor);assert.notEqual(a.CurrentUCS,b.CurrentUCS);
  assert.equal(a.CurrentUCS.Name,'Unnamed');a.CeColor.Index=2;assert.equal(b.CeColor.Index,256);
  a.AddCustomVariable(new HeaderVariable('$A',40,1));assert.equal(b.CustomValues().Count,0);
});
test('known values are independent lists of live original HeaderVariable references',()=>{
  const h=make(),a=h.KnownValues(),b=h.KnownValues();assert.notEqual(a,b);assert.equal(a.get_Item(0),b.get_Item(0));
  entry(h,'Angbase').Value=12;assert.equal(h.Angbase,12);h.Angbase=13;assert.equal(entry(h,'Angbase').Value,13);
  a.RemoveAt(0);b.Clear();assert.equal(h.KnownValues().Count,40);assert.equal(h.AcadVer,13);
});
test('header known names snapshots are mutable without altering the internal dictionary',()=>{
  const h=make(),names=h.KnownNames();names.set_Item(0,'$OTHER');names.Clear();assert.equal(h.KnownNames().get_Item(0),'$ACADVER');
});
test('integer and enum boxes preserve type identity at the object-valued escape hatch',()=>{
  const h=make(),precision=entry(h,'AUprec'),version=entry(h,'AcadVer');
  assert.ok(precision.Value instanceof BoxedScalar);assert.equal(precision.Value.Type,'Int16');
  assert.ok(version.Value instanceof HeaderEnum);assert.equal(version.Value.Type,'DxfVersion');
  precision.Value=4;assert.throws(()=>h.AUprec,InvalidCastException);
  precision.Value=new BoxedScalar('Int16',4);assert.equal(h.AUprec,4);
  version.Value=new BoxedScalar('Int32',18);assert.equal(h.AcadVer,18);
});
test('raw known-entry edits bypass model setters and retain source invalid-cast failures',()=>{
  const h=make();entry(h,'AcadVer').Value=new BoxedScalar('Int32',-1);assert.equal(h.AcadVer,-1);
  assert.throws(()=>{h.AcadVer=-1;},NotSupportedException);
  entry(h,'TextSize').Value=-2;assert.equal(h.TextSize,-2);assert.throws(()=>{h.TextSize=-2;},ArgumentOutOfRangeException);
  entry(h,'TextSize').Value=null;assert.throws(()=>h.TextSize,NullReferenceException);
  entry(h,'CeColor').Value=null;assert.equal(h.CeColor,null);assert.throws(()=>{h.CeColor=null;},ArgumentNullException);
  entry(h,'CeLtype').Value=123;assert.throws(()=>h.CeLtype,InvalidCastException);
});
test('header guards reject atomically without imposing stronger nonfinite constraints than C#',()=>{
  const h=make();for(const name of ['CeLtScale','TextSize','LtScale']){
    const old=h[name];for(const bad of [-Infinity,-1,-0,0])assert.throws(()=>{h[name]=bad;},ArgumentOutOfRangeException);
    assert.equal(h[name],old);h[name]=Infinity;assert.equal(h[name],Infinity);h[name]=NaN;assert.ok(Number.isNaN(h[name]));
  }
});
test('header precision segmentation and generation ranges preserve boundary behavior',()=>{
  const h=make();for(const [name,min,max] of [['AUprec',0,8],['LUprec',0,8],['PLineGen',0,1],['PsLtScale',0,1],['SplineSegs',1,32767],['SurfU',0,200],['SurfV',0,200]]){
    h[name]=min;assert.equal(h[name],min);h[name]=max;assert.equal(h[name],max);
    assert.throws(()=>{h[name]=min-1;},ArgumentOutOfRangeException);assert.equal(h[name],max);
  }
});
test('architectural and engineering units set inches without restoring earlier insertion units',()=>{
  const h=make();h.InsUnits=DrawingUnits.Meters;h.LUnits=LinearUnitType.Architectural;assert.equal(h.InsUnits,DrawingUnits.Inches);
  h.LUnits=LinearUnitType.Decimal;assert.equal(h.InsUnits,DrawingUnits.Inches);
  h.InsUnits=DrawingUnits.Feet;h.LUnits=LinearUnitType.Engineering;assert.equal(h.InsUnits,DrawingUnits.Inches);
  entry(h,'LUnits').Value=new HeaderEnum('LinearUnitType',LinearUnitType,2);assert.equal(h.InsUnits,DrawingUnits.Inches);
});
test('header name fields reject only null and empty strings rather than sanitizing text',()=>{
  const h=make();for(const name of ['CeLtype','CLayer','CMLStyle','DimStyle','TextStyle']){
    for(const bad of [null,''])assert.throws(()=>{h[name]=bad;},ArgumentNullException);
    h[name]=' \0\n\ud800 ';assert.equal(h[name],' \0\n\ud800 ');
  }
  h.LastSavedBy=null;assert.equal(h.LastSavedBy,null);h.HandleSeed='not-hex';assert.equal(h.HandleSeed,'not-hex');
});
test('insertion coordinates copy on assignment and read while colors and UCS retain reference identity',()=>{
  const h=make(),v=new Vector3(-0,2,3);h.InsBase=v;v.Y=9;h.InsBase.Z=8;assert.deepEqual([h.InsBase.X,h.InsBase.Y,h.InsBase.Z],[-0,2,3]);
  const c=AciColor.Red,u=new UCS('Shared');h.CeColor=c;h.CurrentUCS=u;c.Index=2;u.Origin=Vector3.UnitX;
  assert.equal(h.CeColor,c);assert.equal(h.CeColor.Index,2);assert.equal(h.CurrentUCS,u);assert.equal(h.CurrentUCS.Origin.X,1);
  assert.throws(()=>{h.CurrentUCS=null;},ArgumentNullException);assert.equal(h.CurrentUCS,u);
});
test('custom headers use ordinal case-insensitive lookup without expanding sharp-s',()=>{
  const h=make(),v=new HeaderVariable('$ß',40,2);h.AddCustomVariable(v);h.AddCustomVariable(new HeaderVariable('$SS',40,3));
  assert.equal(h.CustomValues().Count,2);h.AddCustomVariable(new HeaderVariable('$ŻÓŁĆ',40,4));assert.equal(h.ContainsCustomVariable('$żółć'),true);
  assert.throws(()=>h.AddCustomVariable(new HeaderVariable('$ss',40,5)),ArgumentException);
});
test('custom header names reject known collisions but admit non-listed UCS variable names',()=>{
  const h=make();assert.throws(()=>h.AddCustomVariable(null),{name:'ArgumentNullException',ParamName:'variable'});
  assert.throws(()=>h.AddCustomVariable(new HeaderVariable('$aCaDvEr',70,1)),{name:'ArgumentException',ParamName:'variable'});
  h.AddCustomVariable(new HeaderVariable('$UCSORG',10,Vector3.Zero));assert.equal(h.CustomValues().Count,1);
});
test('custom header dictionaries reuse removed slots in last-in-first-out order',()=>{
  const h=make();for(const n of ['A','B','C','D'])h.AddCustomVariable(new HeaderVariable('$'+n,40,1));
  const old=h.CustomNames();h.RemoveCustomVariable('$b');h.RemoveCustomVariable('$d');
  for(const n of ['E','F'])h.AddCustomVariable(new HeaderVariable('$'+n,40,1));
  assert.deepEqual([...h.CustomNames()],['$A','$F','$C','$E']);assert.deepEqual([...old],['$A','$B','$C','$D']);
});
test('custom values snapshots retain shared variables even after dictionary removal and clearing',()=>{
  const h=make(),v=new HeaderVariable('$APP',40,1);h.AddCustomVariable(v);const old=h.CustomValues();
  v.Value=8;assert.equal(old.get_Item(0).Value,8);h.RemoveCustomVariable('$app');v.Value=9;assert.equal(old.get_Item(0).Value,9);
  h.ClearCustomVariables();assert.equal(old.Count,1);assert.equal(h.CustomValues().Count,0);
});
test('TryGetCustomVariable clears the explicit out adapter for missing keys and preserves errors',()=>{
  const h=make(),v=new HeaderVariable('$V',40,1),out={value:'old'};h.AddCustomVariable(v);
  assert.ok(h.TryGetCustomVariable('$v',out));assert.equal(out.value,v);assert.equal(h.TryGetCustomVariable('$missing',out),false);assert.equal(out.value,null);
  assert.throws(()=>h.TryGetCustomVariable(null,out),{name:'ArgumentNullException',ParamName:'key'});
});
