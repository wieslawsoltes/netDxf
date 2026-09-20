// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from '../Tables/TableObject.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { AciColor } from '../AciColor.js';
import { ObservableCollection } from '../Collections/ObservableCollection.js';
import { MLineStyleElement } from './MLineStyleElement.js';
import { MLineStyleElementChangeEventArgs } from './MLineStyleElementChangeEventArgs.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NullReferenceException } from '../../runtime/Errors.js';
export class MLineStyle extends TableObject {
  #elements; #description; #fill=AciColor.ByLayer; #angles=new DataView(new ArrayBuffer(16));
  Flags=0;
  static get DefaultName() { return 'Standard'; } static get Default() { return new MLineStyle(MLineStyle.DefaultName); }
  constructor(name,elements=null,description='') {
    super(name,DxfObjectCode.MLineStyle,true);
    if(name==null||name==='')throw new ArgumentNullException('name');
    if(arguments.length===2&&typeof elements==='string') { description=elements;elements=null; }
    this.Description=description;this.StartAngle=90;this.EndAngle=90;
    for(const name of ['MLineStyleElementAdded','MLineStyleElementRemoved','MLineStyleElementLinetypeChanged'])Object.defineProperty(this,name,{value:new EventHook(),enumerable:true});
    const changed=(_,e)=>{e.NewValue=this.OnMLineStyleElementLinetypeChangedEvent(e.OldValue,e.NewValue);};
    this.#elements=new ObservableCollection();
    this.#elements.BeforeAddItem.Add((_,e)=>{e.Cancel=e.Item==null;});
    this.#elements.AddItem.Add((_,e)=>{this.OnMLineStyleElementAddedEvent(e.Item);if(e.Item==null)throw new NullReferenceException();e.Item.LinetypeChanged.Add(changed);});
    this.#elements.BeforeRemoveItem.Add(()=>{});
    this.#elements.RemoveItem.Add((_,e)=>{this.OnMLineStyleElementRemovedEvent(e.Item);if(e.Item==null)throw new NullReferenceException();e.Item.LinetypeChanged.Remove(changed);});
    this.#elements.AddRange(elements??[new MLineStyleElement(.5),new MLineStyleElement(-.5)]);this.#elements.Sort();
    if(this.#elements.Count<1)throw new ArgumentOutOfRangeException('elements',this.#elements.Count);
  }
  static CreateOverload(signature,...args) {
    if(signature==='string,string')return new MLineStyle(args[0],null,args[1]);
    if(['string','string,System.Collections.Generic.IEnumerable<netDxf.Objects.MLineStyleElement>','string,System.Collections.Generic.IEnumerable<netDxf.Objects.MLineStyleElement>,string'].includes(signature))return new MLineStyle(...args);
    throw new ArgumentException('Unknown MLineStyle constructor signature.','signature');
  }
  get Description() { return this.#description; } set Description(value) { this.#description=value==null||value===''?'':value; }
  get FillColor() { return this.#fill; } set FillColor(value) { if(value==null)throw new ArgumentNullException('value');this.#fill=value; }
  get StartAngle() { return this.#angles.getFloat64(0); } set StartAngle(value) { this.#angle(0,value); }
  get EndAngle() { return this.#angles.getFloat64(8); } set EndAngle(value) { this.#angle(8,value); }
  #angle(offset,value) { if(value<10||value>170)throw new ArgumentOutOfRangeException('value',value);this.#angles.setFloat64(offset,value); }
  get Elements() { return this.#elements; }
  OnMLineStyleElementAddedEvent(item) { this.MLineStyleElementAdded.Invoke(this,new MLineStyleElementChangeEventArgs(item)); }
  OnMLineStyleElementRemovedEvent(item) { this.MLineStyleElementRemoved.Invoke(this,new MLineStyleElementChangeEventArgs(item)); }
  OnMLineStyleElementLinetypeChangedEvent(oldValue,newValue) { const e=new TableObjectChangedEventArgs(oldValue,newValue);this.MLineStyleElementLinetypeChanged.Invoke(this,e);return e.NewValue; }
  HasReferences() { return this.Owner!==null&&this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner===null?null:this.Owner.GetReferences(this.Name); }
  Clone(newName=this.Name) {
    const elements=[];for(const e of this.#elements){if(e==null)throw new NullReferenceException();elements.push(e.Clone());}
    const copy=new MLineStyle(newName,elements);copy.Flags=this.Flags;copy.Description=this.#description;copy.FillColor=this.#fill.Clone();copy.StartAngle=this.StartAngle;copy.EndAngle=this.EndAngle;
    for(const data of this.XData.Values)copy.XData.Add(data.Clone());return copy;
  }
}
