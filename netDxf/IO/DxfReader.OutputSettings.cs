using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Tuple<PlotSettings, string>> outputShadeReferences = new List<Tuple<PlotSettings, string>>();
        private static readonly HashSet<short> PlotSettingsCodes = new HashSet<short> { 1, 2, 4, 6, 7, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 140, 141, 142, 143, 70, 72, 73, 74, 75, 76, 77, 78, 147, 148, 149, 333 };
        private bool ReadOutputSettingsPayload(DatabaseRecord record, string type, List<DxfTag> tags, int start)
        {
            if (type != "PLOTSETTINGS" && type != "WIPEOUTVARIABLES") return false;
            int end = tags.FindIndex(start, t => t.Code == 1001); if (end < 0) end = tags.Count;
            var body = tags.GetRange(start, end - start);
            string marker = type == "PLOTSETTINGS" ? "AcDbPlotSettings" : "AcDbWipeoutVariables";
            if (body.Count == 0 || body[0].Code != 100 || (string)body[0].Value != marker) return false;
            if (body.Skip(1).Any(t => type == "PLOTSETTINGS" ? !PlotSettingsCodes.Contains(t.Code) : t.Code != 70)) return false;
            if (type == "PLOTSETTINGS")
            {
                var result = new DxfPlotSettingsObject(this.ParsePlotSettings(body, out string handle));
                this.outputShadeReferences.Add(Tuple.Create(result.Settings, handle)); record.Object = result;
            }
            else
            {
                if (body.Count > 2) throw new FormatException("Duplicate WIPEOUTVARIABLES frame flag.");
                short frame = body.Count == 2 ? (short)body[1].Value : (short)0;
                if (frame != 0 && frame != 1) throw new FormatException("WIPEOUTVARIABLES frame flag must be zero or one.");
                record.Object = new DxfWipeoutVariables { DisplayFrame = frame == 1 };
            }
            if (end < tags.Count) this.ReadDatabaseXData(record.Object, tags, end);
            return true;
        }
        private PlotSettings ParsePlotSettings(List<DxfTag> tags, out string shadeHandle)
        {
            shadeHandle = null;
            if (tags.Count == 0 || tags[0].Code != 100 || (string)tags[0].Value != "AcDbPlotSettings") throw new FormatException("Missing AcDbPlotSettings subclass.");
            var seen = new HashSet<short>(); var plot = new PlotSettings();
            var margins = plot.PaperMargin; var paper = plot.PaperSize; var origin = plot.Origin; var lower = plot.WindowBottomLeft; var upper = plot.WindowUpRight; var image = plot.PaperImageOrigin;
            try
            {
                foreach (DxfTag tag in tags.Skip(1))
                {
                    if (!PlotSettingsCodes.Contains(tag.Code) || !seen.Add(tag.Code)) throw new FormatException("Unsupported or duplicate plot settings group: " + tag.Code);
                    switch (tag.Code)
                    {
                        case 1: plot.PageSetupName = this.DecodeEncodedNonAsciiCharacters((string)tag.Value); break;
                        case 2: plot.PlotterName = this.DecodeEncodedNonAsciiCharacters((string)tag.Value); break;
                        case 4: plot.PaperSizeName = this.DecodeEncodedNonAsciiCharacters((string)tag.Value); break;
                        case 6: plot.ViewName = this.DecodeEncodedNonAsciiCharacters((string)tag.Value); break;
                        case 7: plot.CurrentStyleSheet = this.DecodeEncodedNonAsciiCharacters((string)tag.Value); break;
                        case 40: margins.Left = (double)tag.Value; break;
                        case 41: margins.Bottom = (double)tag.Value; break;
                        case 42: margins.Right = (double)tag.Value; break;
                        case 43: margins.Top = (double)tag.Value; break;
                        case 44: paper.X = (double)tag.Value; break;
                        case 45: paper.Y = (double)tag.Value; break;
                        case 46: origin.X = (double)tag.Value; break;
                        case 47: origin.Y = (double)tag.Value; break;
                        case 48: lower.X = (double)tag.Value; break;
                        case 49: lower.Y = (double)tag.Value; break;
                        case 140: upper.X = (double)tag.Value; break;
                        case 141: upper.Y = (double)tag.Value; break;
                        case 142: plot.PrintScaleNumerator = (double)tag.Value; break;
                        case 143: plot.PrintScaleDenominator = (double)tag.Value; break;
                        case 70: plot.Flags = (PlotFlags)(short)tag.Value; break;
                        case 72: plot.PaperUnits = (PlotPaperUnits)(short)tag.Value; break;
                        case 73: plot.PaperRotation = (PlotRotation)(short)tag.Value; break;
                        case 74: plot.PlotType = (PlotType)(short)tag.Value; break;
                        case 75: plot.StandardScaleType = (short)tag.Value; break;
                        case 76: plot.ShadePlotMode = (ShadePlotMode)(short)tag.Value; break;
                        case 77: plot.ShadePlotResolutionMode = (ShadePlotResolutionMode)(short)tag.Value; break;
                        case 78: plot.ShadePlotDPI = (short)tag.Value; break;
                        case 147: plot.StandardScaleFactor = (double)tag.Value; break;
                        case 148: image.X = (double)tag.Value; break;
                        case 149: image.Y = (double)tag.Value; break;
                        case 333: shadeHandle = (string)tag.Value; break;
                    }
                }
                plot.PaperMargin = margins; plot.PaperSize = paper; plot.Origin = origin; plot.WindowBottomLeft = lower; plot.WindowUpRight = upper; plot.PaperImageOrigin = image;
                var errors = new List<string>(); PlotSettings.ValidateValues(plot, errors);
                if (errors.Count > 0) throw new FormatException(string.Join("; ", errors));
                return plot;
            }
            catch (ArgumentException error) { throw new FormatException("Invalid plot settings field.", error); }
        }
        private void ResolveOutputSettingsReferences()
        {
            foreach (Tuple<PlotSettings, string> pair in this.outputShadeReferences)
            {
                if (string.IsNullOrEmpty(pair.Item2) || pair.Item2 == "0") continue;
                DxfObject target = this.doc.GetObjectByHandle(pair.Item2);
                if (target == null) throw new FormatException("Unresolved plot-settings shade reference: " + pair.Item2);
                try { pair.Item1.ShadePlotObject = target; }
                catch (ArgumentException error) { throw new FormatException("Invalid plot-settings shade object.", error); }
            }
        }
    }
}
