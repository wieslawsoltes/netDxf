// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { AciColor } from '../AciColor.js';
import { CoordinateSystem } from '../CoordinateSystem.js';
import { Copy, DotNetMath as M, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceList } from '../../runtime/EntityRuntime.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException, RequireInteger } from '../../runtime/Errors.js';
export const MTextColumnType = Object.freeze({None:0,Static:1,Dynamic:2});
export const MTextColumnStorage = Object.freeze({Direct:0,LegacyLinked:1,Embedded:2});
export class MTextColumns {
  #type = 1; #storage = 2; #count = 1; #width = 1; #gutter = 0; #definedHeight = 0; #totalHeight = 0;
  #heights = new ReferenceList(); #links = new ReferenceList(); #direction = null; #insertion = null;
  AutoHeight = false; FlowReversed = false; StoredTotalWidth = null; EmbeddedReferenceWidth = null;
  PendingHandles = new ReferenceList();
  get Type() { return this.#type; } set Type(v) { this.#type = RequireInteger(v,0,2); }
  get Storage() { return this.#storage; } set Storage(v) { this.#storage = RequireInteger(v,0,2); }
  get Count() { return this.#count; } set Count(v) { this.#count = RequireInteger(v,1,32767); this.StoredTotalWidth = null; }
  get Width() { return this.#width; } set Width(v) { MTextColumns.Positive(v,'value'); this.#width = v; this.StoredTotalWidth = null; }
  get Gutter() { return this.#gutter; } set Gutter(v) { MTextColumns.NonNegative(v,'value'); this.#gutter = v; this.StoredTotalWidth = null; }
  get DefinedHeight() { return this.#definedHeight; } set DefinedHeight(v) { MTextColumns.NonNegative(v,'value'); this.#definedHeight = v; }
  get TotalHeight() { return this.#totalHeight; } set TotalHeight(v) { MTextColumns.NonNegative(v,'value'); this.#totalHeight = v; }
  get TotalWidth() { return mul(this.Count,this.Width) + mul(this.Count-1,this.Gutter); }
  get EmbeddedTextDirection() { return Copy(this.#direction); } set EmbeddedTextDirection(v) { this.#direction = Copy(v); }
  get EmbeddedInsertionPoint() { return Copy(this.#insertion); } set EmbeddedInsertionPoint(v) { this.#insertion = Copy(v); }
  get Heights() { return this.#heights; }
  get LinkedColumns() { return this.#links; }
  static NonNegative(value,name) { if (!Number.isFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
  static Positive(value,name) { MTextColumns.NonNegative(value,name); if (value === 0) throw new ArgumentOutOfRangeException(name); }
  static $validateVector(vector) { if (![vector.X,vector.Y,vector.Z].every(Number.isFinite)) throw new ArgumentOutOfRangeException('vector'); }
  Validate() {
    MTextColumns.Positive(this.TotalWidth,'TotalWidth');
    if (this.StoredTotalWidth !== null) {
      MTextColumns.NonNegative(this.StoredTotalWidth,'StoredTotalWidth');
      if (this.Type === 2 && this.AutoHeight && Math.abs((this.StoredTotalWidth+this.Gutter)/(this.Width+this.Gutter)-this.Count) > 1e-5)
        throw new InvalidOperationException('The stored total width must encode Count for automatic embedded columns.');
    }
    if (this.EmbeddedReferenceWidth !== null) MTextColumns.NonNegative(this.EmbeddedReferenceWidth,'EmbeddedReferenceWidth');
    if (this.#direction !== null) {
      MTextColumns.$validateVector(this.#direction);
      if (this.#direction.X === 0 && this.#direction.Y === 0 && this.#direction.Z === 0) throw new ArgumentOutOfRangeException('EmbeddedTextDirection');
    }
    if (this.#insertion !== null) MTextColumns.$validateVector(this.#insertion);
    if (this.Type === 0 && (this.Count !== 1 || this.AutoHeight || this.Heights.Count !== 0 || this.LinkedColumns.Count !== 0))
      throw new InvalidOperationException('No-column MTEXT must have one column and no height list or links.');
    if (this.Type !== 2 && this.AutoHeight) throw new InvalidOperationException('Automatic height is only valid for dynamic columns.');
    const manual = this.Type === 2 && !this.AutoHeight;
    if (manual && this.Heights.Count !== this.Count) throw new InvalidOperationException('Manual dynamic MTEXT requires one height for every column.');
    if (!manual && this.Heights.Count !== 0) throw new InvalidOperationException('Only manual dynamic MTEXT stores individual column heights.');
    for (let i=0;i<this.Heights.Count;i++) {
      MTextColumns.NonNegative(this.Heights.get_Item(i),'Heights');
      if (i<this.Heights.Count-1) MTextColumns.Positive(this.Heights.get_Item(i),'Heights');
    }
    if (manual && this.DefinedHeight !== 0) throw new InvalidOperationException('Manual dynamic columns use individual heights, not DefinedHeight.');
    if (this.Storage !== 1 && this.LinkedColumns.Count !== 0) throw new InvalidOperationException('Only legacy columns support linked MTEXT entities.');
    if (this.Storage === 1 && this.LinkedColumns.Count !== this.Count-1) throw new InvalidOperationException('Legacy MTEXT requires Count minus one links.');
    const unique = new Set();
    for (const linked of this.LinkedColumns) {
      if (linked === null || unique.has(linked) || linked.Columns !== null) throw new InvalidOperationException('Linked columns must be distinct non-null entities without columns.');
      unique.add(linked);
    }
  }
  ResetEmbeddedPlacement() { this.#direction = null; this.#insertion = null; this.EmbeddedReferenceWidth = null; }
  Scale(scale) {
    this.Width = mul(this.Width,scale); this.Gutter = mul(this.Gutter,scale); this.DefinedHeight = mul(this.DefinedHeight,scale); this.TotalHeight = mul(this.TotalHeight,scale);
    for (let i=0;i<this.Heights.Count;i++) this.Heights.set_Item(i,mul(this.Heights.get_Item(i),scale));
  }
  CloneMetadata() {
    const copy = new MTextColumns();
    Object.assign(copy,{Type:this.Type,Storage:this.Storage,Count:this.Count,AutoHeight:this.AutoHeight,FlowReversed:this.FlowReversed,
      Width:this.Width,Gutter:this.Gutter,DefinedHeight:this.DefinedHeight,TotalHeight:this.TotalHeight,
      StoredTotalWidth:this.StoredTotalWidth,EmbeddedTextDirection:this.EmbeddedTextDirection,EmbeddedInsertionPoint:this.EmbeddedInsertionPoint,EmbeddedReferenceWidth:this.EmbeddedReferenceWidth});
    copy.Heights.AddRange(this.Heights); return copy;
  }
  Clone() {
    this.Validate(); if (this.PendingHandles.Count !== 0) throw new InvalidOperationException('Unresolved MTEXT column handles cannot be cloned.');
    const copy = this.CloneMetadata(); for (const linked of this.LinkedColumns) copy.LinkedColumns.Add(linked.Clone()); return copy;
  }
}
export function InstallMTextColumns(Type) {
  Object.defineProperty(Type.prototype,'DefinedHeight',{get() { return this.$definedColumnHeight; },set(value) {
    if (value !== null) MTextColumns.NonNegative(value,'value'); this.$definedColumnHeight = value;
  }});
  Type.prototype.ConvertToEmbeddedColumns = function() {
    if (this.Columns === null) throw new InvalidOperationException('The MTEXT has no column definition.');
    this.Columns.Validate();
    if (this.Columns.Storage === 1 && this.Columns.LinkedColumns.Count !== this.Columns.Count-1) throw new InvalidOperationException('All legacy columns must be resolved.');
    for (const linked of this.Columns.LinkedColumns) if (!this.$hasSameColumnFormatting(linked)) throw new NotSupportedException('Normalize linked entity formatting explicitly before conversion.');
    const result = this.Clone(); let combined = this.Value;
    for (const linked of this.Columns.LinkedColumns) combined = (combined ?? '') + (linked.Value ?? '');
    result.Value = combined; result.Columns.LinkedColumns.Clear(); result.Columns.Storage = 2;
    if (this.Columns.Storage !== 2) result.Columns.ResetEmbeddedPlacement(); return result;
  };
  Type.prototype.ConvertToLinkedColumns = function(columnTexts) {
    if (columnTexts === null) throw new ArgumentNullException('columnTexts');
    if (this.Columns === null) throw new InvalidOperationException('The MTEXT has no column definition.');
    this.Columns.Validate(); const parts = Array.from(columnTexts);
    if (parts.length !== this.Columns.Count || parts.some(v=>v===null)) throw new ArgumentException('Supply one non-null text partition for each column.','columnTexts');
    let source = this.Value; for (const linked of this.Columns.LinkedColumns) source = (source ?? '') + (linked.Value ?? '');
    if (parts.join('') !== source) throw new ArgumentException('Partitions must preserve exact source text.','columnTexts');
    const main = this.Clone(); main.Columns.LinkedColumns.Clear(); main.Columns.Storage = 1; main.Value = parts[0]; main.RectangleWidth = main.Columns.Width;
    main.DefinedHeight = main.Columns.Type === 2 && !main.Columns.AutoHeight ? null : main.Columns.DefinedHeight;
    main.Columns.ResetEmbeddedPlacement(); const result = new ReferenceList([main]);
    const angle = mul(this.Rotation,MathHelper.DegToRad);
    const direction = MathHelper.Transform(new Vector3(M.Cos(angle),M.Sin(angle),0),this.Normal,CoordinateSystem.Object,CoordinateSystem.World);
    let advance = main.Columns.Width+main.Columns.Gutter; if (main.Columns.FlowReversed) advance = -advance;
    for (let i=1;i<parts.length;i++) {
      const column = this.Clone(); column.Columns = null; column.Value = parts[i];
      column.Position = Vector3.Add(this.Position,Vector3.Multiply(direction,mul(advance,i))); column.RectangleWidth = main.Columns.Width;
      column.DefinedHeight = main.DefinedHeight; main.Columns.LinkedColumns.Add(column); result.Add(column);
    }
    return ReadOnlyReferenceList(result);
  };
  Type.prototype.$hasSameColumnFormatting = function(other) {
    const a=this.BackgroundFill,b=other.BackgroundFill;
    const sameBackground = a===null && b===null || a!==null && b!==null && ['Flags','ScaleFactor','ColorIndex','TrueColor','ColorName','Transparency'].every(k=>a[k]===b[k]);
    return sameBackground && this.Style.Name===other.Style.Name && this.Height===other.Height && this.Rotation===other.Rotation && Vector3.Equals(this.Normal,other.Normal) &&
      this.AttachmentPoint===other.AttachmentPoint && this.LineSpacingStyle===other.LineSpacingStyle && this.LineSpacingFactor===other.LineSpacingFactor &&
      this.DrawingDirection===other.DrawingDirection && this.Layer.Name===other.Layer.Name && this.Color.Index===other.Color.Index && this.Color.UseTrueColor===other.Color.UseTrueColor &&
      AciColor.ToTrueColor(this.Color)===AciColor.ToTrueColor(other.Color) && this.Linetype.Name===other.Linetype.Name && this.Lineweight===other.Lineweight &&
      this.LinetypeScale===other.LinetypeScale && this.IsVisible===other.IsVisible && this.Transparency.Value===other.Transparency.Value;
  };
  Type.RelinkClonedColumns = function(copies) {
    for (const [source,target] of copies) {
      if (!(source instanceof Type) || source.Columns===null || source.Columns.LinkedColumns.Count===0) continue;
      target.Columns.LinkedColumns.Clear();
      for (const linked of source.Columns.LinkedColumns) {
        if (!copies.has(linked)) throw new InvalidOperationException('All linked MTEXT columns must belong to the cloned block.');
        target.Columns.LinkedColumns.Add(copies.get(linked));
      }
    }
  };
  Type.prototype.$validateColumnTransform = function(transformation) {
    if (this.Columns===null) return; this.Columns.Validate();
    if (this.Columns.Storage===1 && this.Columns.LinkedColumns.Count!==0) throw new NotSupportedException('Convert linked columns to embedded before transforming.');
    const x=Matrix3.Multiply(transformation,Vector3.UnitX),y=Matrix3.Multiply(transformation,Vector3.UnitY),z=Matrix3.Multiply(transformation,Vector3.UnitZ),scale=x.Modulus();
    if (!Number.isFinite(scale) || scale<=MathHelper.Epsilon || !MathHelper.IsEqual(scale,y.Modulus()) || !MathHelper.IsEqual(scale,z.Modulus()) ||
      !MathHelper.IsZero(Vector3.DotProduct(x,y)) || !MathHelper.IsZero(Vector3.DotProduct(x,z)) || !MathHelper.IsZero(Vector3.DotProduct(y,z)) || Vector3.DotProduct(Vector3.CrossProduct(x,y),z)<0)
      throw new NotSupportedException('Columns require translation, rotation and positive uniform scaling.');
    const trial=this.Columns.CloneMetadata(); trial.Scale(scale); trial.Validate();
  };
}
