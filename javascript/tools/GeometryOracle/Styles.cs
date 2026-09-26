// Test-only adapters: every model operation is executed by the pinned production assembly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Tables;
internal static partial class Program
{
    private static object TableWire(TableObject table) => new { type=table.GetType().Name,name=table.Name,code=table.CodeName,
        reserved=table.IsReserved,xdata=table.XData.Values.Select(data=>new {name=data.ApplicationRegistry.Name,records=data.XDataRecord.Select(Wire).ToArray()}).ToArray() };
    private static bool StyleWire(object value, out object? result)
    {
        result=null;
        if(value is TextStyleFontData font)result=new {type="TextStyleFontData",family=font.FamilyName,flags=font.Flags,style=(int)font.FontStyle};
        else if(value is TextStyle text)result=new {table=TableWire(text),file=text.FontFile,bigFont=text.BigFont,family=text.FontFamilyName,fontStyle=(int)text.FontStyle,
            fontData=Wire(text.ExtendedFontData),height=Wire(text.Height),width=Wire(text.WidthFactor),oblique=Wire(text.ObliqueAngle),flags=(int)text.Flags,
            generation=text.TextGenerationFlags,lastHeight=Wire(text.LastHeight),vertical=text.IsVertical,backward=text.IsBackward,upsideDown=text.IsUpsideDown};
        else if(value is ShapeStyle shape)result=new {table=TableWire(shape),file=shape.File,size=Wire(shape.Size),width=Wire(shape.WidthFactor),oblique=Wire(shape.ObliqueAngle),flags=(int)shape.Flags,
            generation=shape.TextGenerationFlags,lastHeight=Wire(shape.LastHeight)};
        else if(value is Linetype line)result=new {table=TableWire(line),description=line.Description,byLayer=line.IsByLayer,byBlock=line.IsByBlock,length=Wire(line.Length()),segments=line.Segments.Select(Wire).ToArray()};
        else if(value is Layer layer)result=new {table=TableWire(layer),description=layer.Description,color=Wire(layer.Color),linetype=Wire(layer.Linetype),
            lineweight=(int)layer.Lineweight,transparency=Wire(layer.Transparency),assigned=(bool)typeof(Layer).GetProperty("HasTransparencyAssignment",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(layer)!,visible=layer.IsVisible,frozen=layer.IsFrozen,locked=layer.IsLocked,plot=layer.Plot};
        else if(value is LinetypeSegment segment) {
            var common=new {type=value.GetType().Name,kind=(int)segment.Type,length=Wire(segment.Length)};
            if(value is LinetypeTextSegment label)result=new {common,text=label.Text,style=Wire(label.Style),offset=Wire(label.Offset),rotationType=(int)label.RotationType,rotation=Wire(label.Rotation),scale=Wire(label.Scale)};
            else if(value is LinetypeShapeSegment item)result=new {common,name=item.Name,style=Wire(item.Style),offset=Wire(item.Offset),rotationType=(int)item.RotationType,rotation=Wire(item.Rotation),scale=Wire(item.Scale)};
            else result=new {common};
        }
        else if(value is netDxf.Collections.XDataDictionary dictionary)result=new {type="XDataDictionary",entries=dictionary.Values.Select(Wire).ToArray()};
        else if(value is XData data)result=new {type="XData",name=data.ApplicationRegistry.Name,records=data.XDataRecord.Select(Wire).ToArray()};
        else if(value is TableObject table)result=TableWire(table);
        else return false;
        return true;
    }
    private static object? StyleFileStep(JsonElement step,object? target)
    {
        string kind=step.GetProperty("kind").GetString()!,file=Path.Combine(Path.GetTempPath(),"netdxf-style-oracle-"+Guid.NewGuid().ToString("N")+(kind.StartsWith("shape-")?".shx":".lin"));
        try {
            if(kind=="lin-save") { ((Linetype)target!).Save(file);return File.ReadAllText(file); }
            if(kind.StartsWith("shape-")) {
                File.WriteAllBytes(file,Convert.FromBase64String(step.GetProperty("bytes").GetString()!));
                if(kind=="shape-names")return ShapeStyle.NamesFromFile(file);
                var style=new ShapeStyle("S",file); string member=step.GetProperty("member").GetString()!;
                if(member=="ShapeNumber")return style.ShapeNumber(step.GetProperty("name").GetString()!);
                if(member=="ContainsShapeName")return style.ContainsShapeName(step.GetProperty("name").GetString()!);
                if(member=="StaticContainsShapeName")return ShapeStyle.ContainsShapeName(file,step.GetProperty("name").GetString()!);
                if(member=="ShapeName")return style.ShapeName(step.GetProperty("number").GetInt16());
                return style.NamesFromShapeStyle();
            }
            File.WriteAllText(file,step.GetProperty("text").GetString()!);
            if(kind=="lin-names")return Linetype.NamesFromFile(file);
            return Linetype.Load(file,step.GetProperty("patternName").GetString()!);
        }finally { if(File.Exists(file))File.Delete(file); }
    }
    private static readonly List<object> Observations=new();
    private static readonly Dictionary<string,(object target,EventInfo info,Delegate handler)> Observers=new();
    private static void ResetObservations(){Observers.Clear();Observations.Clear();}
    private static object? ObservationStep(JsonElement step,object? target)
    {
        string kind=step.GetProperty("kind").GetString()!;
        if(kind=="events")return Observations.Select(v=>JsonSerializer.Serialize(v,Json)).ToArray();
        string id=step.GetProperty("observer").GetString()!;
        if(kind=="unobserve") { var old=Observers[id];old.info.RemoveEventHandler(old.target,old.handler);Observers.Remove(id);return null; }
        string member=step.GetProperty("member").GetString()!;
        var info=target!.GetType().GetEvent(member)!;
        var parameters=info.EventHandlerType!.GetMethod("Invoke")!.GetParameters().Select(p=>Expression.Parameter(p.ParameterType)).ToArray();
        Action<object,object> action=(sender,args)=>{
            var type=args.GetType();
            var properties=new Dictionary<string,object?>();
            foreach(string name in new[]{"OldValue","NewValue","Item","Cancel"}) {
                var p=type.GetProperty(name);if(p is not null)properties[name]=Wire(p.GetValue(args));
            }
            Observations.Add(new {observer=id,member,values=properties});
            if(step.TryGetProperty("replace",out var replacement))type.GetProperty("NewValue")!.SetValue(args,Read(replacement));
            if(step.TryGetProperty("cancel",out var cancel))type.GetProperty("Cancel")!.SetValue(args,cancel.GetBoolean());
            if(step.TryGetProperty("throw",out var fail)&&fail.GetBoolean())throw new InvalidOperationException("Observer failure");
        };
        var body=Expression.Invoke(Expression.Constant(action),Expression.Convert(parameters[0],typeof(object)),Expression.Convert(parameters[1],typeof(object)));
        var handler=Expression.Lambda(info.EventHandlerType,body,parameters).Compile();
        info.AddEventHandler(target,handler);Observers.Add(id,(target,info,handler));return null;
    }
}
