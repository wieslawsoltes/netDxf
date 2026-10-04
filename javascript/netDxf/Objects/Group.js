// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from '../Tables/TableObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { EntityCollection } from '../Collections/EntityCollection.js';
import { GroupEntityChangeEventArgs } from './GroupEntityChangeEventArgs.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentException, NullReferenceException } from '../../runtime/Errors.js';
const constructorToken = Symbol('exact group constructor');
export class Group extends TableObject {
  #entities; #unnamed;
  Description = ''; IsSelectable = true;
  constructor(...args) {
    let name='',entities=null,checkName;
    if(args[0]===constructorToken) ({name,entities,checkName}=args[1]);
    else if(args.length===1 && args[0]!==null && typeof args[0]!=='string') entities=args[0];
    else if(args.length<=2) { name=args.length?args[0]:'';entities=args.length>1?args[1]:null; }
    else throw new ArgumentException('No matching Group constructor.');
    const internal=checkName!==undefined;
    super(name,DxfObjectCode.Group,internal?checkName:name!==null&&name!=='');
    this.#unnamed=name===null||name===''||(internal&&name.startsWith('*'));
    this.#entities=new EntityCollection();
    for(const event of ['EntityAdded','EntityRemoved'])Object.defineProperty(this,event,{value:new EventHook(),enumerable:true});
    this.#entities.BeforeAddItem.Add((_,e)=>{e.Cancel=e.Item===null||this.#entities.Contains(e.Item);});
    this.#entities.AddItem.Add((_,e)=>{
      if(e.Item===null)throw new NullReferenceException();e.Item.AddReactor(this);this.OnEntityAddedEvent(e.Item);
    });
    this.#entities.BeforeRemoveItem.Add(()=>{});
    this.#entities.RemoveItem.Add((_,e)=>{
      if(e.Item===null)throw new NullReferenceException();e.Item.RemoveReactor(this);this.OnEntityRemovedEvent(e.Item);
    });
    if(entities!==null)this.#entities.AddRange(entities);
  }
  static CreateOverload(signature,...args) {
    const select=(name,entities=null,checkName)=>new Group(constructorToken,{name,entities,checkName});
    if(signature==='string')return select(args[0]);
    if(signature==='System.Collections.Generic.IEnumerable<netDxf.Entities.EntityObject>')return select('',args[0]);
    if(signature==='string,System.Collections.Generic.IEnumerable<netDxf.Entities.EntityObject>')return select(args[0],args[1]);
    if(signature==='string,bool')return select(args[0],null,args[1]);
    throw new ArgumentException('Unknown Group constructor signature.','signature');
  }
  get Name() { return super.Name; } set Name(value) { super.Name=value;this.#unnamed=false; }
  get IsUnnamed() { return this.#unnamed; } set IsUnnamed(value) { this.#unnamed=value; } // internal in C#
  get Entities() { return this.#entities; }
  OnEntityAddedEvent(item) { this.EntityAdded.Invoke(this,new GroupEntityChangeEventArgs(item)); }
  OnEntityRemovedEvent(item) { this.EntityRemoved.Invoke(this,new GroupEntityChangeEventArgs(item)); }
  HasReferences() { return this.Owner!==null&&this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner===null?null:this.Owner.GetReferences(this.Name); }
  Clone(newName) {
    if(arguments.length===0)newName=this.IsUnnamed?'':this.Name;
    const entities=[];
    // Every entity is cloned before the replacement name is validated, as in C#.
    for(let i=0;i<this.#entities.Count;i++) {
      const entity=this.#entities.get_Item(i);if(entity===null)throw new NullReferenceException();entities.push(entity.Clone());
    }
    const copy=new Group(newName,entities);copy.Description=this.Description;copy.IsSelectable=this.IsSelectable;
    for(const data of this.XData.Values)copy.XData.Add(data.Clone());return copy;
  }
}
