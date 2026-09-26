// Retained constructor/registration observations, not a substitute typed reader.
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.IO;
using netDxf.Entities;
internal static partial class Program {
    private static object? StoredTableSnapshot(object? value) {
        if(value==null)return null;
        if(value is StoredTable table)return new {kind="table",common=TableRef(table),version=(int)table.SourceVersion,normal=GeometryPoint(table.Normal),position=GeometryPoint(table.Position),grid=StoredTableSnapshot(table.Grid),
            tags=table.Payload.Select(TableStyleSnapshot).ToArray(),references=table.References.Select(TableRef).ToArray(),backing=TableRef(table.BackingContent),typedBacking=TableRef(table.StoredBackingContent),agreement=table.BackingLiteralValuesAgree,
            resourceNames=table.Payload.Select(tag=>(string?)typeof(StoredTable).GetMethod("ChangedResourceName",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(table,new object[]{tag})).ToArray()};
        if(value is StoredTableGrid grid)return new {kind="grid",rows=grid.RowCount,columns=grid.ColumnCount,heights=grid.RowHeights.Select(TableReal).ToArray(),widths=grid.ColumnWidths.Select(TableReal).ToArray(),cells=grid.Cells.Select(StoredTableSnapshot).ToArray()};
        if(value is StoredTableCell cell)return new {kind="cell",type=cell.StoredType,field=cell.HasFieldReference,valueType=cell.ValueType,flags=cell.StoredFlags,hasValue=cell.HasLiteralValue,value=ContentScalar(cell.LiteralValue),tags=cell.Tags.Select(TableStyleSnapshot).ToArray()};
        if(value is IEnumerable values&&value is not string)return values.Cast<object?>().Select(StoredTableSnapshot).ToArray();
        return TableContentSnapshot(value);
    }
    private static object StoredTableLoad(JsonElement step,DxfDocument document){
        var tags=step.GetProperty("tags").EnumerateArray().Select(row=>new DxfTag(row[0].GetInt16(),TableTagInput(row[1]))).ToList();
        var table=(StoredTable)Activator.CreateInstance(typeof(StoredTable),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{document,tags,TableDecoder},null)!;
        if(step.TryGetProperty("block",out var block))((netDxf.Blocks.Block)Read(block)!).Entities.Add(table);
        else if(!step.TryGetProperty("register",out var register)||register.GetBoolean())document.Entities.Add(table);
        if(!step.TryGetProperty("resolve",out var resolve)||resolve.GetBoolean())typeof(StoredTable).GetMethod("Resolve",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(table,null);
        return table;
    }
    private static object? StoredTableInternal(JsonElement step,object target){
        var args=Arguments(step);return target.GetType().GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name==step.GetProperty("member").GetString()&&Matches(m.GetParameters(),args)).Invoke(target,args);
    }
}
