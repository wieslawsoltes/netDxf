// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,MLeaderChildCollection,MLeaderComponentTypes} from './MLeaderData.js';
import {Vector3} from '../Vector3.js';
import {Copy} from '../../runtime/GeometryRuntime.js';
import {ReferenceList} from '../../runtime/ReferenceList.js';
import {ValueList} from '../../runtime/ValueList.js';
import {ArgumentException,InvalidOperationException,NotSupportedException,NullReferenceException} from '../../runtime/Errors.js';
// Empty Collection<T> uses the shared empty-array iterator; nonempty lists expose value defaults.
const emptyIterators=new Map();
class MLeaderValues extends ValueList{
  #zero;constructor(zero){super();this.#zero=zero;}
  IndexOf(item){return this.ToArray().findIndex(value=>value?.Equals?value.Equals(item):value===item||Number.isNaN(value)&&Number.isNaN(item));}
  GetEnumerator(){if(this.Count===0){const key=typeof this.#zero==='number'?'Double':'Vector3';if(!emptyIterators.has(key))emptyIterators.set(key,Object.freeze({get Current(){throw new InvalidOperationException('Enumeration has not started or has finished.');},MoveNext(){return false;},Reset(){},Dispose(){},next(){return{done:true,value:undefined};},[Symbol.iterator](){return this;}}));return emptyIterators.get(key);}const iterator=super.GetEnumerator(),owner=this;let active=false;return{
    get Current(){return active?iterator.Current:Copy(owner.#zero);},
    MoveNext(){active=iterator.MoveNext();return active;},Reset(){iterator.Reset();active=false;},Dispose(){iterator.Dispose();},
    next(){return this.MoveNext()?{done:false,value:this.Current}:{done:true,value:undefined};},[Symbol.iterator](){return this;}
  };}
}
export class MLeaderBreak{
  #start;#end;
  constructor(start,end){MLeaderData.Finite(start);MLeaderData.Finite(end);this.#start=Copy(start);this.#end=Copy(end);}
  get Start(){return Copy(this.#start);}get End(){return Copy(this.#end);}
}
export class MLeaderLineBreaks{
  Index=0;#breaks=new ReferenceList();get Breaks(){return this.#breaks;}
  Copy(){const copy=new MLeaderLineBreaks();copy.Index=this.Index;copy.Breaks.AddRange(this.#breaks);return copy;}
}
const state=new WeakMap();
export function InitializeMLeaderChildren(value,name){
  const s={};state.set(value,s);
  if(name==='MLeaderContext'){s.MText=null;s.Block=null;s.Leaders=new MLeaderChildCollection(value,'MLeaderNode');}
  if(name==='MLeaderMTextContent')s.ColumnHeights=new MLeaderValues(0);
  if(name==='MLeaderBlockContent')s.TransformationMatrix=new MLeaderValues(0);
  if(name==='MLeaderNode'){s.Breaks=new ReferenceList();s.Lines=new MLeaderChildCollection(value,'MLeaderLine');}
  if(name==='MLeaderLine'){s.Vertices=new MLeaderValues(Vector3.Zero);s.Breaks=new ReferenceList();}
  if(name==='MLeaderProperties'){s.ArrowHeads=new MLeaderChildCollection(value,'MLeaderArrowHead');s.BlockAttributes=new MLeaderChildCollection(value,'MLeaderBlockAttribute');}
}
export function InstallMLeaderChildren(Type,name){
  const collections={MLeaderContext:['Leaders'],MLeaderMTextContent:['ColumnHeights'],MLeaderBlockContent:['TransformationMatrix'],MLeaderNode:['Breaks','Lines'],MLeaderLine:['Vertices','Breaks'],MLeaderProperties:['ArrowHeads','BlockAttributes']}[name]??[];
  for(const key of collections)Object.defineProperty(Type.prototype,key,{get(){return state.get(this)[key];}});
  if(name==='MLeaderContext')for(const key of ['MText','Block'])Object.defineProperty(Type.prototype,key,{get(){return state.get(this)[key];},set(value){const s=state.get(this),old=s[key];if(old===value)return;if(value!==null){const T=MLeaderComponentTypes.get(key==='MText'?'MLeaderMTextContent':'MLeaderBlockContent');if(!T||!(value instanceof T)||value.Parent!==null||s[key==='MText'?'Block':'MText']!==null)throw new ArgumentException('Content must be unowned and mutually exclusive.','value');value.CheckDocument(this.Document);}if(old!==null)old.Parent=null;s[key]=value;if(value!==null)value.Parent=this;}});
  Object.defineProperty(Type.prototype,'Children',{get(){const s=state.get(this);if(name==='MLeaderContext')return{*[Symbol.iterator](){if(s.MText!==null)yield s.MText;if(s.Block!==null)yield s.Block;yield*s.Leaders;}};if(name==='MLeaderNode')return s.Lines;if(name==='MLeaderProperties')return{*[Symbol.iterator](){yield*s.ArrowHeads;yield*s.BlockAttributes;}};return [];}});
  Type.prototype.CopyChildrenTo=function(copy){const s=state.get(this);if(name==='MLeaderContext'){if(s.MText!==null)copy.MText=s.MText.Clone();if(s.Block!==null)copy.Block=s.Block.Clone();}
    for(const key of collections)for(const item of s[key]){if(key==='Breaks'&&name==='MLeaderLine'){if(item===null)throw new NullReferenceException();copy[key].Add(item.Copy());}else copy[key].Add(item instanceof MLeaderData?item.Clone():Copy(item));}
  };
  Type.prototype.ValidateValues=function(version){MLeaderData.prototype.ValidateValues.call(this,version);
    if(name==='MLeaderContext'){MLeaderData.ValidateDirection(this.PlaneXAxis);MLeaderData.ValidateDirection(this.PlaneYAxis);MLeaderData.ValidateDirection(Vector3.CrossProduct(this.PlaneXAxis,this.PlaneYAxis));this.MText?.ValidateValues(version);this.Block?.ValidateValues(version);for(const node of this.Leaders)node.ValidateValues(version);}
    if(name==='MLeaderMTextContent'){MLeaderData.ValidateDirection(this.Normal);MLeaderData.ValidateDirection(this.Direction);if(this.Style===null)throw new InvalidOperationException('Embedded MULTILEADER text requires a registered STYLE reference.');if(this.ColumnType<0||this.ColumnType>2)throw new NotSupportedException('Unsupported embedded MULTILEADER text column type.');for(const height of this.ColumnHeights){MLeaderData.Finite(height);if(height<0)throw new InvalidOperationException('Column heights cannot be negative.');}}
    if(name==='MLeaderBlockContent'){MLeaderData.ValidateDirection(this.Normal);if(this.Block===null)throw new InvalidOperationException('Embedded MULTILEADER block content requires a registered BLOCK_RECORD reference.');if(this.TransformationMatrix.Count!==0&&this.TransformationMatrix.Count!==16)throw new InvalidOperationException('A stored MULTILEADER block matrix must contain exactly sixteen values.');for(const cell of this.TransformationMatrix)MLeaderData.Finite(cell);}
    if(name==='MLeaderNode'){if([...this.Breaks].some(x=>x===null))throw new InvalidOperationException('Break pairs cannot be null.');for(const line of this.Lines)line.ValidateValues(version);}
    if(name==='MLeaderLine'){for(const point of this.Vertices)MLeaderData.Finite(point);for(const group of this.Breaks)if(group===null||group.Breaks.Count===0||[...group.Breaks].some(x=>x===null))throw new InvalidOperationException('Line break groups require complete endpoint pairs.');}
  };
}
