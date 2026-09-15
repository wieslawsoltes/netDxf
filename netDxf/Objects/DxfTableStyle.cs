// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Objects
{
    /// <summary>A loaded TABLESTYLE with immutable source payload and conservative classic projections.</summary>
    /// <remarks>Style editing, map interpretation and table regeneration are not supported. Common metadata and XData retain their ordinary interfaces.</remarks>
    public sealed partial class DxfTableStyle : DxfDatabaseObject
    {
        private readonly DxfDocument source;
        private readonly List<DxfObject> references = new List<DxfObject>();
        private readonly Dictionary<DxfTag, Tuple<TextStyle, string>> namedStyles = new Dictionary<DxfTag, Tuple<TextStyle, string>>();
        private readonly Dictionary<string, DxfObject> handles = new Dictionary<string, DxfObject>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DxfTag> publicTags;
        private bool resolved;

        internal DxfTableStyle(DxfDocument source, IList<DxfTag> tags, Func<string, string> decode) : base("TABLESTYLE")
        {
            this.source = source;
            this.SourceVersion = source.DrawingVariables.AcadVer;
            if (this.SourceVersion < DxfVersion.AutoCad2004) throw new NotSupportedException("Stored TABLESTYLE requires an AutoCAD 2004 or later source profile.");
            this.Tags = new ReadOnlyCollection<DxfTag>(new List<DxfTag>(tags));
            this.publicTags = PublicTags(tags);
            int first = this.publicTags.FindIndex(t => t.Code == 7);
            this.Header = DxfTableStyleHeader.TryRead(first < 0 ? this.publicTags : this.publicTags.Take(first).ToList(), decode);
            var rows = new List<DxfTableStyleRow>();
            var starts = this.publicTags.Select((tag, index) => new { tag, index }).Where(p => p.tag.Code == 7).Select(p => p.index).ToList();
            if (starts.Count == 3)
                for (int i = 0; i < starts.Count; i++)
                    rows.Add(new DxfTableStyleRow(this.publicTags.GetRange(starts[i], (i + 1 < starts.Count ? starts[i + 1] : this.publicTags.Count) - starts[i]), decode));
            this.Rows = rows.AsReadOnly();
        }
        /// <summary>Gets the source DXF version. Cross-version output is not qualified.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the complete immutable retained packet, excluding recognized common metadata and XData.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
        /// <summary>Gets classic header values, or null for an incomplete, ambiguous or unknown header.</summary>
        public DxfTableStyleHeader Header { get; }
        /// <summary>Gets three ordered classic row packets, or an empty list when their count is not recognized.</summary>
        /// <remarks>Order is retained without assigning data, title or header roles. Unknown and unprojected values remain in Tags.</remarks>
        public IReadOnlyList<DxfTableStyleRow> Rows { get; }
        /// <summary>Gets exact registered STYLE and exposed semantic handle dependencies.</summary>
        public IReadOnlyList<DxfObject> References { get { return this.references.AsReadOnly(); } }
        /// <summary>Gets the retained owned CELLSTYLEMAP when the known extension dictionary slot identifies it.</summary>
        /// <remarks>This reference does not qualify the map's custom cell-style schema or synchronization.</remarks>
        public DxfOpaqueObject CellStyleMap { get; private set; }
        // The common database registrar does not own ATTRIB/ENDBLK metadata carriers.
        // Their exact retained identities are validated below and guarded through References.
        internal override IEnumerable<DxfObject> DatabaseReferences
        { get { return this.references.Where(item => ReferenceEquals(this.source.GetObjectByHandle(item.Handle), item)); } }
        internal override IEnumerable<DxfTag> AllocationReservations { get { return this.Tags; } }
        internal override DxfDatabaseObject CloneShell() { throw new NotSupportedException("Stored TABLESTYLE cloning requires its complete application schema."); }

        internal void Resolve(Func<string, DxfObject> resolve, Func<string, string> decode)
        {
            foreach (DxfTag tag in this.Tags)
                if (DxfObjectDatabase.IsReference(tag))
                {
                    DxfObject target = resolve((string)tag.Value);
                    if (target == null) continue;
                    this.handles[(string)tag.Value] = target;
                    this.references.Add(target);
                }
            foreach (DxfTag tag in this.publicTags.Where(t => t.Code == 7))
            {
                string name = decode((string)tag.Value);
                if (!this.source.TextStyles.TryGetValue(name, out TextStyle style) || !ReferenceEquals(resolve(style.Handle), style)) continue;
                this.namedStyles.Add(tag, Tuple.Create(style, style.Name));
                this.references.Add(style);
            }
            foreach (DxfTableStyleRow row in this.Rows)
                if (this.namedStyles.TryGetValue(row.Tags[0], out Tuple<TextStyle, string> binding)) row.BindTextStyle(binding.Item1);
            if (this.ExtensionDictionary != null && this.ExtensionDictionary.Contains("ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP"))
            {
                var map = this.ExtensionDictionary["ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP"] as DxfOpaqueObject;
                if (map != null && map.CodeName == "CELLSTYLEMAP" && ReferenceEquals(map.Owner, this.ExtensionDictionary)) this.CellStyleMap = map;
            }
            this.resolved = true;
        }
        internal string ChangedStyleName(DxfTag tag)
        {
            if (!this.namedStyles.TryGetValue(tag, out Tuple<TextStyle, string> binding) || binding.Item1.Name == binding.Item2) return null;
            return binding.Item1.Name;
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!this.resolved || !ReferenceEquals(database.Document, this.source)) errors.Add("Stored TABLESTYLE must remain in its source document.");
            if (database.Document.DrawingVariables.AcadVer != this.SourceVersion) errors.Add("Stored TABLESTYLE conversion requires complete schema regeneration.");
            foreach (var pair in this.handles)
                if (!ReferenceEquals(this.source.StoredTableHandleTarget(pair.Key), pair.Value)) errors.Add("A stored TABLESTYLE handle dependency is no longer registered: " + pair.Key);
            foreach (var binding in this.namedStyles.Values)
                if (!ReferenceEquals(this.source.GetObjectByHandle(binding.Item1.Handle), binding.Item1)) errors.Add("A stored TABLESTYLE text-style dependency is no longer registered.");
            if (this.CellStyleMap != null && (!ReferenceEquals(this.source.GetObjectByHandle(this.CellStyleMap.Handle), this.CellStyleMap) || !ReferenceEquals(this.CellStyleMap.Owner, this.ExtensionDictionary)))
                errors.Add("A stored TABLESTYLE map dependency changed its identity or owner.");
            foreach (DxfTag tag in this.Tags)
                if (tag.Value is string text)
                    for (int i = 0; i < text.Length; i++)
                        if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || i + 1 == text.Length || !char.IsLowSurrogate(text[++i])))
                        { errors.Add("Stored TABLESTYLE contains invalid UTF-16 text."); break; }
        }
        private static List<DxfTag> PublicTags(IList<DxfTag> tags)
        {
            var result = new List<DxfTag>();
            bool active = false, seen = false;
            int depth = 0;
            foreach (DxfTag tag in tags)
            {
                if (tag.Code == 102)
                {
                    string marker = (string)tag.Value;
                    if (marker.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (marker == "}" && depth > 0) depth--;
                    else throw new FormatException("Invalid TABLESTYLE application control group.");
                    continue;
                }
                if (depth > 0) continue;
                if (tag.Code == 100)
                {
                    active = (string)tag.Value == "AcDbTableStyle";
                    if (active && seen) return new List<DxfTag>();
                    seen |= active;
                    continue;
                }
                if (active) result.Add(tag);
            }
            if (depth != 0) throw new FormatException("Unterminated TABLESTYLE application control group.");
            return result;
        }
    }
}
