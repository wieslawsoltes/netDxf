using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>A retained VERTEX or SEQEND belonging to a loaded polyface mesh.</summary>
    /// <remarks>
    /// Geometry remains in PolyfaceMesh.Vertexes. These records expose identity and common metadata;
    /// they are not independently insertable entities. Stored optional fields and private packets
    /// survive without interpreting private application data. The initial implementation is source bound.
    /// </remarks>
    public sealed class PolyfaceMeshRecord : DxfObject
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
        internal DxfDocument SourceDocument;
        internal int IdentityIndex;
        internal int OwnerIndex;
        internal bool HasPrivateData;
        internal int CoordinateIndex = -1;
        internal int FaceIndex = -1;
        internal readonly Dictionary<short, int> FaceSlots = new Dictionary<short, int>();
        internal short[] OriginalIndexes;
        internal PolyfaceMeshFace StoredFace;
        internal AciColor OriginalColor;
        internal int FaceLayerIndex = -1;
        internal int FaceCommonEnd = -1;
        internal readonly HashSet<int> FaceColorIndices = new HashSet<int>();
        /// <summary>Gets whether this VERTEX describes a face through signed coordinate indices.</summary>
        public bool IsFaceRecord { get; internal set; }
        /// <summary>Gets the existing face model for a face record, or null for a coordinate or terminator.</summary>
        public PolyfaceMeshFace Face { get { return this.StoredFace; } }
        internal bool FaceColorUnchanged
        { get { return this.Face == null || this.Face.Color == null && this.OriginalColor == null
            || this.Face.Color != null && this.OriginalColor != null && this.Face.Color.Index == this.OriginalColor.Index
                && this.Face.Color.UseTrueColor == this.OriginalColor.UseTrueColor && this.Face.Color.Equals(this.OriginalColor); } }
        internal bool FaceIndexesUnchanged { get { return this.Face == null || this.Face.VertexIndexes.SequenceEqual(this.OriginalIndexes); } }

        internal PolyfaceMeshRecord(string codeName, List<DxfTag> tags) : base(codeName)
        { this.Tags = tags; this.XDataStart = tags.Count; }

        /// <summary>Gets the DXF profile in which this stored record was loaded.</summary>
        /// <remarks>Stored optional and private packets require the same profile on output or adoption.</remarks>
        public DxfVersion SourceVersion { get; internal set; }
        /// <summary>Gets the stored layer identity, or null when group 8 was absent.</summary>
        public Layer Layer { get { return this.Face != null ? this.Face.Layer : this.Resources.Values.OfType<Layer>().FirstOrDefault(); } }
        /// <summary>Gets the stored linetype identity, or null when group 6 was absent.</summary>
        public Linetype Linetype { get { return this.Resources.Values.OfType<Linetype>().FirstOrDefault(); } }
        /// <summary>Gets whether this record terminates the vertex sequence.</summary>
        public bool IsSequenceEnd { get { return this.CodeName == DxfObjectCode.EndSequence; } }
        /// <summary>Gets whether the stored VERTEX group 330 names the containing block record.</summary>
        /// <remarks>The structural Owner is always the containing PolyfaceMesh. This observed producer form is retained independently.</remarks>
        public bool UsesBlockRecordOwner { get; internal set; }
        /// <summary>Gets the current identity written in ordinary group 330.</summary>
        /// <remarks>For the block-record form this follows an explicit move of the containing polyface mesh to another block.</remarks>
        public DxfObject StoredOwner
        { get { return this.UsesBlockRecordOwner ? ((this.Owner as PolyfaceMesh)?.Owner as netDxf.Blocks.Block)?.Record : this.Owner; } }
        internal IEnumerable<DxfObject> References
        { get { return this.Face == null ? this.Resources.Values : this.Resources.Values.Where(item => !(item is Layer))
                .Concat(this.Face.Layer == null ? Enumerable.Empty<DxfObject>() : new DxfObject[] { this.Face.Layer }); } }
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

        internal void Validate(DxfDocument document, PolyfaceMesh owner, bool registered)
        {
            if (document.DrawingVariables.AcadVer != this.SourceVersion)
                throw new NotSupportedException("Converting retained VERTEX/SEQEND records to another DXF profile requires schema regeneration.");
            if (this.SourceDocument != null && !ReferenceEquals(this.SourceDocument, document) || !ReferenceEquals(this.Owner, owner))
                throw new InvalidOperationException("A retained polyface mesh record must stay with its source polyface mesh and document.");
            DxfObject current = this.Handle == null ? null : document.GetObjectByHandle(this.Handle);
            if (registered ? !ReferenceEquals(current, this) : current != null && !ReferenceEquals(current, this))
                throw new InvalidOperationException("A retained polyface mesh record has inconsistent registration.");
            if (registered && (this.StoredOwner == null || !ReferenceEquals(document.GetObjectByHandle(this.StoredOwner.Handle), this.StoredOwner)))
                throw new InvalidOperationException("A retained polyface mesh record has an invalid stored owner.");
            if (this.SourceDocument == null && !registered) return;
            foreach (DxfObject target in this.References)
                if (!ReferenceEquals(document.GetObjectByHandle(target.Handle), target))
                    throw new InvalidOperationException("A retained polyface mesh record resource is outside its source document.");
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
            int count = this.XDataStart;
            if (this.Face != null)
            {
                count += (this.Face.Layer == null ? 0 : 1) - (this.FaceLayerIndex < 0 ? 0 : 1);
                if (!this.FaceColorUnchanged)
                    count += (this.Face.Color == null ? 0 : this.Face.Color.UseTrueColor ? 2 : 1) - this.FaceColorIndices.Count;
            }
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
        internal PolyfaceMeshRecord CopyForClone(PolyfaceMeshFace face = null)
        {
            var result = new PolyfaceMeshRecord(this.CodeName, new List<DxfTag>(this.Tags))
            {
                SourceOwner = this.SourceOwner, CommonEnd = this.CommonEnd, XDataStart = this.XDataStart,
                Position = this.Position, IdentityIndex = this.IdentityIndex, OwnerIndex = this.OwnerIndex,
                ExtensionHandle = this.ExtensionHandle, UsesBlockRecordOwner = this.UsesBlockRecordOwner, SourceVersion = this.SourceVersion,
                CoordinateIndex = this.CoordinateIndex, FaceIndex = this.FaceIndex, IsFaceRecord = this.IsFaceRecord,
                FaceLayerIndex = this.FaceLayerIndex, FaceCommonEnd = this.FaceCommonEnd,
                OriginalIndexes = this.OriginalIndexes == null ? null : (short[])this.OriginalIndexes.Clone(), StoredFace = face,
                OriginalColor = this.OriginalColor == null ? null : (AciColor)this.OriginalColor.Clone()
            };
            foreach (int index in this.FaceColorIndices) result.FaceColorIndices.Add(index);
            foreach (var pair in this.FaceSlots) result.FaceSlots.Add(pair.Key, pair.Value);
            foreach (var pair in this.Coordinates) result.Coordinates.Add(pair.Key, pair.Value);
            foreach (var pair in this.MetadataGroups) result.MetadataGroups.Add(pair.Key, pair.Value);
            result.ReactorHandles.AddRange(this.ReactorHandles);
            foreach (var pair in this.OriginalResourceNames) result.OriginalResourceNames.Add(pair.Key, pair.Value);
            foreach (var pair in this.Resources)
            {
                if (pair.Value is Layer layer)
                {
                    Layer current = face == null ? (Layer)layer.Clone() : face.Layer;
                    if (current != null) result.Resources.Add(pair.Key, current);
                }
                else result.Resources.Add(pair.Key, (Linetype)((Linetype)pair.Value).Clone());
            }
            foreach (XData data in this.XData.Values) result.XData.Add((XData)data.Clone());
            return result;
        }
    }
}
