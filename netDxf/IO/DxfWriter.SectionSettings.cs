using System;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteSectionSettingsPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfSectionSettings settings)) return false;
            if (this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2007) throw new NotSupportedException("Typed SECTIONSETTINGS requires DXF 2007 or later.");
            settings.ValidateValues();
            this.chunk.Write(100, "AcDbSectionSettings"); this.chunk.Write(90, settings.SectionType); this.chunk.Write(91, settings.TypeSettings.Count);
            foreach (DxfSectionTypeSettings type in settings.TypeSettings)
            {
                this.chunk.Write(1, "SectionTypeSettings"); this.chunk.Write(90, type.SectionType); this.chunk.Write(91, type.GenerationOptions);
                this.chunk.Write(92, type.SourceObjects.Count);
                foreach (DxfObject source in type.SourceObjects) this.chunk.Write(330, source?.Handle ?? "0");
                this.chunk.Write(331, type.DestinationBlock?.Handle ?? "0"); this.chunk.Write(1, this.EncodeDatabaseString(type.DestinationFileName));
                this.chunk.Write(93, type.GeometrySettings.Count);
                if (!type.RepeatGeometryMarkers) this.chunk.Write(2, "SectionGeometrySettings");
                foreach (DxfSectionGeometrySettings geometry in type.GeometrySettings)
                {
                    if (type.RepeatGeometryMarkers) this.chunk.Write(2, "SectionGeometrySettings");
                    this.chunk.Write(90, geometry.SectionType); this.chunk.Write(91, geometry.GeometryValue); this.chunk.Write(92, geometry.Flags);
                    this.chunk.Write(geometry.ColorCode, geometry.ColorIndex);
                    this.chunk.Write(8, this.EncodeDatabaseString(geometry.LayerName)); this.chunk.Write(6, this.EncodeDatabaseString(geometry.LinetypeName));
                    this.chunk.Write(40, geometry.LinetypeScale); this.chunk.Write(1, this.EncodeDatabaseString(geometry.PlotStyleName)); this.chunk.Write(370, geometry.Lineweight);
                    this.chunk.Write(70, geometry.FaceTransparency); this.chunk.Write(71, geometry.EdgeTransparency); this.chunk.Write(72, geometry.HatchPatternType);
                    this.chunk.Write(2, this.EncodeDatabaseString(geometry.HatchPatternName)); this.chunk.Write(41, geometry.HatchAngle);
                    this.chunk.Write(42, geometry.HatchScale); this.chunk.Write(43, geometry.HatchSpacing); this.chunk.Write(3, "SectionGeometrySettingsEnd");
                }
                this.chunk.Write(3, "SectionTypeSettingsEnd");
            }
            return true;
        }
    }
}
