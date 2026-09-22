// Observation-only harness for the unchanged C# document and collections.
using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Tables;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Blocks;
using netDxf.IO;
internal static partial class Program
{
    private static object? OwnerRef(DxfObject? item) => item == null ? null : new {
        type=item.GetType().Name, code=item.CodeName, handle=item.Handle,
        name=(item as TableObject)?.Name, owner=item.Owner?.Handle, ownerCode=item.Owner?.CodeName
    };
    private static readonly string[] OwnerTableNames={"VPorts","Views","ApplicationRegistries","Layers","Linetypes","TextStyles","ShapeStyles","DimensionStyles","MlineStyles","UCSs","Blocks","ImageDefinitions","UnderlayDgnDefinitions","UnderlayDwfDefinitions","UnderlayPdfDefinitions","Groups","Layouts"};
    private static object OwnershipDocument(DxfDocument doc) => new {
        handle=doc.Handle, seed=doc.DrawingVariables.HandleSeed, version=(int)doc.DrawingVariables.AcadVer, name=doc.Name,
        active=doc.Entities.ActiveLayout, dimensionBlocks=doc.BuildDimensionBlocks,
        thumbnail=Convert.ToBase64String(doc.ThumbnailImage), classes=doc.Classes.Count,
        registry= ((IEnumerable)typeof(DxfDocument).GetField("AddedObjects",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(doc)!)
            .Cast<object>().Select(pair=>new {key=pair.GetType().GetProperty("Key")!.GetValue(pair),value=OwnerRef((DxfObject)pair.GetType().GetProperty("Value")!.GetValue(pair)!)}).ToArray(),
        tables=OwnerTableNames.Select(name=>{
            var table=typeof(DxfDocument).GetProperty(name)!.GetValue(doc)!;
            return new {name,table=OwnerRef((DxfObject)table),items=((IEnumerable)table).Cast<DxfObject>().Select(OwnerRef).ToArray()};
        }).ToArray(),
        layouts=doc.Layouts.Select(layout=>new {name=layout.Name,block=OwnerRef(layout.AssociatedBlock),viewport=OwnerRef(layout.Viewport),
            entities=layout.AssociatedBlock.Entities.Select(OwnerRef).ToArray(),attributes=layout.AssociatedBlock.AttributeDefinitions.Values.Select(OwnerRef).ToArray()}).ToArray()
    };
    private static object OwnershipLayerState(LayerState state) => new {
        common=OwnerRef(state),description=state.Description,current=state.CurrentLayer,paper=state.PaperSpace,
        properties=state.Properties.Select(p=>new {key=p.Key, value=OwnershipLayerProperties(p.Value)}).ToArray()
    };
    private static object OwnershipLayerProperties(LayerStateProperties p) => new {
        name=p.Name, flags=(int)p.Flags, linetype=p.LinetypeName, color=Wire(p.Color), lineweight=(int)p.Lineweight, transparency=Wire(p.Transparency)
    };
    private static object? OwnershipModel(object? value) {
        if(value is LayerState state)return OwnershipLayerState(state);
        if(value is LayerStateProperties properties)return OwnershipLayerProperties(properties);
        if(value is DxfSortOrderEntry order)return new {entity=OwnerRef(order.Entity),handle=order.SortHandle};
        return Wire(value);
    }
    private static object OwnershipLas(LayerState state) {
        string path=Path.Combine(Path.GetTempPath(),"netdxf-layer-state-"+Guid.NewGuid().ToString("N")+".las");
        try { bool saved=state.Save(path); return new {saved,text=File.ReadAllText(path),loaded=OwnershipLayerState(LayerState.Load(path))}; }
        finally {if(File.Exists(path))File.Delete(path);}
    }
    private static object? OwnershipWire(object? value) {
        if(value==null)return null;
        if(value is LayerState state)return OwnershipLayerState(state);
        if(value is LayerStateProperties properties)return OwnershipLayerProperties(properties);
        if(value is DxfSortOrderEntry order)return new {entity=OwnerRef(order.Entity),handle=order.SortHandle};
        if(value is DxfDocument doc)return OwnershipDocument(doc);
        if(value is DxfObject item)return OwnerRef(item);
        if(value is DxfObjectReference reference)return new {reference=OwnerRef(reference.Reference),uses=reference.Uses};
        if(value is XData data)return new {registry=OwnerRef(data.ApplicationRegistry),records=data.XDataRecord.Count};
        if(value is string || value is bool)return value;
        if(value is double number)return new {@double=Bits(number)};
        if(value is long wide)return new {@long=wide.ToString(System.Globalization.CultureInfo.InvariantCulture)};
        if(value is Enum)return Convert.ToInt32(value);
        if(value is int || value is short || value is byte)return value;
        if(value is IDictionary dict)return dict.Keys.Cast<object>().Select(key=>new {key=OwnershipWire(key),value=OwnershipWire(dict[key])}).ToArray();
        if(value is IEnumerable list)return list.Cast<object?>().Select(OwnershipWire).ToArray();
        if(value is IEnumerator)return new {iterator=true};
        return new {type=value.GetType().Name};
    }
    private static PropertyInfo OwnershipProperty(object target,string name) {
        for(Type? type=target.GetType();type!=null;type=type.BaseType) {
            var property=type.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly);
            if(property!=null)return property;
        }
        throw new MissingMemberException(target.GetType().Name,name);
    }
    private static object DocumentOwnershipRequest(JsonElement input) {
        var results=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()) {
            object? result=null;string? error=null,param=null;
            try {
                string method=step.GetProperty("method").GetString()!;
                object? target=step.TryGetProperty("target",out var targetId)?Values[targetId.GetString()!]:null;
                string member=step.TryGetProperty("member",out var key)?key.GetString()!:"";
                switch(method){
                    case "opaque": {
                        var args=Arguments(step);
                        result=Activator.CreateInstance(typeof(DxfOpaqueObject),BindingFlags.Instance|BindingFlags.NonPublic,null,
                            new object[]{(string)args[0]!,((IEnumerable<DxfTag>)args[1]!).ToList()},System.Globalization.CultureInfo.InvariantCulture);break;
                    }
                    case "append-loaded":typeof(DxfXRecord).GetMethod("AddLoadedData",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,Arguments(step));break;
                    case "mapping": {
                        var mapping=new Dictionary<DxfObject,DxfObject>();
                        foreach(var pair in step.GetProperty("pairs").EnumerateArray())mapping.Add((DxfObject)Read(pair[0])!,(DxfObject)Read(pair[1])!);
                        result=mapping;break;
                    }
                    case "model":result=OwnershipModel(target);break;
                    case "las":result=OwnershipLas((LayerState)target!);break;
                    case "new":result=Read(step.GetProperty("value"));break;
                    case "get":result=OwnershipProperty(target!,member).GetValue(target);break;
                    case "set":OwnershipProperty(target!,member).SetValue(target,Read(step.GetProperty("value")));break;
                    case "item":result=OwnershipProperty(target!,"Item").GetValue(target,Arguments(step));break;
                    case "call":{
                        var args=Arguments(step);var signature=Signature(step);var type=target!.GetType();
                        var invoke=signature==null?type.GetMethods().Where(m=>m.Name==member).Single(m=>Matches(m.GetParameters(),args)):type.GetMethod(member,signature)!;
                        result=invoke.Invoke(target,args);break;
                    }
                    case "snapshot":result=target;break;
                    case "same":var compared=Arguments(step);result=ReferenceEquals(compared[0],compared[1]);break;
                    default:throw new ArgumentException("Unknown ownership method "+method);
                }
                if(step.TryGetProperty("id",out var id))Values[id.GetString()!]=result;
                if(method!="model"&&method!="las")result=OwnershipWire(result);
            }catch(Exception e){while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;error=e.GetType().Name;param=(e as ArgumentException)?.ParamName;}
            results.Add(new {result,error,param});
        }
        return results;
    }
}
