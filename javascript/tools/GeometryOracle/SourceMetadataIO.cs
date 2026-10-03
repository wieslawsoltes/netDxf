// Calls the unchanged native common-header observer and source-identity context.
using System;using System.Collections;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Reflection;using System.Text;using System.Text.Json;
using netDxf;using netDxf.Entities;using netDxf.Objects;
internal static partial class Program {
    private static object SourceMetadataRequest(JsonElement input){
        var document=new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018);var line=new Line();document.Entities.Add(line);
        var resources=new Dictionary<string,DxfObject>{{"line",line},{"root",document.NamedObjects},{"style",document.DimensionStyles["Standard"]}};
        var assembly=typeof(DxfDocument).Assembly;var type=assembly.GetType("netDxf.IO.DxfReader")!;var context=Activator.CreateInstance(type,true)!;TSSet(context,"doc",document);
        object? Value(JsonElement value){
            if(value.ValueKind==JsonValueKind.Object&&value.TryGetProperty("h",out var h)){
                string handle=resources[h.GetString()!].Handle;if(value.TryGetProperty("lower",out var lower)&&lower.GetBoolean())handle=handle.ToLowerInvariant();return new string('0',value.TryGetProperty("pad",out var pad)?pad.GetInt32():0)+handle;
            }
            return TSInput(value);
        }
        bool binary=input.GetProperty("mode").GetString()!="text",legacy=input.GetProperty("mode").GetString()=="legacy";
        using var bytes=new MemoryStream();using var text=new StringWriter(CultureInfo.InvariantCulture);text.NewLine="\n";
        var wt=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueWriter")!;
        object writer=binary?Activator.CreateInstance(wt,new object[]{new BinaryWriter(bytes,Encoding.UTF8,true),legacy})!:Activator.CreateInstance(wt,text)!;
        foreach(var pair in input.GetProperty("tags").EnumerateArray())TSCall(writer,"Write",pair[0].GetInt16(),Value(pair[1]));TSCall(writer,"Flush");bytes.Position=0;
        var rt=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueReader")!;
        var inner=binary?Activator.CreateInstance(rt,new object[]{new BinaryReader(bytes,Encoding.UTF8,true),Encoding.UTF8,legacy})!:Activator.CreateInstance(rt,new StringReader(text.ToString()))!;
        var records=(IDictionary)TSGet(context,"entityDatabaseMetadata")!;var ids=(HashSet<ulong>)TSGet(context,"sourceObjectIdentities")!;
        var metadataType=type.GetNestedType("DatabaseMetadataReader",BindingFlags.NonPublic)!;
        var reader=Activator.CreateInstance(metadataType,TSFlags,null,new object[]{inner,records,ids},null)!;TSSet(context,"chunk",reader);
        object Identity(object s)=>new{handle=((ulong)TSGet(s,"Handle")!).ToString(CultureInfo.InvariantCulture),seen=(bool)TSGet(s,"IdentitySeen")!,ambiguous=(bool)TSGet(s,"Ambiguous")!};
        var results=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()){
            object? result=null,error=null;
            try{
                switch(step.GetProperty("method").GetString()){
                    case "next":TSCall(reader,"Next");break;
                    case "skip":TSCall(reader,"SetSkipComments",step.GetProperty("value").GetBoolean());break;
                    case "accept":TSCall(context,"RecordSourceObject",resources[step.GetProperty("target").GetString()!],TSGet(context,"CurrentSourceRecord"));break;
                    case "validate":TSCall(context,"ValidateSourceIdentityDeclarations");break;
                    case "lookup":result=PayloadRef((DxfObject?)TSCall(context,"GetObjectBySourceHandle",Value(step.GetProperty("handle")),step.TryGetProperty("includeMetadata",out var im)&&im.GetBoolean()));break;
                    case "dictionary":result=TSCall(context,"IsAcceptedSourceDictionary",Value(step.GetProperty("handle")));break;
                    case "replace":((IDictionary<string,DxfObject>)TSGet(document,"AddedObjects")!)[resources[step.GetProperty("target").GetString()!].Handle]=new DxfPlaceholder();break;
                    case "cast":result=Wire(TSCall(reader,step.GetProperty("member").GetString()!));break;
                }
            }catch(Exception e){error=PayloadError(e);}
            var accepted=(IDictionary)TSGet(context,"acceptedSourceRecords")!;
            results.Add(new{ok=true,value=new{result,error,code=(short)TSGet(reader,"Code")!,position=(long)TSGet(reader,"CurrentPosition")!,value=Wire(TSGet(reader,"Value")),current=Identity(TSGet(reader,"SourceRecord")!),
                declared=ids.Select(n=>n.ToString(CultureInfo.InvariantCulture)).ToArray(),accepted=accepted.Keys.Cast<ulong>().Select(k=>new{key=k.ToString(CultureInfo.InvariantCulture),handle=((ulong)TSGet(accepted[k]!,"Handle")!).ToString(CultureInfo.InvariantCulture),seen=(bool)TSGet(accepted[k]!,"IdentitySeen")!,ambiguous=(bool)TSGet(accepted[k]!,"Ambiguous")!}).ToArray(),
                metadata=records.Keys.Cast<string>().Select(k=>new{key=k,owner=TSGet(records[k]!,"Owner"),extension=TSGet(records[k]!,"Extension"),reactors=((IEnumerable)TSGet(records[k]!,"Reactors")!).Cast<string>().ToArray()}).ToArray()}});
        }
        return results;
    }
}
