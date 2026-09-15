// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.IO;
using System.Linq;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void ValidateSections()
        {
            foreach (Section section in this.doc.Blocks.SelectMany(b => b.Entities).OfType<Section>()) section.Validate(this.doc);
            foreach (DxfSectionSettings settings in this.doc.Objects.Items.OfType<DxfSectionSettings>())
            {
                if (this.doc.DrawingVariables.AcadVer < netDxf.Header.DxfVersion.AutoCad2007) throw new System.NotSupportedException("SECTIONSETTINGS output requires R2007 or later.");
                settings.ValidateValues();
            }
        }
        private void PrepareSectionClasses(DxfClassCollection definitions)
        {
            foreach (string name in new[] { "SECTION", "SECTIONOBJECT" })
            {
                int count = this.doc.Blocks.Sum(b => b.Entities.OfType<Section>().Count(s => s.CodeName == name));
                this.PrepareSectionClass(definitions, name, "AcDbSection", true, count, count != 0);
            }
            foreach (string name in new[] { "SECTIONSETTINGS", "SECTION_SETTINGS" })
            {
                int count = this.doc.Objects.Items.Count(s => s.CodeName == name);
                this.PrepareSectionClass(definitions, name, "AcDbSectionSettings", false, count, this.doc.Objects.Items.Any(s => s.CodeName == name && s is DxfSectionSettings));
            }
        }
        private void PrepareSectionClass(DxfClassCollection definitions, string name, string cpp, bool entity, int count, bool typed)
        {
            if (definitions.Contains(name))
            {
                DxfClass definition = definitions[name];
                if (definition.CppClassName != cpp || definition.IsEntity != entity)
                { if (typed) throw new InvalidDataException("CLASS conflicts with " + name); return; }
                definition.InstanceCount = count;
            }
            else if (typed) definitions.Add(new DxfClass(name, cpp, "ObjectDBX Classes") { ProxyFlags = entity ? 1025 : 1024, IsEntity = entity, InstanceCount = count });
        }
        private void WriteSection(Section section)
        {
            this.chunk.Write(100, "AcDbSection"); this.chunk.Write(90, section.State); this.chunk.Write(91, section.Flags);
            this.chunk.Write(1, this.EncodeDatabaseString(section.Name)); this.WriteMLeaderVector(10, section.VerticalDirection);
            this.chunk.Write(40, section.TopHeight); this.chunk.Write(41, section.BottomHeight); this.chunk.Write(70, section.IndicatorTransparency);
            if (section.StoredNativeIndicatorColor.HasValue) this.chunk.Write(62, section.StoredNativeIndicatorColor.Value);
            if (section.StoredIndicatorColor.HasValue) this.chunk.Write(63, section.StoredIndicatorColor.Value);
            if (section.IndicatorColorName != null) this.chunk.Write(411, this.EncodeDatabaseString(section.IndicatorColorName));
            this.chunk.Write(92, section.Vertices.Count); foreach (Vector3 vertex in section.Vertices) this.WriteMLeaderVector(11, vertex);
            this.chunk.Write(93, section.BackLineVertices.Count); foreach (Vector3 vertex in section.BackLineVertices) this.WriteMLeaderVector(12, vertex);
            if (section.HasSettingsField) this.chunk.Write(360, section.GeometrySettings == null ? "0" : section.GeometrySettings.Handle);
            this.WriteXData(section.XData);
        }
    }
}
