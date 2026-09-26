// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import {EntityObject} from './EntityObject.js';
import {EntityType} from './EntityType.js';
import {EntityChangeEventArgs} from './EntityChangeEventArgs.js';
import {LeaderPathType} from './LeaderPathType.js';
import {MText} from './MText.js';
import {Text} from './Text.js';
import {Insert} from './Insert.js';
import {Tolerance} from './Tolerance.js';
import {ToleranceEntry} from './ToleranceEntry.js';
import {MTextAttachmentPoint as M} from './MTextAttachmentPoint.js';
import {TextAlignment as T} from './TextAligment.js';
import {Block} from '../Blocks/Block.js';
import {AciColor} from '../AciColor.js';
import {DxfObjectCode} from '../DxfObjectCode.js';
import {DimensionStyle} from '../Tables/DimensionStyle.js';
import {DimensionStyleOverride} from '../Tables/DimensionStyleOverride.js';
import {DimensionStyleOverrideType as O} from '../Tables/DimensionStyleOverrideType.js';
import {DimensionStyleOverrideChangeEventArgs} from '../Tables/DimensionStyleOverrideChangeEventArgs.js';
import {DimensionStyleOverrideDictionary} from '../Collections/DimensionStyleOverrideDictionary.js';
import {TableObjectChangedEventArgs} from '../Tables/TableObjectChangedEventArgs.js';
import {Vector2} from '../Vector2.js';
import {Vector3} from '../Vector3.js';
import {Matrix3} from '../Matrix3.js';
import {MathHelper} from '../MathHelper.js';
import {Copy,DotNetMath,MultiplyDouble as mul} from '../../runtime/GeometryRuntime.js';
import {ValueList} from '../../runtime/ValueList.js';
import {EventHook} from '../../runtime/EventHook.js';
import {BoxedScalar} from '../../runtime/BoxedScalar.js';
import {BoxedString} from '../../runtime/BoxedString.js';
import {HeaderEnum} from '../../runtime/HeaderBox.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,Exception,NullReferenceException} from '../../runtime/Errors.js';
const exact=Symbol('exact Leader constructor');
const ref=value=>{if(value==null)throw new NullReferenceException();return value;};
const number=value=>value instanceof BoxedScalar||value instanceof HeaderEnum?value.Value:value;
const rightText=[T.TopRight,T.MiddleRight,T.BottomRight,T.BaselineRight];
const leftText=[T.TopLeft,T.MiddleLeft,T.BottomLeft,T.BaselineLeft];
const rightMText=[M.TopRight,M.MiddleRight,M.BottomRight];
const leftMText=[M.TopLeft,M.MiddleLeft,M.BottomLeft];
class LeaderVertexList extends ValueList {
  GetEnumerator(){
    const iterator=super.GetEnumerator();let active=false;
    return {get Current(){return active?iterator.Current:Vector2.Zero;},MoveNext(){active=iterator.MoveNext();return active;},
      Reset(){iterator.Reset();active=false;},Dispose(){iterator.Dispose();},
      next(){return this.MoveNext()?{done:false,value:this.Current}:{done:true,value:undefined};},[Symbol.iterator](){return this;}};
  }
}
/** Classic LEADER with real annotation entities. Registered document adoption remains external. */
export class Leader extends EntityObject {
  #style;#vertices;#annotation=null;#hookline=false;#lineColor=AciColor.ByLayer;
  #offset=Vector2.Zero;#direction=Vector2.UnitX;#elevation=new DataView(new ArrayBuffer(8));#overrides;
  ShowArrowhead=true;PathType=LeaderPathType.StraightLineSegments;
  constructor(...args){
    super(EntityType.Leader,DxfObjectCode.Leader);
    let vertices,style,kind=null,content,hookline=false;
    if(args[0]===exact)({vertices,style,kind=null,content,hookline=false}=args[1]);
    else if(typeof args[0]==='string'||args[0] instanceof ToleranceEntry||args[0] instanceof Block){
      [content,vertices,style=DimensionStyle.Default]=args;
      kind=typeof content==='string'?'text':content instanceof Block?'block':'tolerance';
    }else [vertices,style=DimensionStyle.Default,hookline=false]=args;
    if(vertices==null)throw new ArgumentNullException('vertexes');
    this.#vertices=new LeaderVertexList(vertices);
    if(this.#vertices.Count<2)throw new ArgumentOutOfRangeException('vertexes',this.#vertices.Count);
    if(style==null)throw new ArgumentNullException('style');
    this.#style=style;this.#hookline=hookline;
    for(const name of ['LeaderStyleChanged','AnnotationAdded','AnnotationRemoved','DimensionStyleOverrideAdded','DimensionStyleOverrideRemoved'])
      Object.defineProperty(this,name,{value:new EventHook(),enumerable:true});
    this.#overrides=new DimensionStyleOverrideDictionary();
    this.#overrides.BeforeAddItem.Add((_,e)=>{
      const old={};if(this.#overrides.TryGetValue(e.Item.Type,old)&&Leader.SameOverrideValue(old.value,e.Item))e.Cancel=true;
    });
    this.#overrides.AddItem.Add((_,e)=>this.OnDimensionStyleOverrideAddedEvent(e.Item));
    this.#overrides.BeforeRemoveItem.Add(()=>{});
    this.#overrides.RemoveItem.Add((_,e)=>this.OnDimensionStyleOverrideRemovedEvent(e.Item));
    if(kind!==null){this.Annotation=this.#buildAnnotation(kind,content);if(kind==='text')this.CalculateAnnotationDirection();}
  }
  static CreateOverload(signature,...args){
    const v='System.Collections.Generic.IEnumerable<netDxf.Vector2>',s='netDxf.Tables.DimensionStyle';
    if([v,v+','+s,v+','+s+',bool'].includes(signature))return new Leader(exact,{vertices:args[0],style:args.length>1?args[1]:DimensionStyle.Default,hookline:args[2]??false});
    for(const [type,kind]of [['string','text'],['netDxf.Entities.ToleranceEntry','tolerance'],['netDxf.Blocks.Block','block']])
      if(signature===type+','+v||signature===type+','+v+','+s)return new Leader(exact,{kind,content:args[0],vertices:args[1],style:args.length>2?args[2]:DimensionStyle.Default});
    throw new ArgumentException('Unknown Leader constructor signature.','signature');
  }
  static SameOverrideValue(old,item){
    if(old===item)return true;
    const a=old.Value,b=item.Value;
    // CLR object-valued numeric/bool arguments are separately boxed. Explicit boxed
    // adapters preserve shared identities; primitive empty strings are interned.
    if(a===''||a instanceof BoxedString&&a.Value==='')return b===''||b instanceof BoxedString&&b.Value==='';
    return a===null?b===null:typeof a==='object'&&a===b;
  }
  get Style(){return this.#style;}
  set Style(value){if(value==null)throw new ArgumentNullException('value');this.#style=this.OnDimensionStyleChangedEvent(this.#style,value);}
  OnDimensionStyleChangedEvent(oldValue,newValue){const e=new TableObjectChangedEventArgs(oldValue,newValue);this.LeaderStyleChanged.Invoke(this,e);return e.NewValue;}
  OnAnnotationAddedEvent(item){this.AnnotationAdded.Invoke(this,new EntityChangeEventArgs(item));}
  OnAnnotationRemovedEvent(item){this.AnnotationRemoved.Invoke(this,new EntityChangeEventArgs(item));}
  OnDimensionStyleOverrideAddedEvent(item){this.DimensionStyleOverrideAdded.Invoke(this,new DimensionStyleOverrideChangeEventArgs(item));}
  OnDimensionStyleOverrideRemovedEvent(item){this.DimensionStyleOverrideRemoved.Invoke(this,new DimensionStyleOverrideChangeEventArgs(item));}
  get StyleOverrides(){return this.#overrides;}
  get Vertexes(){return this.#vertices;}
  get Annotation(){return this.#annotation;}
  set Annotation(value){
    if(value!=null&&![EntityType.MText,EntityType.Text,EntityType.Insert,EntityType.Tolerance].includes(value.Type))throw new ArgumentException('Only MText, Text, Insert, and Tolerance entities are supported as a leader annotation.','value');
    value??=null;if(this.#annotation===value)return;
    if(this.#annotation!==null){this.#annotation.RemoveReactor(this);this.OnAnnotationRemovedEvent(this.#annotation);}
    if(value!==null){value.AddReactor(this);this.OnAnnotationAddedEvent(value);}
    this.#annotation=value;
  }
  get Hook(){return this.#vertices.get_Item(this.#vertices.Count-1);}
  set Hook(value){this.#vertices.set_Item(this.#vertices.Count-1,value);}
  get HasHookline(){return this.#hookline;}
  set HasHookline(value){
    this.#checkVertices();if(this.#hookline!==value){if(value)this.#vertices.Insert(this.#vertices.Count-1,this.CalculateHookLine());else this.#vertices.RemoveAt(this.#vertices.Count-2);}this.#hookline=value;
  }
  get LineColor(){return this.#lineColor;}
  set LineColor(value){if(value==null)throw new ArgumentNullException('value');this.#lineColor=value;}
  get Offset(){return Copy(this.#offset);}set Offset(value){this.#offset=Copy(value);}
  get Direction(){return Copy(this.#direction);}set Direction(value){this.#direction=Vector2.Normalize(value);}
  get Elevation(){return this.#elevation.getFloat64(0);}set Elevation(value){this.#elevation.setFloat64(0,value);}
  #checkVertices(){if(this.#vertices.Count<2)throw new Exception('The leader vertexes list requires at least two points.');}
  Update(resetAnnotationPosition){
    this.#checkVertices();if(this.#annotation===null)return;
    this.CalculateAnnotationDirection();
    if(resetAnnotationPosition)this.ResetAnnotationPosition();else this.ResetHookPosition();
    if(this.#hookline)this.#vertices.set_Item(this.#vertices.Count-2,this.CalculateHookLine());
  }
  CalculateAnnotationDirection(){
    let angle=0;const a=this.#annotation;
    if(a!==null){angle=a.Rotation;
      if(a.Type===EntityType.MText){if(rightMText.includes(a.AttachmentPoint))angle+=180;}
      else if(a.Type===EntityType.Text){if(rightText.includes(a.Alignment))angle+=180;}
      else if(a.Type!==EntityType.Insert&&a.Type!==EntityType.Tolerance)throw new ArgumentException('Unsupported leader annotation.','annotation');
    }
    this.#direction=Vector2.Rotate(Vector2.UnitX,mul(angle,MathHelper.DegToRad));
  }
  #effective(name){const initial=ref(this.Style)[name],found={};return this.#overrides.TryGetValue(O[name],found)?number(found.value.Value):initial;}
  CalculateHookLine(){
    const scale=this.#effective('DimScaleOverall'),size=this.#effective('ArrowSize');
    return Vector2.Subtract(this.Hook,Vector2.Multiply(Vector2.Multiply(this.Direction,size),scale));
  }
  #reset(resetAnnotation){
    // Preserve getter/override order and left-associative arithmetic from both C# routines.
    const placement=this.#effective('TextVerticalPlacement');let gap=this.#effective('TextOffset');
    const scale=this.#effective('DimScaleOverall'),height=this.#effective('TextHeight'),color=this.#effective('TextColor');
    const hook=resetAnnotation?this.Hook:null,dir=this.Direction,a=ref(this.#annotation);gap=mul(gap,scale);
    if(a.Type===EntityType.MText||a.Type===EntityType.Text){
      let side=MathHelper.Sign(dir.X);if(side===0)side=MathHelper.Sign(dir.Y);if(a.Rotation>90&&a.Rotation<=270)side*=-1;
      const isM=a.Type===EntityType.MText,key=isM?'AttachmentPoint':'Alignment',right=isM?rightMText:rightText,left=isM?leftMText:leftText;
      const source=side>=0?right:left,dest=side>=0?left:right,at=source.indexOf(a[key]);if(at!==-1)a[key]=dest[at];
      const offset=new Vector2(mul(side,gap),placement===0?0:gap),rotated=Vector2.Rotate(offset,mul(a.Rotation,MathHelper.DegToRad));
      if(resetAnnotation)a.Position=MathHelper.Transform(Vector2.Add(Vector2.Add(hook,this.Offset),rotated),this.Normal,this.Elevation);
      else this.Hook=Vector2.Subtract(Vector2.Subtract(MathHelper.Transform(a.Position,this.Normal,{value:0}),this.Offset),rotated);
      a.Height=mul(height,scale);a.Color=ref(color).IsByBlock?AciColor.ByLayer:color;
    }else if(a.Type===EntityType.Insert||a.Type===EntityType.Tolerance){
      if(resetAnnotation)a.Position=MathHelper.Transform(Vector2.Add(hook,this.Offset),this.Normal,this.Elevation);
      else this.Hook=Vector2.Subtract(MathHelper.Transform(a.Position,this.Normal,{value:0}),this.Offset);
      a.Color=ref(color).IsByBlock?AciColor.ByLayer:color;
    }else throw new Exception('The entity type is not supported as a leader annotation.');
  }
  ResetHookPosition(){this.#reset(false);}ResetAnnotationPosition(){this.#reset(true);}
  #buildAnnotation(kind,content){
    const s=ref(this.#style);
    if(kind==='block'){const a=new Insert(content,this.Hook);a.Color=ref(s.TextColor).IsByBlock?AciColor.ByLayer:s.TextColor;return a;}
    if(kind==='tolerance'){const a=new Tolerance(content,this.Hook);a.Color=ref(s.TextColor).IsByBlock?AciColor.ByLayer:s.TextColor;a.Style=s;return a;}
    const side=DotNetMath.Sign(this.Hook.X-this.#vertices.get_Item(this.#vertices.Count-2).X),centered=s.TextVerticalPlacement===0;
    const offset=new Vector2(mul(mul(side,s.TextOffset),s.DimScaleOverall),centered?0:mul(s.TextOffset,s.DimScaleOverall));
    const pos=MathHelper.Transform(Vector2.Add(this.Hook,offset),this.Normal,this.Elevation);
    const a=new MText(content,pos,mul(s.TextHeight,s.DimScaleOverall),0,s.TextStyle);
    a.Color=ref(s.TextColor).IsByBlock?AciColor.ByLayer:s.TextColor;
    a.AttachmentPoint=centered?(side>=0?M.MiddleLeft:M.MiddleRight):(side>=0?M.BottomLeft:M.BottomRight);
    if(!MathHelper.IsZero(this.Hook.Y-this.#vertices.get_Item(this.#vertices.Count-2).Y))this.HasHookline=true;
    return a;
  }
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    let normal=Matrix3.Multiply(matrix,this.Normal);if(Vector3.Equals(Vector3.Zero,normal))normal=this.Normal;
    let elevation=this.Elevation;const ow=MathHelper.ArbitraryAxis(this.Normal),wo=MathHelper.ArbitraryAxis(normal).Transpose();
    for(let i=0;i<this.#vertices.Count;i++){
      const point=this.#vertices.get_Item(i);let v=Matrix3.Multiply(ow,new Vector3(point.X,point.Y,this.Elevation));
      v=Vector3.Add(Matrix3.Multiply(matrix,v),translation);v=Matrix3.Multiply(wo,v);
      this.#vertices.set_Item(i,new Vector2(v.X,v.Y));elevation=v.Z;
    }
    let offset=Matrix3.Multiply(ow,new Vector3(this.Offset.X,this.Offset.Y,this.Elevation));offset=Matrix3.Multiply(matrix,offset);offset=Matrix3.Multiply(wo,offset);
    this.Offset=new Vector2(offset.X,offset.Y);this.Elevation=elevation;this.Normal=normal;
    this.#annotation?.TransformBy(matrix,translation);
  }
  Clone(){
    const copy=this.$copyEntityAttributes(new Leader(this.#vertices));
    copy.Elevation=this.Elevation;copy.Style=ref(this.#style).Clone();copy.ShowArrowhead=this.ShowArrowhead;copy.PathType=this.PathType;
    copy.LineColor=this.#lineColor;copy.Annotation=this.#annotation?.Clone()??null;copy.Offset=this.#offset;copy.#hookline=this.#hookline;
    // Source Clone deliberately shares LineColor and does not copy the Direction cache.
    for(const override of this.#overrides.Values){const value=override.Value;copy.StyleOverrides.Add(new DimensionStyleOverride(override.Type,value?.Clone?value.Clone():value));}
    return this.$finishEntityClone(copy);
  }
}
