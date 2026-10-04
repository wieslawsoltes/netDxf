// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from '../Tables/TableObject.js';
import { Block } from '../Blocks/Block.js';
import { Viewport } from '../Entities/Viewport.js';
import { ViewportStatusFlags } from '../Entities/ViewportStatusFlags.js';
import { PlotSettings } from './PlotSettings.js';
import { PlotFlags } from './PlotFlags.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { InstallViewFields } from '../../runtime/ViewFields.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException, NotSupportedException, NullReferenceException } from '../../runtime/Errors.js';
const exact=Symbol('private Layout constructor');
const ref=value=>{if(value==null)throw new NullReferenceException();return value;};
const fields={MinLimit:[()=>new Vector2(-20,-7.5)],MaxLimit:[()=>new Vector2(277,202.5)],
  BasePoint:[()=>Vector3.Zero],MinExtents:[()=>new Vector3(25.7,19.5,0)],MaxExtents:[()=>new Vector3(231.3,175.5,0)],
  Elevation:[0],UcsOrigin:[()=>Vector3.Zero],UcsXAxis:[()=>Vector3.UnitX],UcsYAxis:[()=>Vector3.UnitY]};
/** Detached layout metadata. Its model/paper block is registered only by a document host. */
export class Layout extends TableObject {
  #paper;#viewport;#tab=0;#block;
  PlotSettings;
  static get ModelSpaceName(){return 'Model';}
  static get ModelSpace(){return new Layout('Model',exact,Block.ModelSpace,new PlotSettings());}
  constructor(name,selector,block=null,plot=new PlotSettings()) {
    super(name,DxfObjectCode.Layout,true);
    if(name==null||name==='')throw new ArgumentNullException('name');
    if(selector!==undefined&&selector!==exact)throw new ArgumentException('No matching Layout constructor.');
    this.#block=block;this.PlotSettings=plot;
    // Classification tests the original untrimmed argument, as in the pinned constructor.
    this.#paper=!OrdinalIgnoreCaseEquals(name,Layout.ModelSpaceName);this.IsReserved=!this.#paper;
    if(this.#paper){this.#viewport=new Viewport(1);this.#viewport.ViewCenter=new Vector2(50,100);
      this.#viewport.Status=ViewportStatusFlags.AdaptiveGridDisplay|ViewportStatusFlags.DisplayGridBeyondDrawingLimits|ViewportStatusFlags.CurrentlyAlwaysEnabled|ViewportStatusFlags.UcsIconVisibility;
    }else{this.#viewport=null;ref(plot).Flags=PlotFlags.Initializing|PlotFlags.UpdatePaper|PlotFlags.ModelType|PlotFlags.DrawViewportsFirst|PlotFlags.PrintLineweights|PlotFlags.PlotPlotStyles|PlotFlags.UseStandardScale;}
  }
  static CreateOverload(signature,...args){
    if(signature==='string')return new Layout(args[0]);
    if(signature==='string,netDxf.Blocks.Block,netDxf.Objects.PlotSettings')return new Layout(args[0],exact,args[1],args[2]);
    throw new ArgumentException('Unknown Layout constructor signature.','signature');
  }
  get TabOrder(){return this.#tab;}
  set TabOrder(value){if(value<=0)throw new ArgumentException('The tab order index must be greater than zero.','value');this.#tab=value;}
  get IsPaperSpace(){return this.#paper;}
  get Viewport(){return this.#viewport;}
  set Viewport(value){
    const previous=this.#viewport;
    if(previous!==value&&this.Owner!==null&&ref(this.Owner.Owner).StoredTableReferencesRemoval(previous))
      throw new InvalidOperationException('A stored TABLE references the viewport being replaced.');
    this.#viewport=value;
    if(this.Owner!==null)ref(this.Owner.Owner).ReplaceLayoutViewportMetadata(this,previous,value);
  }
  get AssociatedBlock(){return this.#block;}set AssociatedBlock(value){this.#block=value;} // internal in C#
  HasReferences(){return this.Owner!==null&&this.Owner.HasReferences(this.Name);}
  GetReferences(){return this.Owner===null?null:this.Owner.GetReferences(this.Name);}
  CompareTo(other){if(other==null)throw new ArgumentNullException('other');return this.#tab-other.TabOrder;}
  Clone(newName=this.Name){
    if(!this.#paper)throw new NotSupportedException('The Model layout cannot be cloned.');
    if(OrdinalIgnoreCaseEquals(newName,Layout.ModelSpaceName))throw new ArgumentException('The layout name "Model" is reserved for the ModelSpace.');
    const copy=new Layout(newName,exact,null,ref(this.PlotSettings).Clone());
    // A new unregistered paper layout has tab order zero: the original Clone rejects it.
    copy.TabOrder=this.#tab;
    for(const name of Object.keys(fields))copy[name]=this[name];
    copy.Viewport=ref(this.#viewport).Clone();
    for(const data of this.XData.Values)copy.XData.Add(data.Clone());return copy;
  }
  AssignHandle(number){number=ref(this.Owner).AssignHandle(number);if(this.#paper)number=ref(this.#viewport).AssignHandle(number);return super.AssignHandle(number);}
}
InstallViewFields(Layout,fields);
