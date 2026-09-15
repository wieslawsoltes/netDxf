using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>A retained VERTEX or SEQEND belonging to a loaded ordinary legacy 2D polyline.</summary>
    /// <remarks>
    /// Geometry remains in the existing Polyline2DVertex objects in Polyline2D.Vertexes. These records expose identity and common metadata;
    /// they are not independently insertable entities. Stored optional fields and private packets
    /// survive without interpreting private application data. The initial implementation is source bound.
    /// </remarks>
    public sealed class Polyline2DRecord : DxfObject
    {
        internal readonly List<DxfTag> Tags;
        internal readonly Dictionary<int, DxfObject> Resources = new Dictionary<int, DxfObject>();
        internal readonly Dictionary<int, string> OriginalResourceNames = new Dictionary<int, string>();
        internal readonly Dictionary<short, int> Coordinates = new Dictionary<short, int>();
        internal readonly Dictionary<int, int> MetadataGroups = new Dictionary<int, int>();
        internal readonly List<string> ReactorHandles = new List<string>();
        internal readonly List<DxfObject> OriginalReactors = new List<DxfObject>();
        internal string ExtensionHandle;
        internal DxfObject OriginalExtension;
        internal string SourceOwner;
        internal int CommonEnd;
        internal int XDataStart;
        internal Vector3 Position;
        internal Polyline2DVertex StoredVertex;
        internal int GeometryEnd;
        internal readonly Dictionary<short, int> GeometryIndices = new Dictionary<short, int>();
        /// <summary>Gets the existing point model for this VERTEX, or null for SEQEND.</summary>
        public Polyline2DVertex Vertex { get { return this.StoredVertex; } }

        internal Dictionary<short, DxfTag> GeometryTags()
        {
            var result = new Dictionary<short, DxfTag>();
            if (this.IsSequenceEnd) return result;
            result.Add(10, new DxfTag(10, this.Vertex.Position.X));
            result.Add(20, new DxfTag(20, this.Vertex.Position.Y));
            result.Add(30, new DxfTag(30, 0.0));
            if (this.Vertex.StartWidthOverride.HasValue) result.Add(40, new DxfTag(40, this.Vertex.StartWidthOverride.Value));
            if (this.Vertex.EndWidthOverride.HasValue) result.Add(41, new DxfTag(41, this.Vertex.EndWidthOverride.Value));
            if (this.GeometryIndices.ContainsKey(42) || this.Vertex.Bulge != 0) result.Add(42, new DxfTag(42, this.Vertex.Bulge));
            if (this.Vertex.VertexIdentifier.HasValue) result.Add(91, new DxfTag(91, this.Vertex.VertexIdentifier.Value));
            return result;
        }
        internal DxfDocument SourceDocument;
        internal int IdentityIndex;
        internal int OwnerIndex;
        internal bool HasPrivateData;

        internal Polyline2DRecord(string codeName, List<DxfTag> tags) : base(codeName)
        { this.Tags = tags; this.XDataStart = tags.Count; }

        /// <summary>Gets the DXF profile in which this stored record was loaded.</summary>
        /// <remarks>Stored optional and private packets require the same profile on output or adoption.</remarks>
        public DxfVersion SourceVersion { get; internal set; }
        /// <summary>Gets the stored layer identity, or null when group 8 was absent.</summary>
        public Layer Layer { get { return this.Resources.Values.OfType<Layer>().FirstOrDefault(); } }
        /// <summary>Gets the stored linetype identity, or null when group 6 was absent.</summary>
        public Linetype Linetype { get { return this.Resources.Values.OfType<Linetype>().FirstOrDefault(); } }
        /// <summary>Gets whether this record terminates the vertex sequence.</summary>
        public bool IsSequenceEnd { get { return this.CodeName == DxfObjectCode.EndSequence; } }
        /// <summary>Gets whether the stored VERTEX group 330 names the containing block record.</summary>
        /// <remarks>The structural Owner is always the containing Polyline2D. This observed producer form is retained independently.</remarks>
        public bool UsesBlockRecordOwner { get; internal set; }
        /// <summary>Gets the current identity written in ordinary group 330.</summary>
        /// <remarks>For the block-record form this follows an explicit move of the containing legacy 2D polyline to another block.</remarks>
        public DxfObject StoredOwner
        { get { return this.UsesBlockRecordOwner ? ((this.Owner as Polyline2D)?.Owner as netDxf.Blocks.Block)?.Record : this.Owner; } }
        internal IEnumerable<DxfObject> References { get { return this.Resources.Values; } }
        internal IEnumerable<DxfTag> OpaqueHandleTags
        {
            get
            {
                for (int i = 0; i < this.XDataStart; i++)
                {
                    if (this.MetadataGroups.TryGetValue(i, out int last)) { i = last; continue; }
                    if (i != this.IdentityIndex && i != this.OwnerIndex && !this.Resources.ContainsKey(i)
                        && netDxf.Objects.DxfObjectDatabase.IsReference(this.Tags[i])) yield return this.Tags[i];
                }
            }
        }

        internal void Validate(DxfDocument document, Polyline2D owner, bool registered)
        {
            if (document.DrawingVariables.AcadVer != this.SourceVersion)
                throw new NotSupportedException("Converting retained VERTEX/SEQEND records to another DXF profile requires schema regeneration.");
            if (this.SourceDocument != null && !ReferenceEquals(this.SourceDocument, document) || !ReferenceEquals(this.Owner, owner))
                throw new InvalidOperationException("A retained legacy 2D polyline record must stay with its source legacy 2D polyline and document.");
            DxfObject current = this.Handle == null ? null : document.GetObjectByHandle(this.Handle);
            if (registered ? !ReferenceEquals(current, this) : current != null && !ReferenceEquals(current, this))
                throw new InvalidOperationException("A retained legacy 2D polyline record has inconsistent registration.");
            if (registered && (this.StoredOwner == null || !ReferenceEquals(document.GetObjectByHandle(this.StoredOwner.Handle), this.StoredOwner)))
                throw new InvalidOperationException("A retained legacy 2D polyline record has an invalid stored owner.");
            if (this.SourceDocument == null && !registered) return;
            foreach (DxfObject target in this.References)
                if (!ReferenceEquals(document.GetObjectByHandle(target.Handle), target))
                    throw new InvalidOperationException("A retained legacy 2D polyline record resource is outside its source document.");
        }
        internal bool CanClone()
        {
            return !this.HasPrivateData && this.ExtensionDictionary == null && this.PersistentReactors.Count == 0
                && this.Resources.Values.All(resource => resource is Layer || resource is Linetype)
                && !this.XData.Values.SelectMany(data => data.XDataRecord).Any(tag => tag.Code == XDataCode.DatabaseHandle
                    && ulong.Parse((string)tag.Value, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture) != 0);
        }
        internal int TopologyTagCount()
        {
            int count = this.XDataStart + this.GeometryTags().Count - this.GeometryIndices.Count;
            bool extension = false, reactors = false;
            foreach (var group in this.MetadataGroups)
            {
                bool isExtension = (string)this.Tags[group.Key].Value == "{ACAD_XDICTIONARY";
                bool unchanged = isExtension ? ReferenceEquals(this.ExtensionDictionary, this.OriginalExtension)
                    : this.PersistentReactors.SequenceEqual(this.OriginalReactors)
                        && this.ReactorHandles.Where(handle => handle != "0").SequenceEqual(this.PersistentReactors.Select(target => target.Handle));
                if (!unchanged)
                {
                    count -= group.Value - group.Key + 1;
                    count += isExtension ? this.ExtensionDictionary == null ? 0 : 3
                        : this.PersistentReactors.Count == 0 ? 0 : this.PersistentReactors.Count + 2;
                }
                if (isExtension) extension = true; else reactors = true;
            }
            if (!extension && this.ExtensionDictionary != null) count += 3;
            if (!reactors && this.PersistentReactors.Count != 0) count += this.PersistentReactors.Count + 2;
            foreach (XData data in this.XData.Values) count += 1 + data.XDataRecord.Count;
            return count;
        }
        internal Polyline2DRecord CopyForClone(Polyline2DVertex vertex = null)
        {
            var result = new Polyline2DRecord(this.CodeName, new List<DxfTag>(this.Tags))
            {
                SourceOwner = this.SourceOwner, CommonEnd = this.CommonEnd, XDataStart = this.XDataStart,
                Position = this.Position, StoredVertex = vertex, GeometryEnd = this.GeometryEnd, IdentityIndex = this.IdentityIndex, OwnerIndex = this.OwnerIndex,
                ExtensionHandle = this.ExtensionHandle, UsesBlockRecordOwner = this.UsesBlockRecordOwner, SourceVersion = this.SourceVersion
            };
            foreach (var pair in this.GeometryIndices) result.GeometryIndices.Add(pair.Key, pair.Value);
            foreach (var pair in this.Coordinates) result.Coordinates.Add(pair.Key, pair.Value);
            foreach (var pair in this.MetadataGroups) result.MetadataGroups.Add(pair.Key, pair.Value);
            result.ReactorHandles.AddRange(this.ReactorHandles);
            foreach (var pair in this.OriginalResourceNames) result.OriginalResourceNames.Add(pair.Key, pair.Value);
            foreach (var pair in this.Resources) result.Resources.Add(pair.Key, pair.Value is Layer layer ? (DxfObject)layer.Clone() : ((Linetype)pair.Value).Clone() as DxfObject);
            foreach (XData data in this.XData.Values) result.XData.Add((XData)data.Clone());
            return result;
        }
    }
}
