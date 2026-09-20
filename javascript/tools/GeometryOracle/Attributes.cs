// Test-only serialization of the unchanged pinned assembly.
using System;
using System.Linq;
using netDxf.Entities;
internal static partial class Program {
  private static bool AttributeWire(object value,out object? result){
    result=null;
    if(value is not AttributeDefinition && value is not netDxf.Entities.Attribute)return false;
    // Reflection selects shared public properties without sharing implementation with JavaScript.
    var type=value.GetType();
    object? P(string key)=>type.GetProperty(key)!.GetValue(value);
    var v=(netDxf.DxfObject)value;
    var common=new {type=type.Name,code=v.CodeName,handle=v.Handle,owner=v.Owner?.CodeName,
      color=Wire(P("Color")),layer=Wire(P("Layer")),linetype=Wire(P("Linetype")),lineweight=(int)(netDxf.Lineweight)P("Lineweight")!,transparency=Wire(P("Transparency")),
      scale=Wire(P("LinetypeScale")),normal=Wire(P("Normal")),visible=(bool)P("IsVisible")!,colorName=Wire(P("ColorName")),shadow=Wire(P("ShadowMode")),proxy=Wire(P("ProxyGraphics")),xdata=v.XData.Values.Select(Wire).ToArray()};
    var text=new {tag=Wire(P("Tag")),value=Wire(P("Value")),style=Wire(P("Style")),position=Wire(P("Position")),flags=(int)(AttributeFlags)P("Flags")!,
      height=Wire(P("Height")),width=Wire(P("Width")),widthFactor=Wire(P("WidthFactor")),oblique=Wire(P("ObliqueAngle")),rotation=Wire(P("Rotation")),
      alignment=(int)(TextAlignment)P("Alignment")!,backward=(bool)P("IsBackward")!,upsideDown=(bool)P("IsUpsideDown")!};
    result=value is AttributeDefinition?new {common,text,prompt=Wire(P("Prompt"))}:(object)new {common,text,definition=Wire(P("Definition"))};return true;
  }
}
