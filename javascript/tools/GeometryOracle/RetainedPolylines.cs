// Test-only construction of retained metadata through the unchanged source's
// internal constructors. Independent observation, not expected output or a reader.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Header;
using netDxf.IO;
internal static partial class Program
{
    private const BindingFlags RetainedFlags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    private static object? RetainedGet(object target,string name) {
        for(Type? type=target.GetType();type!=null;type=type.BaseType) {
            var f=type.GetField(name,RetainedFlags|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(target);
            var p=type.GetProperty(name,RetainedFlags|BindingFlags.DeclaredOnly);if(p!=null)return p.GetValue(target);
        }
        throw new MissingMemberException(target.GetType().Name,name);
    }
    private static void RetainedPut(object target,string name,object? value) {
        for(Type? type=target.GetType();type!=null;type=type.BaseType) {
            var f=type.GetField(name,RetainedFlags|BindingFlags.DeclaredOnly);if(f!=null){f.SetValue(target,value);return;}
            var p=type.GetProperty(name,RetainedFlags|BindingFlags.DeclaredOnly);if(p!=null){p.SetValue(target,value);return;}
        }
        throw new MissingMemberException(target.GetType().Name,name);
    }
    private static object CreateRetainedParent(JsonElement options) {
        string family=options.GetProperty("family").GetString()!;
        int count=options.TryGetProperty("count",out var c)?c.GetInt32():4;
        var points=Enumerable.Range(0,count).Select(i=>new Vector3(i,i%2,family=="Polyline2D"?0:i/4.0)).ToArray();
        EntityObject parent=family switch {
            "Polyline3D"=>new Polyline3D(points),"PolygonMesh"=>new PolygonMesh(2,(short)(count/2),points),
            "PolyfaceMesh"=>new PolyfaceMesh(points,new[]{new short[]{1,2,3,4}}),
            "Polyline2D"=>new Polyline2D(points.Select(p=>new Vector2(p.X,p.Y))),_=>throw new Exception("Unknown retained family.")
        };
        Type recordType=Resolve("Entities."+family+"Record");
        var layer=new Layer(options.TryGetProperty("layer",out var l)?l.GetString()!:"Stored");
        var linetype=new Linetype(options.TryGetProperty("linetype",out var lt)?lt.GetString()!:"StoredLine");
        var records=new List<DxfObject>();
        DxfObject Make(int index,bool end=false,bool face=false) {
            var position=points[Math.Min(index,count-1)];string handle=(0xc0+index).ToString("X");
            var tags=new List<DxfTag>{new DxfTag(5,handle),new DxfTag(330,"A0"),new DxfTag(100,"AcDbEntity"),
                new DxfTag(8,layer.Name),new DxfTag(6,linetype.Name),new DxfTag(100,end?"AcDbSequenceEnd":"AcDbVertex")};
            if(!end)tags.AddRange(new[]{new DxfTag(10,position.X),new DxfTag(20,position.Y),new DxfTag(30,position.Z),new DxfTag(70,(short)32)});
            var record=(DxfObject)Activator.CreateInstance(recordType,RetainedFlags,null,new object[]{end?"SEQEND":"VERTEX",tags},null)!;
            RetainedPut(record,"SourceOwner","A0");RetainedPut(record,"SourceVersion",(DxfVersion)(options.TryGetProperty("version",out var v)?v.GetInt32():18));
            RetainedPut(record,"CommonEnd",2);RetainedPut(record,"IdentityIndex",0);RetainedPut(record,"OwnerIndex",1);RetainedPut(record,"Position",position);
            RetainedPut(record,"UsesBlockRecordOwner",options.TryGetProperty("blockOwner",out var bo)&&bo.GetBoolean());
            if(options.TryGetProperty("preserveHandles",out var ph)&&ph.GetBoolean())RetainedPut(record,"Handle",handle);
            var resources=(IDictionary)RetainedGet(record,"Resources")!;resources.Add(3,layer);resources.Add(4,linetype);
            var names=(IDictionary)RetainedGet(record,"OriginalResourceNames")!;names.Add(3,layer.Name);names.Add(4,linetype.Name);
            var coordinates=(IDictionary)RetainedGet(record,"Coordinates")!;
            if(!end){coordinates.Add((short)10,6);coordinates.Add((short)20,7);coordinates.Add((short)30,8);}
            if(family=="Polyline2D"&&!end)RetainedPut(record,"StoredVertex",((Polyline2D)parent).Vertexes[index]);
            if(family=="PolyfaceMesh") {
                RetainedPut(record,"CoordinateIndex",end||face?-1:index);
                if(face){var f=((PolyfaceMesh)parent).Faces[0];RetainedPut(record,"IsFaceRecord",true);RetainedPut(record,"FaceIndex",0);RetainedPut(record,"StoredFace",f);
                    RetainedPut(record,"OriginalIndexes",f.VertexIndexes.ToArray());RetainedPut(record,"FaceLayerIndex",3);
                    if(!options.TryGetProperty("faceLayer",out var fl)||fl.ValueKind!=JsonValueKind.Null)f.Layer=new Layer(fl.ValueKind==JsonValueKind.String?fl.GetString()!:"FaceLayer");}
            }
            records.Add(record);return record;
        }
        for(int index=0;index<count;index++)Make(index);
        if(family=="PolyfaceMesh")Make(count,false,true);
        var end=Make(records.Count,true);
        object array=Array.CreateInstance(recordType,count);for(int i=0;i<count;i++)((Array)array).SetValue(records[i],i);
        if(family=="Polyline3D") {var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;foreach(var item in (Array)array)list.Add(item);array=list;}
        object?[] args;
        if(family=="PolyfaceMesh"){var sequence=Array.CreateInstance(recordType,records.Count);for(int i=0;i<records.Count;i++)sequence.SetValue(records[i],i);args=new object?[]{null,sequence};}
        else args=new object?[]{null,array,end};
        parent.GetType().GetMethod("SetStoredRecords",RetainedFlags)!.Invoke(parent,args);
        if(family=="Polyline2D"||family=="PolyfaceMesh") {
            RetainedPut(parent,"StoredHeaderTags",new List<DxfTag>{new DxfTag(100,family=="Polyline2D"?"AcDb2dPolyline":"AcDbPolyFaceMesh")});
            RetainedPut(parent,"StoredHeaderPublicEnd",1);
        }
        return parent;
    }
    private static object? RetainedRef(object? value) => value is not DxfObject item?null:new {type=item.GetType().Name,handle=item.Handle,owner=item.Owner?.Handle,code=item.CodeName};
    private static string[] RetainedPoint(object value) {var p=value is Polyline2DVertex v?new Vector3(v.Position.X,v.Position.Y,0):(Vector3)value;return new[]{Bits(p.X),Bits(p.Y),Bits(p.Z)};}
    private static object RetainedRecordState(object record) {
        var item=(DxfObject)record;var type=record.GetType();
        object[] Map(string name)=>((IDictionary)RetainedGet(record,name)!).Keys.Cast<object>().Select(key=>(object)new{key,value=name=="Resources"?RetainedRef(((IDictionary)RetainedGet(record,name)!)[key]):((IDictionary)RetainedGet(record,name)!)[key]}).ToArray();
        return new {common=RetainedRef(record),sourceVersion=(int)(DxfVersion)RetainedGet(record,"SourceVersion")!,storedOwner=RetainedRef(RetainedGet(record,"StoredOwner")),sourceDocument=RetainedRef(RetainedGet(record,"SourceDocument")),
            layer=RetainedRef(RetainedGet(record,"Layer")),linetype=RetainedRef(RetainedGet(record,"Linetype")),sequenceEnd=RetainedGet(record,"IsSequenceEnd"),blockOwner=RetainedGet(record,"UsesBlockRecordOwner"),
            authored=record is Polyline3DRecord?RetainedGet(record,"IsAuthored"):null,removed=record is Polyline3DRecord?RetainedGet(record,"IsRemoved"):null,
            privateData=RetainedGet(record,"HasPrivateData"),position=RetainedPoint(RetainedGet(record,"Position")!),tagCount=type.GetMethod("TopologyTagCount",RetainedFlags)!.Invoke(record,null),
            tags=((IEnumerable<DxfTag>)RetainedGet(record,"Tags")!).Select(t=>new{code=t.Code,type=(int)t.ValueType,value=t.Value is double d?(object)Bits(d):t.Value}).ToArray(),
            resources=Map("Resources"),coordinates=Map("Coordinates"),references=((IEnumerable)RetainedGet(record,"References")!).Cast<object?>().Select(RetainedRef).ToArray(),
            xdata=item.XData.Values.Select(data=>new{registry=RetainedRef(data.ApplicationRegistry),count=data.XDataRecord.Count}).ToArray()};
    }
    private static object RetainedSnapshot(object target) {
        if(target is Polyline3DRecord||target is PolygonMeshRecord||target is PolyfaceMeshRecord||target is Polyline2DRecord)return RetainedRecordState(target);
        return new{common=RetainedRef(target),points=((IEnumerable)RetainedGet(target,"Vertexes")!).Cast<object>().Select(RetainedPoint).ToArray(),records=((IEnumerable)RetainedGet(target,"StoredRecords")!).Cast<object>().Select(RetainedRecordState).ToArray()};
    }
}
