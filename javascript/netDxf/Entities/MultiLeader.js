// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {EntityObject} from './EntityObject.js';
import {EntityType} from './EntityType.js';
import {MLeaderData} from './MLeaderData.js';
import {MLeaderProperties} from './MLeaderProperties.Fields.js';
import {MLeaderContext} from './MLeaderContext.Fields.js';
import {Matrix3} from '../Matrix3.js';
import {Matrix4} from '../Matrix4.js';
import {Vector3} from '../Vector3.js';
import {ArgumentException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException} from '../../runtime/Errors.js';
/** Stored MULTILEADER grammar, not a style evaluator or geometric renderer. */
export class MultiLeader extends EntityObject{
  #properties;#context;#version=2;PendingInputReferences=false;
  constructor(){super(EntityType.MultiLeader,'MULTILEADER');this.#properties=new MLeaderProperties();this.#properties.Parent=this;this.#context=new MLeaderContext();this.#context.Parent=this;}
  get Properties(){return this.#properties;}get Context(){return this.#context;}
  get StoredVersion(){return this.#version;}set StoredVersion(value){if(value!==null&&value!==2)throw new ArgumentOutOfRangeException('value');this.#version=value;}
  get Version(){return 2;}
  get Data(){const owner=this;return{*[Symbol.iterator](){yield owner.#properties;yield owner.#context;}};}
  Validate(document,version){if(arguments.length===0){document=MLeaderData.RegisteredDocument(this);version=document?.DrawingVariables.AcadVer??18;}
    if(version<15)throw new NotSupportedException('MULTILEADER requires the qualified R2007 or later writer profile.');
    const p=this.#properties,c=this.#context;p.ValidateValues(version);c.ValidateValues(version);
    if(p.Style===null||p.LeaderLinetype===null||p.TextStyle===null)throw new InvalidOperationException('MULTILEADER requires style, leader linetype and text-style references.');
    const type=p.ContentType;if(type<0||type>2)throw new NotSupportedException('Only no-content, block and MTEXT MULTILEADER content types are qualified.');
    if((type===1)!==(c.Block!==null)||(type===2)!==(c.MText!==null))throw new InvalidOperationException('MULTILEADER content type and stored context disagree.');
    for(const arrow of p.ArrowHeads)if(arrow.Block===null)throw new InvalidOperationException('Repeated MULTILEADER arrow data requires a block record.');
    for(const attribute of p.BlockAttributes){if(attribute.Definition===null)throw new InvalidOperationException('MULTILEADER attribute data requires an ATTDEF reference.');if(c.Block===null||attribute.Definition.Owner?.Record!==c.Block.Block)throw new InvalidOperationException('MULTILEADER attributes must reference definitions in the embedded content block.');}
    p.CheckDocument(document);c.CheckDocument(document);
  }
  ValidateIncoming(document){if(this.PendingInputReferences)return;this.Validate(document,document.DrawingVariables.AcadVer);for(const data of this.XData.Values)if(data.ApplicationRegistry.Owner!==null&&data.ApplicationRegistry.Owner!==document.ApplicationRegistries)throw new ArgumentException('Clone foreign-owned XData registries before adding a MULTILEADER to this document.');}
  TransformBy(matrix,translation){
    if(matrix instanceof Matrix4&&arguments.length===1){for(let r=1;r<=4;r++)for(let c=1;c<=4;c++)if(matrix['M'+r+c]!==+(r===c))throw new NotSupportedException('MULTILEADER geometric transforms are not qualified; no stored value was changed.');return;}
    if(!(matrix instanceof Matrix3)||!(translation instanceof Vector3))throw new ArgumentException('Expected Matrix4 or Matrix3 and Vector3.');
    for(let r=1;r<=3;r++)for(let c=1;c<=3;c++)if(matrix['M'+r+c]!==+(r===c))throw new NotSupportedException('MULTILEADER geometric transforms are not qualified; no stored value was changed.');
    if(translation.X!==0||translation.Y!==0||translation.Z!==0)throw new NotSupportedException('MULTILEADER geometric transforms are not qualified; no stored value was changed.');
  }
  Clone(){const copy=this.$copyEntityAttributes(new MultiLeader());copy.StoredVersion=this.#version;this.#properties.CopyValuesTo(copy.#properties);this.#properties.CopyChildrenTo(copy.#properties);this.#context.CopyValuesTo(copy.#context);this.#context.CopyChildrenTo(copy.#context);return this.$finishEntityClone(copy);}
}
