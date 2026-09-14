using System;
using System.Collections.Generic;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void ValidateOutputSettings()
        {
            var errors = new List<string>();
            bool shadeReferencesQualified = this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007;
            if (!shadeReferencesQualified)
                foreach (DxfObject item in this.doc.AddedObjects.Values)
                    if (item is DxfPlotSettingsObject page && page.Settings.ShadePlotObject != null)
                        errors.Add("Plot-settings shade references require the qualified AutoCAD 2007 or later export profile.");
            foreach (Layout layout in this.doc.Layouts)
            {
                PlotSettings.ValidateValues(layout.PlotSettings, errors);
                if (!shadeReferencesQualified && layout.PlotSettings?.ShadePlotObject != null)
                    errors.Add("Layout shade references require the qualified AutoCAD 2007 or later export profile.");
                if (layout.PlotSettings?.ShadePlotObject != null && (layout.PlotSettings.ShadePlotObject.Handle == null || this.doc.GetObjectByHandle(layout.PlotSettings.ShadePlotObject.Handle) != layout.PlotSettings.ShadePlotObject))
                    errors.Add("A layout shade-plot reference is not registered in this document.");
            }
            if (errors.Count > 0) throw new InvalidOperationException("Invalid plot settings: " + string.Join("; ", errors));
        }
        private bool WriteOutputSettingsPayload(DxfDatabaseObject item)
        {
            if (item is DxfPlotSettingsObject plot) this.WritePlotSettingsPayload(plot.Settings);
            else if (item is DxfWipeoutVariables wipeout) { this.chunk.Write(100, "AcDbWipeoutVariables"); this.chunk.Write(70, wipeout.DisplayFrame ? (short)1 : (short)0); }
            else return false;
            return true;
        }
        private void WritePlotSettingsPayload(PlotSettings plot)
        {
            this.chunk.Write(100, "AcDbPlotSettings");
            this.chunk.Write(1, this.EncodeDatabaseString(plot.PageSetupName)); this.chunk.Write(2, this.EncodeDatabaseString(plot.PlotterName));
            this.chunk.Write(4, this.EncodeDatabaseString(plot.PaperSizeName)); this.chunk.Write(6, this.EncodeDatabaseString(plot.ViewName));
            this.chunk.Write(40, plot.PaperMargin.Left); this.chunk.Write(41, plot.PaperMargin.Bottom); this.chunk.Write(42, plot.PaperMargin.Right); this.chunk.Write(43, plot.PaperMargin.Top);
            this.chunk.Write(44, plot.PaperSize.X); this.chunk.Write(45, plot.PaperSize.Y); this.chunk.Write(46, plot.Origin.X); this.chunk.Write(47, plot.Origin.Y);
            this.chunk.Write(48, plot.WindowBottomLeft.X); this.chunk.Write(49, plot.WindowBottomLeft.Y); this.chunk.Write(140, plot.WindowUpRight.X); this.chunk.Write(141, plot.WindowUpRight.Y);
            this.chunk.Write(142, plot.PrintScaleNumerator); this.chunk.Write(143, plot.PrintScaleDenominator); this.chunk.Write(70, (short)plot.Flags);
            this.chunk.Write(72, (short)plot.PaperUnits); this.chunk.Write(73, (short)plot.PaperRotation); this.chunk.Write(74, (short)plot.PlotType);
            this.chunk.Write(7, this.EncodeDatabaseString(plot.CurrentStyleSheet)); this.chunk.Write(75, plot.StandardScaleType);
            this.chunk.Write(76, (short)plot.ShadePlotMode); this.chunk.Write(77, (short)plot.ShadePlotResolutionMode); this.chunk.Write(78, plot.ShadePlotDPI);
            this.chunk.Write(147, plot.StandardScaleFactor ?? plot.PrintScale); this.chunk.Write(148, plot.PaperImageOrigin.X); this.chunk.Write(149, plot.PaperImageOrigin.Y);
            if (plot.ShadePlotObject != null) this.chunk.Write(333, plot.ShadePlotObject.Handle);
        }
    }
}
