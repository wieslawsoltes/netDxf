// Test-only snapshots of the unchanged pinned production assembly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using netDxf;
using netDxf.Objects;
internal static partial class Program
{
    private static object? OutputReference(DxfObject? value) => value == null ? null :
        new { type = value.GetType().Name, code = value.CodeName, handle = value.Handle };
    private static bool OutputSettingsWire(object value, out object? result)
    {
        result = null;
        if (value is PaperMargin margin) { result = new {type="PaperMargin",left=Wire(margin.Left),bottom=Wire(margin.Bottom),right=Wire(margin.Right),top=Wire(margin.Top)}; return true; }
        if (value is PlotSettings plot)
        {
            var fields = new Dictionary<string,object?>();
            foreach (string key in new[]{"PageSetupName","PlotterName","PaperSizeName","ViewName","CurrentStyleSheet","PaperMargin","PaperSize","Origin",
                "WindowUpRight","WindowBottomLeft","ScaleToFit","PrintScaleNumerator","PrintScaleDenominator","PrintScale","Flags","PlotType",
                "PaperUnits","PaperRotation","ShadePlotMode","ShadePlotResolutionMode","ShadePlotDPI","PaperImageOrigin","StandardScaleType","StandardScaleFactor"})
                fields[key] = Wire(typeof(PlotSettings).GetProperty(key)!.GetValue(plot));
            result = new {type="PlotSettings",fields,shade=OutputReference(plot.ShadePlotObject)}; return true;
        }
        if (value is DxfPlotSettingsObject || value is DxfWipeoutVariables)
        {
            var db=(DxfDatabaseObject)value;
            var references=(IEnumerable<DxfObject>)db.GetType().GetProperty("DatabaseReferences",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(db)!;
            var common=new {type=value.GetType().Name,code=db.CodeName,handle=db.Handle,owner=OutputReference(db.Owner),erased=db.IsErased,
                xdata=db.XData.Values.Select(Wire).ToArray(),references=references.Select(OutputReference).ToArray()};
            result=value is DxfPlotSettingsObject page ? (object)new {common,settings=Wire(page.Settings)} : new {common,displayFrame=((DxfWipeoutVariables)value).DisplayFrame};return true;
        }
        if (value is RasterVariables raster)
        {
            result=new {type="RasterVariables",code=raster.CodeName,handle=raster.Handle,owner=OutputReference(raster.Owner),frame=raster.DisplayFrame,
                quality=Wire(raster.DisplayQuality),units=Wire(raster.Units),xdata=raster.XData.Values.Select(Wire).ToArray()}; return true;
        }
        return false;
    }
}
