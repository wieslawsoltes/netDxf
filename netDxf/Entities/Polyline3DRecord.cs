using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>A retained VERTEX or SEQEND belonging to a loaded ordinary 3D polyline.</summary>
    /// <remarks>
    /// Geometry remains in Polyline3D.Vertexes. These records expose identity and common metadata;
    /// they are not independently insertable entities. Stored optional fields and private packets
    /// survive without interpreting private application data. The initial implementation is source bound.
    /// </remarks>
    public sealed class Polyline3DRecord : DxfObject
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

        internal Polyline3DRecord(string codeName, List<DxfTag> tags) : base(codeName)
        { this.Tags = tags; this.XDataStart = tags.Count; }

        /// <summary>Gets the stored layer identity, or null when group 8 was absent.</summary>
        public Layer Layer { get { return this.Resources.Values.OfType<Layer>().FirstOrDefault(); } }
        /// <summary>Gets the stored linetype identity, or null when group 6 was absent.</summary>
        public Linetype Linetype { get { return this.Resources.Values.OfType<Linetype>().FirstOrDefault(); } }
        /// <summary>Gets whether this record terminates the vertex sequence.</summary>
        public bool IsSequenceEnd { get { return this.CodeName == DxfObjectCode.EndSequence; } }
        internal IEnumerable<DxfObject> References { get { return this.Resources.Values; } }

        internal void Validate(DxfDocument document, Polyline3D owner, bool registered)
        {
            if (this.SourceDocument != null && !ReferenceEquals(this.SourceDocument, document) || !ReferenceEquals(this.Owner, owner))
                throw new InvalidOperationException("A retained polyline record must stay with its source polyline and document.");
            DxfObject current = this.Handle == null ? null : document.GetObjectByHandle(this.Handle);
            if (registered ? !ReferenceEquals(current, this) : current != null && !ReferenceEquals(current, this))
                throw new InvalidOperationException("A retained polyline record has inconsistent registration.");
            if (this.SourceDocument == null && !registered) return;
            foreach (DxfObject target in this.References)
                if (!ReferenceEquals(document.GetObjectByHandle(target.Handle), target))
                    throw new InvalidOperationException("A retained polyline record resource is outside its source document.");
        }
        internal bool CanClone()
        {
            return !this.HasPrivateData && this.ExtensionDictionary == null && this.PersistentReactors.Count == 0
                && this.Resources.Values.All(resource => resource is Layer || resource is Linetype)
                && !this.XData.Values.SelectMany(data => data.XDataRecord).Any(tag => tag.Code == XDataCode.DatabaseHandle
                    && ulong.Parse((string)tag.Value, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture) != 0);
        }
        internal Polyline3DRecord CopyForClone()
        {
            var result = new Polyline3DRecord(this.CodeName, new List<DxfTag>(this.Tags))
            {
                SourceOwner = this.SourceOwner, CommonEnd = this.CommonEnd, XDataStart = this.XDataStart,
                Position = this.Position, IdentityIndex = this.IdentityIndex, OwnerIndex = this.OwnerIndex,
                ExtensionHandle = this.ExtensionHandle
            };
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
