import * as api from '../index.js';
const activeColumns = new WeakSet();
export function mtextValueWire(value,wire) {
  if (value instanceof api.MTextColumns) {
    if (activeColumns.has(value)) return {type:'MTextColumns',cycle:true};
    activeColumns.add(value);
    try { return {type:'MTextColumns',kind:value.Type,storage:value.Storage,count:value.Count,auto:value.AutoHeight,reversed:value.FlowReversed,
      width:wire(value.Width),gutter:wire(value.Gutter),defined:wire(value.DefinedHeight),totalWidth:wire(value.TotalWidth),totalHeight:wire(value.TotalHeight),
      storedWidth:wire(value.StoredTotalWidth),direction:wire(value.EmbeddedTextDirection),insertion:wire(value.EmbeddedInsertionPoint),referenceWidth:wire(value.EmbeddedReferenceWidth),
      heights:Array.from(value.Heights,wire),links:Array.from(value.LinkedColumns,wire)};
    } finally { activeColumns.delete(value); }
  }
  if (value instanceof api.MTextBackgroundFill) return {type:'MTextBackgroundFill',flags:value.Flags,scale:wire(value.ScaleFactor),aci:wire(value.ColorIndex),
    rgb:wire(value.TrueColor),name:value.ColorName,transparency:wire(value.Transparency)};
  if (value instanceof api.MTextFormattingOptions) return {type:'MTextFormattingOptions',bold:value.Bold,italic:value.Italic,overline:value.Overline,underline:value.Underline,
    strike:value.StrikeThrough,superscript:value.Superscript,subscript:value.Subscript,color:wire(value.Color),font:value.FontName,height:wire(value.HeightFactor),
    scriptHeight:wire(value.SuperSubScriptHeightFactor),oblique:wire(value.ObliqueAngle),spacing:wire(value.CharacterSpaceFactor),width:wire(value.WidthFactor)};
  if (value instanceof api.MTextParagraphOptions) return {type:'MTextParagraphOptions',height:wire(value.HeightFactor),alignment:value.Alignment,vertical:value.VerticalAlignment,
    before:wire(value.SpacingBefore),after:wire(value.SpacingAfter),first:wire(value.FirstLineIndent),left:wire(value.LeftIndent),right:wire(value.RightIndent),
    spacing:wire(value.LineSpacingFactor),style:value.LineSpacingStyle};
  return undefined;
}
/** Preserve isolated UTF-16 code units that JSON's .NET string decoder rejects/replaces. */
export function utf16Wire(value) {
  for (let i=0;i<value.length;i++) {
    const c=value.charCodeAt(i);
    if (c>=0xd800 && c<=0xdbff) { const next=value.charCodeAt(i+1); if (next>=0xdc00 && next<=0xdfff) { i++;continue; } }
    else if (c<0xdc00 || c>0xdfff) continue;
    return {utf16:value.split('').map(c=>c.charCodeAt(0))};
  }
  return value;
}
