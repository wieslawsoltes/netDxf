using System;
using System.Collections.Generic;
using netDxf.Entities;

namespace netDxf.Objects
{
    public partial class PlotSettings
    {
        private short standardScaleType;
        private double? standardScaleFactor;
        private DxfObject shadePlotObject;
        /// <summary>Gets or sets the exact group-75 standard scale code, from 0 (fit) through 32.</summary>
        /// <remarks>ScaleToFit reads true only for code 0. Setting it false retains an existing nonzero code or selects 16.</remarks>
        public short StandardScaleType
        {
            get { return this.standardScaleType; }
            set { if (value < 0 || value > 32) throw new ArgumentOutOfRangeException(nameof(value)); this.standardScaleType = value; }
        }
        /// <summary>Gets or sets the stored group-147 scale factor independently of the custom numerator/denominator.</summary>
        /// <remarks>Null retains the historical authoring behavior of writing PrintScale. Any finite explicit value is retained without evaluating its plotting meaning.</remarks>
        public double? StandardScaleFactor
        {
            get { return this.standardScaleFactor; }
            set { if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value))) throw new ArgumentOutOfRangeException(nameof(value)); this.standardScaleFactor = value; }
        }
        /// <summary>Gets or sets the optional group-333 reference to a registered nongraphical shade-plot object.</summary>
        /// <remarks>The referenced private rendering schema is not interpreted or executed.</remarks>
        public DxfObject ShadePlotObject
        {
            get { return this.shadePlotObject; }
            set { if (value is EntityObject || value is Entities.Attribute || value is AttributeDefinition || value is DxfDocument) throw new ArgumentException("A shade-plot reference must identify a nongraphical object.", nameof(value)); this.shadePlotObject = value; }
        }
        internal static void ValidateValues(PlotSettings plot, List<string> errors)
        {
            if (plot == null) { errors.Add("Plot settings cannot be null."); return; }
            foreach (string text in new[] { plot.PageSetupName, plot.PlotterName, plot.PaperSizeName, plot.ViewName, plot.CurrentStyleSheet })
                if (!ValidText(text)) errors.Add("Plot settings strings must be nonnull single-line Unicode text.");
            foreach (double value in new[] { plot.PaperMargin.Left, plot.PaperMargin.Bottom, plot.PaperMargin.Right, plot.PaperMargin.Top,
                plot.PaperSize.X, plot.PaperSize.Y, plot.Origin.X, plot.Origin.Y, plot.WindowBottomLeft.X, plot.WindowBottomLeft.Y,
                plot.WindowUpRight.X, plot.WindowUpRight.Y, plot.PrintScaleNumerator, plot.PrintScaleDenominator, plot.StandardScaleFactor ?? plot.PrintScale,
                plot.PaperImageOrigin.X, plot.PaperImageOrigin.Y })
                if (double.IsNaN(value) || double.IsInfinity(value)) errors.Add("Plot settings numeric values must be finite.");
            if (plot.PrintScaleNumerator <= 0 || plot.PrintScaleDenominator <= 0) errors.Add("Plot settings custom scales must be positive.");
            if ((int)plot.PaperUnits < 0 || (int)plot.PaperUnits > 2 || (int)plot.PaperRotation < 0 || (int)plot.PaperRotation > 3 || (int)plot.PlotType < 0 || (int)plot.PlotType > 5 ||
                (int)plot.ShadePlotMode < 0 || (int)plot.ShadePlotMode > 3 || (int)plot.ShadePlotResolutionMode < 0 || (int)plot.ShadePlotResolutionMode > 5 || plot.ShadePlotDPI < 100)
                errors.Add("Invalid plot settings enumeration or shade resolution.");
            if ((int)plot.Flags < short.MinValue || (int)plot.Flags > short.MaxValue) errors.Add("Plot flags must fit their signed 16-bit DXF field.");
        }
        private static bool ValidText(string text)
        {
            if (text == null || text.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i])) { if (i + 1 >= text.Length || !char.IsLowSurrogate(text[++i])) return false; }
                else if (char.IsLowSurrogate(text[i])) return false;
            }
            return true;
        }
    }
}
