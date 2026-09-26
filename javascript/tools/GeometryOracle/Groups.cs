// Observation-only snapshots of the pinned C# Group model and event arguments.
using System.Linq;
using netDxf.Objects;
internal static partial class Program {
    private static bool GroupWire(object value,out object? result) {
        result=null;
        if(value is GroupEntityChangeEventArgs args){result=new {type="GroupEntityChangeEventArgs",item=Wire(args.Item)};return true;}
        if(value is not Group group)return false;
        result=new {type="Group",name=group.Name,code=group.CodeName,reserved=group.IsReserved,unnamed=group.IsUnnamed,
            description=Wire(group.Description),selectable=group.IsSelectable,handle=group.Handle,owner=group.Owner?.CodeName,
            entities=group.Entities.Select(Wire).ToArray(),xdata=group.XData.Values.Select(Wire).ToArray()};return true;
    }
}
