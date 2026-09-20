// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import {EntityObject} from './EntityObject.js';
import {EntityType} from './EntityType.js';
import {DxfObjectCode} from '../DxfObjectCode.js';
import {Vector2} from '../Vector2.js';
import {Vector3} from '../Vector3.js';
import {Matrix3} from '../Matrix3.js';
import {MathHelper} from '../MathHelper.js';
import {Spline} from './Spline.js';
import {PeriodicSplineData} from './PeriodicSplineData.js';
import {ObservableCollection} from '../Collections/ObservableCollection.js';
import {ObservableCollectionEventArgs} from '../Collections/ObservableCollectionEventArgs.js';
import {ReferenceList} from '../../runtime/ReferenceList.js';
import {EventHook} from '../../runtime/EventHook.js';
import {FiniteVector2Collection} from '../../runtime/HatchRuntime.js';
import {HatchSourceRelations} from './HatchSourceRelations.js';
import {TransformHatch} from './Hatch.Transform.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,NotSupportedException} from '../../runtime/Errors.js';
const opaque=e=>e?.constructor.name==='DxfOpaqueEntity';
function opaqueCall(entity,method,...args){if(typeof entity[method]!=='function')throw new NotSupportedException('The retained opaque source host is unavailable.');return entity[method](...args);}
/** Detached HATCH model with source-defined path events and associative backlink bookkeeping. */
export class Hatch extends EntityObject {
  #pattern;#paths=new ObservableCollection();#association=false;#data=new DataView(new ArrayBuffer(16));#pixelPresent=true;#seeds=new FiniteVector2Collection('item');
  constructor(pattern,...args){
    super(EntityType.Hatch,DxfObjectCode.Hatch);if(pattern==null)throw new ArgumentNullException('pattern');this.#pattern=pattern;
    if(args.length!==1&&args.length!==2)throw new ArgumentException('No matching Hatch constructor.');
    if(args.length===2&&args[0]==null)throw new ArgumentNullException('paths');
    for(const name of ['HatchBoundaryPathAdded','HatchBoundaryPathRemoved'])Object.defineProperty(this,name,{value:new EventHook(),enumerable:true});
    this.#seeds.Add(Vector2.Zero);
    this.#paths.BeforeAddItem.Add((_,e)=>this.#beforeAdd(e));this.#paths.AddItem.Add((_,e)=>this.#added(e));
    this.#paths.BeforeRemoveItem.Add(()=>this.ValidateOpaqueSourceRelease());this.#paths.RemoveItem.Add((_,e)=>this.#removed(e));
    this.#association=args.at(-1);if(args.length===1)return;
    const paths=Array.from(args[0]);if(new Set(paths).size!==paths.length)throw new ArgumentException('Duplicate HATCH boundary path instances.','paths');
    for(const p of paths){if(p==null)throw new ArgumentException('HATCH paths cannot contain null.','paths');HatchSourceRelations.ValidatePathOwner(this,p,null);}
    for(const p of paths){
      if(this.#association){if(p.Entities.Count===0)for(const e of p.Edges)p.AddContour(e.ConvertTo());}else p.ClearContour();
      this.#paths.Add(p);
    }
  }
  get PixelSize(){return this.#pixelPresent?this.#data.getFloat64(8):null;}
  set PixelSize(v){if(v!==null&&(!Number.isFinite(v)||v<0))throw new ArgumentOutOfRangeException('value',v);this.#pixelPresent=v!==null;if(v!==null)this.#data.setFloat64(8,v);}
  get SeedPoints(){return this.#seeds;}get BoundaryPaths(){return this.#paths;}
  get Pattern(){return this.#pattern;}set Pattern(value){if(value==null)throw new ArgumentNullException('value');this.#pattern=value;}
  get Associative(){return this.#association;}
  get Elevation(){return this.#data.getFloat64(0);}set Elevation(v){this.#data.setFloat64(0,v);}
  OnHatchBoundaryPathAddedEvent(item){this.HatchBoundaryPathAdded.Invoke(this,new ObservableCollectionEventArgs(item));}
  OnHatchBoundaryPathRemovedEvent(item){this.HatchBoundaryPathRemoved.Invoke(this,new ObservableCollectionEventArgs(item));}
  #beforeAdd(e){
    e.Cancel=e.Item==null;if(e.Cancel)return;
    if(this.#paths.Contains(e.Item))throw new ArgumentException('Duplicate HATCH boundary path instance.');
    for(const source of e.Item.Entities)if(opaque(source))opaqueCall(source,'ValidateHatchSourceAddition');
    HatchSourceRelations.ValidatePathOwner(this,e.Item,this.Owner);
  }
  #added(e){e.Item.ContainingHatch=this;if(this.#association){for(const source of e.Item.Entities)source.AddReactor(this);}else e.Item.ClearContour();this.OnHatchBoundaryPathAddedEvent(e.Item);}
  #removed(e){
    if(!this.#paths.Contains(e.Item))e.Item.ContainingHatch=null;
    if(this.#association)for(const source of e.Item.Entities)source.RemoveReactor(this);
    for(const source of e.Item.Entities)this.#removeBacklink(source);this.OnHatchBoundaryPathRemovedEvent(e.Item);
  }
  #removeBacklink(entity){
    if(opaque(entity))opaqueCall(entity,'ReleaseHatchSourceBacklink',this);
    for(const reactor of entity.Reactors)if(reactor===this)return;
    for(let i=entity.PersistentReactors.Count-1;i>=0;i--)if(entity.PersistentReactors.get_Item(i)===this)entity.PersistentReactors.RemoveAt(i);
  }
  ValidateOpaqueSourceRelease(){
    const sources=new Set();for(const p of this.#paths)for(const e of p.Entities)if(opaque(e))sources.add(e);
    for(const e of sources)opaqueCall(e,'ValidateHatchSourceRelease');
  }
  UnLinkBoundary(){
    this.ValidateOpaqueSourceRelease();const boundary=new ReferenceList();this.#association=false;
    for(const p of this.#paths)for(const e of p.Entities){e.RemoveReactor(this);boundary.Add(e);}
    for(const e of boundary)this.#removeBacklink(e);for(const p of this.#paths)p.ClearContour();return boundary;
  }
  CreateBoundary(linkBoundary){
    const prepared=[],boundary=new ReferenceList(),trans=MathHelper.ArbitraryAxis(this.Normal),position=Matrix3.Multiply(trans,new Vector3(0,0,this.Elevation));
    for(const path of this.#paths)for(const edge of path.Edges){
      const entity=edge.ConvertTo(),point=p=>Vector3.Add(Matrix3.Multiply(trans,p),position);
      switch(entity.Type){
        case EntityType.Arc:case EntityType.Circle:case EntityType.Ellipse:entity.Center=point(entity.Center);entity.Normal=Matrix3.Multiply(trans,entity.Normal);boundary.Add(entity);break;
        case EntityType.Line:entity.StartPoint=point(entity.StartPoint);entity.EndPoint=point(entity.EndPoint);entity.Normal=Matrix3.Multiply(trans,entity.Normal);boundary.Add(entity);break;
        case EntityType.Polyline2D:entity.Elevation=this.Elevation;entity.Normal=this.Normal;boundary.Add(entity);break;
        case EntityType.Spline:entity.TransformBy(trans,position);boundary.Add(entity);break;
      }
      if(entity instanceof Spline&&entity.IsClosedPeriodic)PeriodicSplineData.Validate(entity);prepared.push([path,entity]);
    }
    if(this.#association)this.UnLinkBoundary();this.#association=linkBoundary;
    if(linkBoundary)for(const [path,entity] of prepared){path.AddContour(entity);entity.AddReactor(this);this.OnHatchBoundaryPathAddedEvent(path);}
    return boundary;
  }
  TransformBy(matrix,translation){[matrix,translation]=this.$transformArguments(matrix,translation);TransformHatch(this,matrix,translation);}
  Clone(){
    const copy=this.$copyEntityAttributes(new Hatch(this.#pattern.Clone(),false));copy.Elevation=this.Elevation;copy.PixelSize=this.PixelSize;
    copy.#seeds.Clear();for(const seed of this.#seeds)copy.#seeds.Add(seed);
    for(const p of this.#paths)copy.#paths.Add(p.Clone());return this.$finishEntityClone(copy);
  }
}
