// Observation-only adapter over the actual pinned netDxf.Header APIs.
using netDxf.Header;
internal static partial class Program {
    private static bool HeaderWire(object value,out object? result) {
        result=null;
        // Clocks and host usernames are nondeterministic. Each exact scenario explicitly
        // assigns deterministic values before requesting KnownValues or timestamp fields.
        if(value is HeaderVariables){result=new {type="HeaderVariables"};return true;}
        if(value is HeaderVariable v){result=new {type="HeaderVariable",name=v.Name,code=v.GroupCode,valueType=v.Value?.GetType().Name,value=Wire(v.Value)};return true;}
        return false;
    }
}
