// Observation only. Every operation runs against the unchanged pinned library.
using System.Linq;
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
internal static partial class Program {
  private static bool InsertEntityWire(EntityObject value,object common,out object? result){
    result=null;if(value is not Insert i)return false;
    result=new {common,block=Wire(i.Block),position=Wire(i.Position),scale=Wire(i.Scale),rotation=Wire(i.Rotation),rows=(int)i.RowCount,columns=(int)i.ColumnCount,dx=Wire(i.ColumnSpacing),dy=Wire(i.RowSpacing),multiple=i.IsMultiple,count=i.InstanceCount,attributes=i.Attributes.Select(Wire).ToArray()};return true;
  }
  private static object? BlockOwner(DxfObject? value)=>value==null?null:new {code=value.CodeName,handle=value.Handle};
  private static bool BlockWire(object value,out object? result){
    result=null;
    if(value is BlockEntityChangeEventArgs e)result=new {type="BlockEntityChangeEventArgs",item=Wire(e.Item)};
    else if(value is BlockAttributeDefinitionChangeEventArgs a)result=new {type="BlockAttributeDefinitionChangeEventArgs",item=Wire(a.Item)};
    else if(value is BlockRecord r)result=new {type="BlockRecord",name=Wire(r.Name),code=r.CodeName,handle=r.Handle,owner=BlockOwner(r.Owner),layout=BlockOwner(r.Layout),units=(int)r.Units,explode=r.AllowExploding,uniform=r.ScaleUniformly,@internal=r.IsForInternalUseOnly,xdata=r.XData.Values.Select(Wire).ToArray()};
    else if(value.GetType().FullName=="netDxf.Blocks.EndBlock") {var o=(DxfObject)value;result=new {type="EndBlock",code=o.CodeName,handle=o.Handle,owner=BlockOwner(o.Owner),xdata=o.XData.Values.Select(Wire).ToArray()};}
    else if(value is Block b)result=new {type="Block",name=Wire(b.Name),code=b.CodeName,handle=b.Handle,reserved=b.IsReserved,@internal=b.IsForInternalUseOnly,flags=(int)b.Flags,xref=Wire(b.XrefFile),isXref=b.IsXRef,description=Wire(b.Description),origin=Wire(b.Origin),layer=Wire(b.Layer),record=Wire(b.Record),end=Wire(typeof(Block).GetProperty("End",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(b)),entities=b.Entities.Select(Wire).ToArray(),attributes=b.AttributeDefinitions.Values.Select(Wire).ToArray(),xdata=b.XData.Values.Select(Wire).ToArray()};
    else return false;return true;
  }
}
