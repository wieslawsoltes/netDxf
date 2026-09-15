// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        /// <summary>Reports known writer rejections and omissions for a proposed DXF target version.</summary>
        /// <param name="targetVersion">A recognized DXF version. Pre-R2000 versions report the unsupported writer profile.</param>
        /// <returns>A bounded, immutable diagnostic snapshot with live references to the inspected sources.</returns>
        /// <remarks>No drawing variables, handles, metadata or object collections are changed. The method
        /// does not create the lazy named-object database, write output or invoke Save/preflight callbacks.
        /// It inspects registered blocks (including unused definitions and paper space), their attributes,
        /// registered objects, named views, viewport records, plot settings and explicit class definitions.
        /// An empty report does not establish complete DXF legality or guarantee that Save will succeed.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The supplied value is Unknown or not a defined DXF version.</exception>
        public DxfVersionCompatibilityReport AnalyzeVersionCompatibility(DxfVersion targetVersion)
        {
            if (targetVersion == DxfVersion.Unknown || !Enum.IsDefined(typeof(DxfVersion), targetVersion))
                throw new ArgumentOutOfRangeException(nameof(targetVersion));
            var analysis = new VersionCompatibilityAnalysis(this, targetVersion);
            return analysis.Run();
        }

        private sealed class VersionCompatibilityAnalysis
        {
            private readonly DxfDocument document;
            private readonly DxfVersion target;
            private readonly List<DxfVersionCompatibilityDiagnostic> diagnostics = new List<DxfVersionCompatibilityDiagnostic>();
            internal VersionCompatibilityAnalysis(DxfDocument document, DxfVersion target)
            { this.document = document; this.target = target; }
            private void Add(string code, object source, string property, string message,
                DxfVersionCompatibilityKind kind = DxfVersionCompatibilityKind.WriterRejection)
            {
                string name = source is DxfObject item ? item.CodeName : source is DxfClass ? "CLASS" : "HEADER";
                this.diagnostics.Add(new DxfVersionCompatibilityDiagnostic(code, kind, source, name, property, message));
            }
            private void Minimum(DxfVersion minimum, string code, DxfObject source, string property, string feature)
            {
                if (this.target < minimum) this.Add(code, source, property, feature + " requires the " + minimum + " or later writer profile.");
            }
            private void SourceProfile(DxfObject source, DxfVersion sourceVersion)
            {
                if (this.target != sourceVersion) this.Add("STORED_SOURCE_PROFILE", source, "SourceVersion",
                    "This stored packet requires its original " + sourceVersion + " writer profile; automatic schema conversion is unavailable.");
            }
            internal DxfVersionCompatibilityReport Run()
            {
                if (this.target < DxfVersion.AutoCad2000)
                {
                    this.Add("DXF_WRITER_PROFILE_UNSUPPORTED", this.document.DrawingVariables, "AcadVer", "The typed writer supports AutoCad2000 through AutoCad2018 profiles.");
                    return new DxfVersionCompatibilityReport(this.document.DrawingVariables.AcadVer, this.target, this.diagnostics);
                }
                // Blocks are eagerly registered; do not use Objects or NamedObjects here.
                foreach (var block in this.document.Blocks)
                {
                    foreach (AttributeDefinition definition in block.AttributeDefinitions.Values) this.Common(definition, definition.CommonData);
                    foreach (EntityObject entity in block.Entities)
                    {
                        this.Entity(entity);
                        if (entity is Insert insert)
                            foreach (Entities.Attribute attribute in insert.Attributes) this.Common(attribute, attribute.CommonData);
                    }
                }
                foreach (DxfObject item in this.document.AddedObjects.Values) this.RegisteredObject(item);
                foreach (View view in this.document.Views)
                {
                    if (view.IsCameraPlottable) this.Minimum(DxfVersion.AutoCad2007, "VIEW_CAMERA_PROFILE", view, "IsCameraPlottable", "A plottable named-view camera");
                    if (view.HasStoredLiveSection) this.Minimum(DxfVersion.AutoCad2007, "VIEW_LIVE_SECTION_PROFILE", view, "LiveSection", "A stored VIEW live-section slot (including explicit null)");
                    this.SunSlot(view);
                }
                foreach (VPort port in this.document.VPorts.Records) this.SunSlot(port);
                foreach (Layout layout in this.document.Layouts)
                    if (layout.PlotSettings?.ShadePlotObject != null)
                        this.Minimum(DxfVersion.AutoCad2007, "PLOT_SHADE_REFERENCE_PROFILE", layout, "PlotSettings.ShadePlotObject", "A layout shade-plot reference");
                if (this.target == DxfVersion.AutoCad2000)
                {
                    if (!string.IsNullOrEmpty(this.document.DrawingVariables.LastSavedBy))
                        this.Add("HEADER_LAST_SAVED_BY_OMITTED", this.document.DrawingVariables, "LastSavedBy", "The R2000 writer omits $LASTSAVEDBY.", DxfVersionCompatibilityKind.DataOmission);
                    foreach (DxfClass definition in this.document.Classes)
                        if (definition.InstanceCount.HasValue)
                            this.Add("CLASS_INSTANCE_COUNT_OMITTED", definition, "InstanceCount", "The R2000 writer omits the explicit CLASS group-91 instance count.", DxfVersionCompatibilityKind.DataOmission);
                }
                return new DxfVersionCompatibilityReport(this.document.DrawingVariables.AcadVer, this.target, this.diagnostics);
            }
            private void Common(DxfObject source, CommonEntityData data)
            {
                if (data.ColorName != null) this.Minimum(DxfVersion.AutoCad2004, "ENTITY_COLOR_NAME_PROFILE", source, "ColorName", "Entity color-name data");
                if (data.ShadowMode.HasValue) this.Minimum(DxfVersion.AutoCad2007, "ENTITY_SHADOW_MODE_PROFILE", source, "ShadowMode", "Entity shadow-mode data");
            }
            private void Entity(EntityObject entity)
            {
                this.Common(entity, entity.CommonData);
                if (entity is Mesh) this.Minimum(DxfVersion.AutoCad2010, "MESH_PROFILE", entity, "Type", "MESH entities");
                if (entity is Helix) this.Minimum(DxfVersion.AutoCad2007, "HELIX_PROFILE", entity, "Type", "HELIX entities");
                if (entity is Light) this.Minimum(DxfVersion.AutoCad2007, "LIGHT_PROFILE", entity, "Type", "LIGHT entities");
                if (entity is Section) this.Minimum(DxfVersion.AutoCad2007, "SECTION_PROFILE", entity, "Type", "SECTION entities");
                if (entity is Viewport viewport) this.SunSlot(viewport);
                if (entity is DxfOpaqueEntity opaque) this.SourceProfile(opaque, opaque.SourceVersion);
                if (entity is StoredTable table) this.SourceProfile(table, table.SourceVersion);
                if (entity is Polyline2D polyline)
                    for (int i = 0; i < polyline.Vertexes.Count; i++)
                        if (polyline.Vertexes[i].VertexIdentifier.HasValue)
                            this.Minimum(DxfVersion.AutoCad2013, "LWPOLYLINE_VERTEX_ID_PROFILE", entity, "Vertexes[" + i + "].VertexIdentifier", "LWPOLYLINE vertex identifiers");
                if (entity is MText text) this.MText(text);
                if (entity is Hatch hatch) this.Hatch(hatch);
                if (entity is MultiLeader leader) this.MultiLeader(leader);
                if (entity is AcisEntity)
                {
                    if (this.target >= DxfVersion.AutoCad2013)
                        this.Add("ACIS_SAT_PROFILE", entity, "EncodedSatChunks", "Stored SAT entities support R2000 through R2010. The R2013+ writer requires unimplemented SAB/ACDSDATA support.");
                    if (entity is Solid3D solid && solid.HistoryHandle != null)
                        this.Minimum(DxfVersion.AutoCad2007, "ACIS_HISTORY_PROFILE", entity, "HistoryHandle", "Explicit ACIS history data");
                }
            }
            private void MText(MText text)
            {
                if (text.BackgroundFill != null)
                {
                    this.Minimum(DxfVersion.AutoCad2007, "MTEXT_BACKGROUND_PROFILE", text, "BackgroundFill", "Stored MTEXT background data");
                    if ((text.BackgroundFill.Flags & MTextBackgroundFillFlags.TextFrame) != 0)
                        this.Minimum(DxfVersion.AutoCad2018, "MTEXT_FRAME_PROFILE", text, "BackgroundFill.Flags", "MTEXT text frames");
                }
                if (text.Columns == null) return;
                if (text.Columns.Storage == MTextColumnStorage.Embedded)
                    this.Minimum(DxfVersion.AutoCad2018, "MTEXT_EMBEDDED_COLUMNS_PROFILE", text, "Columns.Storage", "Embedded MTEXT columns");
                else if (text.Columns.Storage == MTextColumnStorage.Direct)
                    this.Minimum(DxfVersion.AutoCad2007, "MTEXT_DIRECT_COLUMNS_PROFILE", text, "Columns.Storage", "Direct MTEXT columns");
                else if (text.Columns.Storage == MTextColumnStorage.LegacyLinked && this.target >= DxfVersion.AutoCad2018)
                    this.Add("MTEXT_LINKED_COLUMNS_PROFILE", text, "Columns.Storage", "Legacy linked MTEXT columns require a pre-R2018 profile and explicit conversion for R2018.");
            }
            private void Hatch(Hatch hatch)
            {
                if (hatch.Pattern is HatchGradientPattern && this.target == DxfVersion.AutoCad2000)
                    this.Add("HATCH_GRADIENT_OMITTED", hatch, "Pattern", "The R2000 writer omits the gradient packet and retains only the base hatch fill.", DxfVersionCompatibilityKind.DataOmission);
                for (int i = 0; i < hatch.BoundaryPaths.Count; i++)
                    for (int j = 0; j < hatch.BoundaryPaths[i].Edges.Count; j++)
                    {
                        if (!(hatch.BoundaryPaths[i].Edges[j] is HatchBoundaryPath.Spline spline)) continue;
                        string path = "BoundaryPaths[" + i + "].Edges[" + j + "].";
                        if (spline.FitPoints.Count != 0) this.Minimum(DxfVersion.AutoCad2010, "HATCH_SPLINE_FIT_PROFILE", hatch, path + "FitPoints", "HATCH spline fit data");
                        if (spline.StartTangent.HasValue) this.Minimum(DxfVersion.AutoCad2010, "HATCH_SPLINE_FIT_PROFILE", hatch, path + "StartTangent", "HATCH spline start-tangent data");
                        if (spline.EndTangent.HasValue) this.Minimum(DxfVersion.AutoCad2010, "HATCH_SPLINE_FIT_PROFILE", hatch, path + "EndTangent", "HATCH spline end-tangent data");
                    }
            }
            private void MultiLeader(MultiLeader leader)
            {
                this.Minimum(DxfVersion.AutoCad2007, "MULTILEADER_PROFILE", leader, "Type", "MULTILEADER entities");
                var properties = leader.Properties;
                if (properties.TextAttachmentDirection.HasValue) this.Minimum(DxfVersion.AutoCad2010, "MULTILEADER_OPTIONAL_FIELD_PROFILE", leader, "Properties.TextAttachmentDirection", "Stored MULTILEADER group 271");
                if (properties.TextBottomAttachment.HasValue) this.Minimum(DxfVersion.AutoCad2010, "MULTILEADER_OPTIONAL_FIELD_PROFILE", leader, "Properties.TextBottomAttachment", "Stored MULTILEADER group 272");
                if (properties.TextTopAttachment.HasValue) this.Minimum(DxfVersion.AutoCad2010, "MULTILEADER_OPTIONAL_FIELD_PROFILE", leader, "Properties.TextTopAttachment", "Stored MULTILEADER group 273");
                if (properties.LeaderExtendToText.HasValue) this.Minimum(DxfVersion.AutoCad2013, "MULTILEADER_OPTIONAL_FIELD_PROFILE", leader, "Properties.LeaderExtendToText", "Stored MULTILEADER group 295");
                if (leader.Context.TopAttachment.HasValue) this.Minimum(DxfVersion.AutoCad2010, "MULTILEADER_OPTIONAL_FIELD_PROFILE", leader, "Context.TopAttachment", "Stored MULTILEADER context group 272");
                if (leader.Context.BottomAttachment.HasValue) this.Minimum(DxfVersion.AutoCad2010, "MULTILEADER_OPTIONAL_FIELD_PROFILE", leader, "Context.BottomAttachment", "Stored MULTILEADER context group 273");
                for (int i = 0; i < leader.Context.Leaders.Count; i++)
                    if (leader.Context.Leaders[i].AttachmentDirection.HasValue)
                        this.Minimum(DxfVersion.AutoCad2010, "MULTILEADER_OPTIONAL_FIELD_PROFILE", leader, "Context.Leaders[" + i + "].AttachmentDirection", "Stored MULTILEADER branch group 271");
            }
            private void SunSlot(DxfObject host)
            {
                if (SunReferences.IsPresent(host)) this.Minimum(host is View ? DxfVersion.AutoCad2010 : DxfVersion.AutoCad2007,
                    "SUN_OWNER_PROFILE", host, "Sun", "The stored SUN owner slot (including explicit null)");
            }
            private void RegisteredObject(DxfObject item)
            {
                if (item is Polyline3DRecord vertex) this.SourceProfile(vertex, vertex.SourceVersion);
                else if (item is PolygonMeshRecord meshVertex) this.SourceProfile(meshVertex, meshVertex.SourceVersion);
                else if (item is PolyfaceMeshRecord faceVertex) this.SourceProfile(faceVertex, faceVertex.SourceVersion);
                else if (item is DxfTableStyle tableStyle) this.SourceProfile(tableStyle, tableStyle.SourceVersion);
                else if (item is DxfStoredTableContent content) this.SourceProfile(content, content.SourceVersion);
                else if (item is DxfStoredTableGeometry geometry) this.SourceProfile(geometry, geometry.SourceVersion);
                else if (item is DxfStoredCellStyleMap map) this.SourceProfile(map, map.SourceVersion);
                else if (item is DxfStoredField field) this.SourceProfile(field, field.SourceVersion);
                else if (item is DxfStoredDimAssoc association) this.SourceProfile(association, association.SourceVersion);
                else if (item is DxfStoredSectionManager manager) this.SourceProfile(manager, manager.SourceVersion);
                else if (item is DxfStoredSunStudy study) this.SourceProfile(study, study.SourceVersion);
                else if (item is DxfDataTable) this.Minimum(DxfVersion.AutoCad2004, "DATATABLE_PROFILE", item, "StoredVersion", "Typed DATATABLE data");
                else if (item is DxfGeoData) this.Minimum(DxfVersion.AutoCad2010, "GEODATA_PROFILE", item, "Version", "Typed GEODATA version 2");
                else if (item is DxfLightList) this.Minimum(DxfVersion.AutoCad2007, "LIGHTLIST_PROFILE", item, "StoredVersion", "Typed LIGHTLIST data");
                else if (item is DxfSun) this.Minimum(DxfVersion.AutoCad2007, "SUN_PROFILE", item, "StoredVersion", "Typed SUN data");
                else if (item is DxfMLeaderStyle) this.Minimum(DxfVersion.AutoCad2007, "MLEADERSTYLE_PROFILE", item, "Properties", "Typed MLEADERSTYLE data");
                else if (item is DxfSectionSettings) this.Minimum(DxfVersion.AutoCad2007, "SECTIONSETTINGS_PROFILE", item, "TypeSettings", "Typed SECTIONSETTINGS data");
                else if (item is DxfSortentsTable) this.Minimum(DxfVersion.AutoCad2004, "SORTENTSTABLE_PROFILE", item, "Entries", "SORTENTSTABLE data");
                else if (item is DxfPlotSettingsObject page && page.Settings.ShadePlotObject != null)
                    this.Minimum(DxfVersion.AutoCad2007, "PLOT_SHADE_REFERENCE_PROFILE", item, "Settings.ShadePlotObject", "A page-setup shade-plot reference");
            }
        }
    }
}
