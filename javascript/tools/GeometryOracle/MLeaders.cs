// Independent reflection snapshots. Production C# provides every operation and calculation.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using netDxf;
using netDxf.Entities;
using netDxf.Objects;
internal static partial class Program{
 private static object? MLeaderReference(DxfObject? value)=>value is null?null:new{type=value.GetType().Name,code=value.CodeName,handle=value.Handle};
 private static bool MLeaderWire(object value,out object? result){
  result=null;var type=value.GetType();
  if(type.FullName=="netDxf.Entities.MLeaderField"){
   object? ReadField(string name)=>type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value);
   result=new{type="MLeaderField",code=(short)ReadField("Code")!,fieldType=((Type)ReadField("Type")!).Name,initial=Wire(ReadField("Default")),reference=(bool)ReadField("Reference")!,minimum=(int)(netDxf.Header.DxfVersion)ReadField("MinimumVersion")!};return true;
  }
  if(value is not MLeaderData && value is not MultiLeader && value is not DxfMLeaderStyle && value is not MLeaderBreak && value is not MLeaderLineBreaks)return false;
  var fields=new Dictionary<string,object?>();
  foreach(var property in type.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)){
    var item=property.GetValue(value);fields[property.Name]=item is DxfObject reference?MLeaderReference(reference):Wire(item);
  }
  string? parent=value is MLeaderData?typeof(MLeaderData).GetField("Parent",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)?.GetType().Name:null;
  object? common=value is DxfObject obj?new{code=obj.CodeName,handle=obj.Handle,owner=MLeaderReference(obj.Owner),xdata=obj.XData.Values.Select(Wire).ToArray()}:null;
  result=new{type=type.Name,fields,parent,common};return true;
 }
}
