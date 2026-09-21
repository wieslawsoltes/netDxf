// Reflection snapshots independently observe the original production assembly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using netDxf.Tables;
using netDxf.Collections;
internal static partial class Program {
  private static bool DimensionWire(object value,out object? result){
    result=null;
    if(MLeaderWire(value,out result))return true;
    if(value is DimensionStyleOverride o){result=new{type="DimensionStyleOverride",kind=(int)o.Type,valueType=o.Value?.GetType().Name,value=Wire(o.Value)};return true;}
    if(value is DimensionStyleOverrideChangeEventArgs a){result=new{type="DimensionStyleOverrideChangeEventArgs",item=Wire(a.Item)};return true;}
    if(value is DimensionStyleOverrideDictionaryEventArgs e){result=new{type="DimensionStyleOverrideDictionaryEventArgs",item=Wire(e.Item),cancel=e.Cancel};return true;}
    if(value is DimensionStyleOverrideDictionary d){result=new{type="DimensionStyleOverrideDictionary",items=d.Select(p=>new[]{Wire(p.Key),Wire(p.Value)}).ToArray()};return true;}
    if(value is KeyValuePair<DimensionStyleOverrideType,DimensionStyleOverride> pair){result=new[]{Wire(pair.Key),Wire(pair.Value)};return true;}
    if(value is not DimensionStyle && value is not DimensionStyleAlternateUnits && value is not DimensionStyleTolerances)return false;
    var fields=new Dictionary<string,object?>();
    foreach(var p in value.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly))
      if(p.Name!="Owner")fields[p.Name]=Wire(p.GetValue(value));
    if(value is DimensionStyle s)result=new{type="DimensionStyle",fields,name=s.Name,code=s.CodeName,handle=s.Handle,owner=s.Owner?.CodeName,reserved=s.IsReserved,xdata=s.XData.Values.Select(Wire).ToArray()};
    else result=new{type=value.GetType().Name,fields};return true;
  }
}
