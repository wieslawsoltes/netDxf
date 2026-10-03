// Observation-only reflection of unchanged native DIMENSION models.
using System.Collections.Generic;
using System.Reflection;
using netDxf.Entities;
internal static partial class Program {
    private static bool ConcreteDimensionWire(EntityObject value, object common, out object? result) {
        result=null;
        if(value is not Dimension dimension)return false;
        var fields=new Dictionary<string,object?>();
        const BindingFlags flags=BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly;
        foreach(var property in typeof(Dimension).GetProperties(flags))fields[property.Name]=Wire(property.GetValue(dimension));
        foreach(var property in value.GetType().GetProperties(flags))fields[property.Name]=Wire(property.GetValue(dimension));
        fields["DefinitionPoint"]=Wire(typeof(Dimension).GetProperty("DefinitionPoint",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dimension));
        result=new {common,fields};return true;
    }
}
