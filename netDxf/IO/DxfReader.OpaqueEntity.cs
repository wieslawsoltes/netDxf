// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using netDxf.Entities;
using netDxf.Tables;
namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<DxfOpaqueEntity> opaqueEntities = new List<DxfOpaqueEntity>();
        private readonly HashSet<string> unqualifiedOpaqueClasses = new HashSet<string>(StringComparer.Ordinal);
        private bool hasDiscardedAcdsData;
        private int opaqueEntityTags;
        private static readonly HashSet<string> OrdinaryEntityNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "3DFACE",
            "3DSOLID",
            "ACAD_TABLE",
            "ARC",
            "ARC_DIMENSION",
            "ATTDEF",
            "BODY",
            "CIRCLE",
            "DGNUNDERLAY",
            "DIMENSION",
            "DWFUNDERLAY",
            "ELLIPSE",
            "HATCH",
            "HELIX",
            "IMAGE",
            "INSERT",
            "LEADER",
            "LIGHT",
            "LINE",
            "LWPOLYLINE",
            "MESH",
            "MLEADER",
            "MLINE",
            "MTEXT",
            "MULTILEADER",
            "OLE2FRAME",
            "OLEFRAME",
            "PDFUNDERLAY",
            "POINT",
            "POLYLINE",
            "RAY",
            "REGION",
            "SECTION",
            "SECTIONOBJECT",
            "SHAPE",
            "SOLID",
            "SPLINE",
            "TEXT",
            "TOLERANCE",
            "TRACE",
            "VIEWPORT",
            "WIPEOUT",
            "XLINE",
        };
        private static bool IsOpaqueEntityCandidate(string name)
        {
            if (OrdinaryEntityNames.Contains(name)) return false;
            if (OrdinaryEntityNames.Any(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported casing of a known entity name.");
            return true;
        }
        private static bool OpaqueExcludedSubclass(string name)
        {
            return new[] { "AcDbSequenceEnd", "AcDbVertex", "AcDb2dVertex", "AcDb3dPolylineVertex", "AcDbPolygonMeshVertex",
                "AcDbPolyFaceMeshVertex", "AcDbFaceRecord", "AcDbAttribute", "AcDbAttributeDefinition", "AcDbBlockBegin", "AcDbBlockEnd",
                "AcDbBlockReference", "AcDbMInsertBlock", "AcDbDimension", "AcDbModelerGeometry", "AcDbSurface", "AcDbProxyEntity" }
                .Any(excluded => string.Equals(name, excluded, StringComparison.OrdinalIgnoreCase));
        }
        private DxfOpaqueEntity ReadOpaqueEntity(bool isBlockEntity)
        {
            string name = this.chunk.ReadString();
            if (new[] { "VERTEX", "ATTRIB", "SEQEND", "BLOCK", "ENDBLK", "SECTION", "ENDSEC", "EOF", "TABLE", "ENDTAB", "CLASS",
                "ACAD_PROXY_ENTITY", "ACAD_PROXY_OBJECT", "ACAD_ZOMBIE_ENTITY", "SURFACE", "PLANESURFACE", "EXTRUDEDSURFACE", "LOFTEDSURFACE", "REVOLVEDSURFACE", "SWEPTSURFACE" }
                .Any(excluded => string.Equals(name, excluded, StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException("Unsupported standalone, aggregate or proxy entity: " + name);
            SourceRecordIdentity source = this.CurrentSourceRecord;
            var tags = new List<DxfTag> { new DxfTag(0, name) };
            var observer = (DatabaseMetadataReader)this.chunk;
            observer.SetSkipComments(false);
            try
            {
                this.chunk.Next();
                while (this.chunk.Code != 0)
                {
                    if (tags.Count >= 65536 || ++this.opaqueEntityTags > 1048576)
                        throw new InvalidDataException("Unknown entity tags exceed the admission budget.");
                    tags.Add(new DxfTag(this.chunk.Code, this.chunk.Value)); this.chunk.Next();
                }
            }
            finally { observer.SetSkipComments(true); }
            string handle = null, owner = null; int common = -1, body = -1;
            var commonFields = new List<int>(); var references = new List<int>(); var owners = new List<int>();
            var singleton = new HashSet<short>(); var metadata = new HashSet<string>();
            int xdata = tags.Count;
            for (int i = 1; i < tags.Count; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 999) continue;
                if (tag.Code == 102)
                {
                    int end = OpaqueEntityGroupEnd(tags, i);
                    string group = (string)tag.Value;
                    if (common < 0 && (group == "{ACAD_XDICTIONARY" || group == "{ACAD_REACTORS"))
                    {
                        if (!metadata.Add(group)) throw new InvalidDataException("Repeated unknown entity metadata group.");
                        int count = 0;
                        for (int at = i + 1; at < end; at++)
                        {
                            if (tags[at].Code == 999) continue;
                            if (tags[at].Code != (group == "{ACAD_XDICTIONARY" ? 360 : 330))
                                throw new InvalidDataException("Unsupported unknown entity metadata group framing.");
                            references.Add(at); count++;
                            if (group == "{ACAD_XDICTIONARY") owners.Add(at);
                        }
                        if (group == "{ACAD_XDICTIONARY" && count != 1) throw new InvalidDataException("Extension metadata requires one target.");
                    }
                    i = end; continue;
                }
                if (tag.Code == 66 || tag.Code == 101)
                    throw new NotSupportedException("Unknown aggregate or embedded entity framing is unsupported.");
                if (tag.Code == 1001)
                {
                    if (body < 0) throw new InvalidDataException("Unknown entity XData must follow private subclasses.");
                    xdata = i; break;
                }
                if (tag.Code >= 1000) throw new InvalidDataException("Unknown entity XData requires an APPID marker.");
                if (tag.Code == 100)
                {
                    string subclass = (string)tag.Value;
                    if (string.IsNullOrWhiteSpace(subclass)) throw new InvalidDataException("Empty unknown entity subclass marker.");
                    if (OpaqueExcludedSubclass(subclass)) throw new NotSupportedException("Unsupported unknown entity subclass: " + subclass);
                    if (common < 0)
                    {
                        if (subclass != "AcDbEntity") throw new InvalidDataException("Unknown entity requires AcDbEntity first.");
                        common = i;
                    }
                    else
                    {
                        if (string.Equals(subclass, "AcDbEntity", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Repeated AcDbEntity frame.");
                        if (body < 0) body = i;
                    }
                    continue;
                }
                if (common < 0)
                {
                    if (tag.Code == 5)
                    {
                        if (handle != null) throw new InvalidDataException("Repeated unknown entity identity.");
                        handle = DxfOpaqueEntity.CanonicalHandle((string)tag.Value);
                    }
                    else if (tag.Code == 330)
                    {
                        if (owner != null) throw new InvalidDataException("Repeated unknown entity block owner.");
                        owner = DxfOpaqueEntity.CanonicalHandle((string)tag.Value); references.Add(i);
                    }
                    else if (tag.HandleKind == DxfHandleKind.SoftPointer || tag.HandleKind == DxfHandleKind.HardPointer) references.Add(i);
                    else if (tag.HandleKind == DxfHandleKind.HardOwner || tag.HandleKind == DxfHandleKind.SoftOwner)
                        throw new NotSupportedException("Unsupported unknown entity header ownership.");
                }
                else
                {
                    if (body < 0)
                    {
                        if (new short[] { 8, 6, 62, 420, 370, 48, 60, 440, 430, 284, 67, 410, 347, 390 }.Contains(tag.Code)
                            && !singleton.Add(tag.Code)) throw new InvalidDataException("Repeated unknown entity common singleton.");
                        commonFields.Add(i);
                    }
                    if (tag.HandleKind == DxfHandleKind.SoftPointer || tag.HandleKind == DxfHandleKind.HardPointer
                        || tag.HandleKind == DxfHandleKind.SoftOwner || tag.HandleKind == DxfHandleKind.HardOwner)
                    {
                        references.Add(i);
                        if (tag.HandleKind == DxfHandleKind.SoftOwner || tag.HandleKind == DxfHandleKind.HardOwner) owners.Add(i);
                    }
                }
            }
            if (handle == null || handle == "0" || owner == null || owner == "0" || common < 0 || body < 0 || !singleton.Contains(8)
                || source == null || source.Handle != ulong.Parse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture))
                throw new InvalidDataException("Unknown entity requires one physical identity, owner, layer and complete subclass envelope.");
            if (source.Ambiguous) throw new FormatException("A retained DXF object has an ambiguous physical source identity: " + handle);
            var entity = new DxfOpaqueEntity(this.doc, name, tags, handle)
            { SourceOwnerHandle = owner, CommonEnd = body, XDataStart = xdata };
            entity.CommonFields.UnionWith(commonFields); entity.ReferenceIndices.AddRange(references); entity.OwnerIndices.AddRange(owners);
            this.ReadOpaqueCommon(entity, tags, commonFields);
            if (xdata < tags.Count)
            {
                this.ReadDatabaseXData(entity, tags.Where((tag, index) => index >= xdata && tag.Code != 999).ToList(), 0);
            }
            this.RecordSourceObject(entity, source);
            if (long.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long identity) && identity >= this.doc.NumHandles && identity < long.MaxValue)
                this.doc.NumHandles = identity + 1;
            this.opaqueEntities.Add(entity);
            if (!isBlockEntity) this.entityList.Add(entity, owner);
            return entity;
        }
        private static int OpaqueEntityGroupEnd(List<DxfTag> tags, int start)
        {
            if (!((string)tags[start].Value).StartsWith("{", StringComparison.Ordinal)) throw new InvalidDataException("Invalid unknown entity control group.");
            int depth = 1;
            for (int i = start + 1; i < tags.Count; i++)
            {
                if (tags[i].Code != 102) continue;
                string value = (string)tags[i].Value;
                if (value == "}") { if (--depth == 0) return i; }
                else if (value.StartsWith("{", StringComparison.Ordinal)) { if (++depth > 32) throw new InvalidDataException("Unknown entity control groups exceed their depth budget."); }
                else throw new InvalidDataException("Invalid unknown entity control delimiter.");
            }
            throw new InvalidDataException("Unterminated unknown entity control group.");
        }
        private void ReadOpaqueCommon(DxfOpaqueEntity entity, List<DxfTag> tags, List<int> indices)
        {
            bool trueColor = false, graphicsSeen = false; long? length = null; var graphics = new List<byte>();
            foreach (int index in indices)
            {
                DxfTag tag = tags[index];
                switch (tag.Code)
                {
                    case 8:
                        string layer = this.DecodeEncodedNonAsciiCharacters((string)tag.Value);
                        if (!this.doc.Layers.TryGetValue(layer, out Layer actualLayer) || !ReferenceEquals(this.GetObjectBySourceHandle(actualLayer.Handle), actualLayer))
                            throw new InvalidDataException("Unknown entity requires its actual source LAYER.");
                        entity.Layer = actualLayer; break;
                    case 6:
                        string ltype = this.DecodeEncodedNonAsciiCharacters((string)tag.Value);
                        if (!this.doc.Linetypes.TryGetValue(ltype, out Linetype actualLtype) || !ReferenceEquals(this.GetObjectBySourceHandle(actualLtype.Handle), actualLtype))
                            throw new InvalidDataException("Unknown entity requires its actual source LTYPE.");
                        entity.Linetype = actualLtype; break;
                    case 62:
                        if ((short)tag.Value < -256 || (short)tag.Value > 256) throw new InvalidDataException("Invalid common ACI color.");
                        if (!trueColor) entity.Color = AciColor.FromCadIndex((short)tag.Value); break;
                    case 420:
                        entity.Color = AciColor.FromTrueColor((int)tag.Value); trueColor = true; break;
                    case 370:
                        if (!Enum.IsDefined(typeof(Lineweight), (Lineweight)(short)tag.Value)) throw new InvalidDataException("Invalid common lineweight.");
                        entity.Lineweight = (Lineweight)(short)tag.Value; break;
                    case 48: entity.LinetypeScale = (double)tag.Value; break;
                    case 60:
                        if ((short)tag.Value != 0 && (short)tag.Value != 1) throw new InvalidDataException("Invalid common visibility.");
                        entity.IsVisible = (short)tag.Value == 0; break;
                    case 67:
                        if ((short)tag.Value != 0 && (short)tag.Value != 1) throw new InvalidDataException("Invalid common space flag."); break;
                    case 440: entity.Transparency = Transparency.FromAlphaValue((int)tag.Value); break;
                    case 430:
                        if (entity.SourceVersion < netDxf.Header.DxfVersion.AutoCad2004) throw new InvalidDataException("Common color names require DXF 2004.");
                        entity.ColorName = this.DecodeEncodedNonAsciiCharacters((string)tag.Value); break;
                    case 284:
                        if (entity.SourceVersion < netDxf.Header.DxfVersion.AutoCad2007) throw new InvalidDataException("Common shadow mode requires DXF 2007.");
                        entity.ShadowMode = (EntityShadowMode)(short)tag.Value; break;
                    case 92:
                    case 160:
                        if (length.HasValue || tag.Code == 160 && entity.SourceVersion < netDxf.Header.DxfVersion.AutoCad2010) throw new InvalidDataException("Invalid common graphics length.");
                        length = tag.Code == 160 ? (long)tag.Value : (int)tag.Value;
                        if (length < 0 || length > EntityObject.MaximumProxyGraphicsBytes) throw new InvalidDataException("Common graphics length exceeds its budget."); break;
                    case 310:
                        graphicsSeen = true;
                        byte[] bytes = (byte[])tag.Value;
                        if (bytes.Length > 128 || graphics.Count + bytes.Length > EntityObject.MaximumProxyGraphicsBytes) throw new InvalidDataException("Common graphics exceed their budget.");
                        graphics.AddRange(bytes); break;
                }
            }
            if (length.HasValue && length != graphics.Count || !length.HasValue && graphicsSeen) throw new InvalidDataException("Common graphics length mismatch.");
            if (length.HasValue) entity.ProxyGraphics = graphics.ToArray();
        }
        private void ResolveOpaqueEntities()
        {
            if (this.opaqueEntities.Count != 0 && this.hasDiscardedAcdsData) throw new NotSupportedException("Unknown entities with discarded ACDSDATA require raw preservation.");
            foreach (DxfOpaqueEntity entity in this.opaqueEntities)
            {
                if (this.unqualifiedOpaqueClasses.Contains(entity.CodeName)) throw new NotSupportedException("Unknown entity CLASS has unqualified source fields.");
                DxfClass definition = this.doc.Classes.Contains(entity.CodeName) ? this.doc.Classes[entity.CodeName] : null;
                if (definition != null && !definition.IsEntity) throw new InvalidDataException("Unknown entity CLASS must declare an entity.");
                entity.Resolve(handle => this.GetObjectBySourceHandle(handle, true), definition);
            }
        }
    }
}
