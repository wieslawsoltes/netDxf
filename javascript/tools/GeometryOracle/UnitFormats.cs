// Observation only: operations execute on the unchanged pinned production assembly.
using System.Collections.Generic;
using System.Reflection;
using netDxf.Units;
internal static partial class Program {
    private static bool UnitFormatWire(object value,out object? result){
        result=null;if(value is not UnitStyleFormat)return false;
        var fields=new Dictionary<string,object?>();
        foreach(var property in typeof(UnitStyleFormat).GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
            fields[property.Name]=Wire(property.GetValue(value));
        result=new {type="UnitStyleFormat",fields};return true;
    }
}
