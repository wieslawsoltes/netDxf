// Observation-only adapter. All model operations are the unchanged production C# APIs.
using System;
using System.Linq;
using System.Reflection;
using netDxf;
using netDxf.Objects;
internal static partial class Program
{
    private static object? ObjectReferenceWire(DxfObject? value) => value is null?null:new {type=value.GetType().Name,code=value.CodeName,handle=value.Handle};
    private static object[] DatabaseList(DxfDatabaseObject value,string name) => ((System.Collections.Generic.IEnumerable<DxfObject>)typeof(DxfDatabaseObject).GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)!).Select(v=>ObjectReferenceWire(v)!).ToArray();
    private static bool DatabaseModelWire(object value,out object? result)
    {
        result=null;
        if(value is DxfLayerIndexEntry layerEntry) {result=new {type="DxfLayerIndexEntry",name=Wire(layerEntry.LayerName),buffer=ObjectReferenceWire(layerEntry.Buffer),count=layerEntry.Count};return true;}
        if(value is DxfDictionaryEntry entry) {result=new {type="DxfDictionaryEntry",name=entry.Name,target=ObjectReferenceWire(entry.Target),hard=entry.IsHardOwner};return true;}
        if(value is DxfDataColumn column) {result=new {type="DxfDataColumn",kind=(int)column.Type,name=Wire(column.Name),values=column.Values.Select(v=>v is DxfObject obj?new {reference=ObjectReferenceWire(obj)}:Wire(v)).ToArray()};return true;}
        if(value is not DxfDatabaseObject model)return false;
        var common=new {type=value.GetType().Name,code=model.CodeName,handle=model.Handle,owner=ObjectReferenceWire(model.Owner),extension=ObjectReferenceWire(model.ExtensionDictionary),erased=model.IsErased,registered=model.Database is not null,
            xdata=model.XData.Values.Select(Wire).ToArray(),reactors=model.PersistentReactors.Select(ObjectReferenceWire).ToArray(),owned=DatabaseList(model,"DeclaredOwnedObjects"),references=DatabaseList(model,"DatabaseReferences")};
        if(model is DxfIdBuffer) result=new {common};
        else if(model is DxfSpatialIndex spatialIndex) result=new {common,timestamp=Wire(spatialIndex.Timestamp)};
        else if(model is DxfLayerIndex layerIndex) result=new {common,timestamp=Wire(layerIndex.Timestamp),entries=layerIndex.Entries.Select(Wire).ToArray()};
        else if(model is DxfLayerFilter layerFilter) result=new {common,names=layerFilter.LayerNames.Select(Wire).ToArray()};
        else if(model is DxfSpatialFilter filter) result=new {common,boundary=filter.Boundary.Select(v=>Wire(v)).ToArray(),normal=Wire(filter.Normal),origin=Wire(filter.Origin),enabled=filter.IsClippingEnabled,front=Wire(filter.FrontClippingDistance),back=Wire(filter.BackClippingDistance),inverse=Wire(filter.InverseInsertTransform),transform=Wire(filter.ClipBoundaryTransform)};
        else if(model is DxfDictionary dictionary) result=new {common,hard=dictionary.IsHardOwner,cloning=(int)dictionary.Cloning,entries=dictionary.Entries.Select(Wire).ToArray(),fallback=dictionary is DxfDictionaryWithDefault fallback?ObjectReferenceWire(fallback.Default):null};
        else if(model is DxfXRecord record) result=new {common,cloning=(int)record.Cloning,managed=record.IsSchemaManaged,
            table=(bool)typeof(DxfXRecord).GetProperty("IsTableRoundtripRecord",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(record)!,
            composite=(bool)typeof(DxfXRecord).GetProperty("IsCompositeTableRoundtripRecord",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(record)!,data=record.Data.Select(Wire).ToArray()};
        else if(model is DxfDictionaryVariable variable) result=new {common,schema=variable.Schema,value=Wire(variable.Value)};
        else if(model is DxfOpaqueObject opaque) result=new {common,tags=opaque.Tags.Select(Wire).ToArray()};
        else if(model is DxfDataTable table) result=new {common,name=Wire(table.Name),rows=table.RowCount,version=table.StoredVersion,columns=table.Columns.Select(Wire).ToArray()};
        else if(model is DxfSun sun) result=new {common,version=sun.StoredVersion,enabled=sun.Enabled,color=Wire(sun.ColorIndex),rgb=Wire(sun.TrueColor),intensity=Wire(sun.Intensity),shadows=sun.ShadowsEnabled,day=sun.JulianDay,time=sun.StoredTime,daylight=sun.DaylightSavingTime,shadow=(int)sun.ShadowType,map=sun.ShadowMapSize,softness=sun.ShadowSoftness};
        else result=new {common};
        return true;
    }
}
