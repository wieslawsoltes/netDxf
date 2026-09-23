// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { CheckTableText,TableSnapshot } from '../../runtime/TablePayload.js';
import { ArgumentNullException,ArgumentOutOfRangeException,NotSupportedException,RequireInteger } from '../../runtime/Errors.js';
import { DxfTableStyleRowBorders } from './DxfTableStyle.Borders.js';
import { DxfTableStyleRowDataTypes } from './DxfTableStyle.DataTypes.js';
const loaded=Symbol('internal projection');
export class DxfTableStyleHeader {
  constructor(description,flowDirection,storedFlags,horizontalCellMargin,verticalCellMargin,suppressTitle,suppressColumnHeading,internal=null) {
    // Internal TryRead deliberately does not add the public edit text validation.
    if(internal!==loaded) {
      CheckTableText(description,'description');
      if(description.length>255)throw new ArgumentOutOfRangeException('description');
      if(flowDirection<0||flowDirection>1)throw new ArgumentOutOfRangeException('flowDirection');
      if(!DxfTableStyleHeader.FiniteNonnegative(horizontalCellMargin))throw new ArgumentOutOfRangeException('horizontalCellMargin');
      if(!DxfTableStyleHeader.FiniteNonnegative(verticalCellMargin))throw new ArgumentOutOfRangeException('verticalCellMargin');
    }
    this.Description=description;this.FlowDirection=flowDirection;this.StoredFlags=storedFlags;
    this.HorizontalCellMargin=horizontalCellMargin;this.VerticalCellMargin=verticalCellMargin;
    this.SuppressTitle=suppressTitle;this.SuppressColumnHeading=suppressColumnHeading;this.StoredVersion=null;
    if(internal!==loaded)Object.freeze(this);
  }
  static FiniteNonnegative(value){return Number.isFinite(value)&&value>=0;}
  static TryRead(values,decode,sourceVersion) {
    let tags=Array.from(values),storedVersion=null;
    if(tags.length===8&&tags[0].Code===280&&tags[0].Value===0&&sourceVersion>=16){storedVersion=0;tags=tags.slice(1);}
    const codes=[3,70,71,40,41,280,281];if(tags.length!==codes.length||tags.some((t,i)=>t.Code!==codes[i]))return null;
    const description=decode(tags[0].Value),flow=tags[1].Value,title=tags[5].Value,heading=tags[6].Value,h=tags[3].Value,v=tags[4].Value;
    if(description.length>255||flow<0||flow>1||title<0||title>1||heading<0||heading>1||!this.FiniteNonnegative(h)||!this.FiniteNonnegative(v))return null;
    const result=new DxfTableStyleHeader(description,flow,tags[2].Value,h,v,title!==0,heading!==0,loaded);
    result.StoredVersion=storedVersion;return Object.freeze(result);
  }
}
function validateRequest(original,kind,value) {
  const parameter={Values:'values',Borders:'borders',DataTypes:'dataTypes',TextStyle:'textStyle'}[kind];
  if(value==null)throw new ArgumentNullException(parameter);
  if(kind!=='TextStyle'&&original[kind]===null)throw new NotSupportedException('The stored row '+kind+' are not qualified for editing.');
}
export class DxfTableStyleRow {
  #textStyle=null;
  constructor(tags,decode) {
    tags=Array.from(tags);this.Tags=TableSnapshot(tags);this.StoredTextStyleName=decode(tags[0].Value);
    this.Values=DxfTableStyleRowValues.TryRead(tags);this.Borders=DxfTableStyleRowBorders.TryRead(tags);this.DataTypes=DxfTableStyleRowDataTypes.TryRead(tags);Object.freeze(this);
  }
  get TextStyle(){return this.#textStyle;}
  BindTextStyle(style){this.#textStyle=style;}
  WithValues(values){validateRequest(this,'Values',values);return new DxfTableStyleRowEdit(this,values);}
  WithBorders(borders){validateRequest(this,'Borders',borders);return new DxfTableStyleRowEdit(this,null,borders);}
  WithDataTypes(dataTypes){validateRequest(this,'DataTypes',dataTypes);return new DxfTableStyleRowEdit(this,null,null,dataTypes);}
  WithTextStyle(textStyle){validateRequest(this,'TextStyle',textStyle);return new DxfTableStyleRowEdit(this,null,null,null,textStyle);}
}
export class DxfTableStyleRowEdit {
  constructor(original,values,borders=null,dataTypes=null,textStyle=null) {
    this.Original=original;this.Values=values;this.Borders=borders;this.DataTypes=dataTypes;this.TextStyle=textStyle;Object.freeze(this);
  }
  WithValues(values){validateRequest(this.Original,'Values',values);return new DxfTableStyleRowEdit(this.Original,values,this.Borders,this.DataTypes,this.TextStyle);}
  WithBorders(borders){validateRequest(this.Original,'Borders',borders);return new DxfTableStyleRowEdit(this.Original,this.Values,borders,this.DataTypes,this.TextStyle);}
  WithDataTypes(dataTypes){validateRequest(this.Original,'DataTypes',dataTypes);return new DxfTableStyleRowEdit(this.Original,this.Values,this.Borders,dataTypes,this.TextStyle);}
  WithTextStyle(textStyle){validateRequest(this.Original,'TextStyle',textStyle);return new DxfTableStyleRowEdit(this.Original,this.Values,this.Borders,this.DataTypes,textStyle);}
}
export class DxfTableStyleRowValues {
  constructor(textHeight,cellAlignment,storedTextColor,storedFillColor,backgroundColorEnabled) {
    if(!DxfTableStyleHeader.FiniteNonnegative(textHeight))throw new ArgumentOutOfRangeException('textHeight');
    this.TextHeight=textHeight;this.CellAlignment=RequireInteger(cellAlignment,-32768,32767,'cellAlignment');
    this.StoredTextColor=RequireInteger(storedTextColor,-32768,32767,'storedTextColor');this.StoredFillColor=RequireInteger(storedFillColor,-32768,32767,'storedFillColor');
    this.BackgroundColorEnabled=backgroundColorEnabled;Object.freeze(this);
  }
  static TryRead(tags) {
    const fields=new Map();
    for(const code of [140,170,62,63,283]){const values=Array.from(tags).filter(t=>t.Code===code);if(values.length!==1)return null;fields.set(code,values[0].Value);}
    const height=fields.get(140),fill=fields.get(283);if(!DxfTableStyleHeader.FiniteNonnegative(height)||fill<0||fill>1)return null;
    return new DxfTableStyleRowValues(height,fields.get(170),fields.get(62),fields.get(63),fill!==0);
  }
}
