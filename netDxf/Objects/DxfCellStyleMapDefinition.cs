// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Tables;

namespace netDxf.Objects
{
    /// <summary>An immutable authored grid packet. Resource identities are validated when applied.</summary>
    public sealed class DxfCellGridFormatDefinition
    {
        /// <summary>Creates a grid definition with a nonzero stored mask and optional LTYPE.</summary>
        public DxfCellGridFormatDefinition(int storedIndexMask, DxfCellGridFormatValues values, Linetype linetype)
        {
            if (storedIndexMask == 0) throw new ArgumentOutOfRangeException(nameof(storedIndexMask));
            this.StoredIndexMask = storedIndexMask;
            this.Values = values ?? throw new ArgumentNullException(nameof(values));
            this.Linetype = linetype;
        }
        /// <summary>Gets the stored index mask, without assigning an edge role.</summary>
        public int StoredIndexMask { get; }
        /// <summary>Gets immutable stored grid values.</summary>
        public DxfCellGridFormatValues Values { get; }
        /// <summary>Gets the selected LTYPE identity; null emits a null handle.</summary>
        public Linetype Linetype { get; }
    }

    /// <summary>An immutable, document-independent definition of the qualified public TABLEFORMAT grammar.</summary>
    /// <remarks>Values are stored, not evaluated. Resource objects are identities, not deep copies.</remarks>
    public sealed class DxfCellStyleFormatDefinition
    {
        /// <summary>Creates an empty format with no content, margins or grids.</summary>
        public DxfCellStyleFormatDefinition(int storedType)
            : this(storedType, 0, null, null, null, 0, null, Array.Empty<DxfCellGridFormatDefinition>()) { }

        /// <summary>Creates a format with explicit frame presence and zero to six ordered grids.</summary>
        /// <remarks>Zero data flags require all data absent. Zero margin flags require margins absent. Border enumeration is bounded and disposed before publication.</remarks>
        public DxfCellStyleFormatDefinition(int storedType, short storedDataFlags,
            DxfCellStyleFormatValues values, DxfCellContentFormatValues content, TextStyle textStyle,
            short storedMarginFlags, DxfCellMargins margins, IEnumerable<DxfCellGridFormatDefinition> borders)
        {
            if (borders == null) throw new ArgumentNullException(nameof(borders));
            var snapshot = new List<DxfCellGridFormatDefinition>();
            foreach (var border in borders)
            {
                if (border == null || snapshot.Count == 6)
                    throw new ArgumentException("At most six non-null grid definitions are allowed.", nameof(borders));
                snapshot.Add(border);
            }
            if (storedDataFlags == 0)
            {
                if (values != null || content != null || textStyle != null || storedMarginFlags != 0 || margins != null || snapshot.Count != 0)
                    throw new ArgumentException("An empty format cannot contain data or resource references.");
            }
            else if (values == null || content == null || (storedMarginFlags == 0) != (margins == null))
                throw new ArgumentException("Present data requires values/content and consistent margin presence.");
            this.StoredType = storedType; this.StoredDataFlags = storedDataFlags;
            this.Values = values; this.Content = content; this.TextStyle = textStyle;
            this.StoredMarginFlags = storedMarginFlags; this.Margins = margins; this.Borders = snapshot.AsReadOnly();
        }
        /// <summary>Gets the stored format type.</summary>
        public int StoredType { get; }
        /// <summary>Gets the flags controlling data presence.</summary>
        public short StoredDataFlags { get; }
        /// <summary>Gets table values, or null for an empty format.</summary>
        public DxfCellStyleFormatValues Values { get; }
        /// <summary>Gets content values, or null for an empty format.</summary>
        public DxfCellContentFormatValues Content { get; }
        /// <summary>Gets the selected STYLE identity; null emits a null handle.</summary>
        public TextStyle TextStyle { get; }
        /// <summary>Gets the flags controlling margin presence; zero when data is absent.</summary>
        public short StoredMarginFlags { get; }
        /// <summary>Gets margins, or null when their frame is absent.</summary>
        public DxfCellMargins Margins { get; }
        /// <summary>Gets the immutable ordered grids. Duplicate nonzero masks retain their order.</summary>
        public IReadOnlyList<DxfCellGridFormatDefinition> Borders { get; }
        internal int TagCount { get { return this.StoredDataFlags == 0 ? 4 : 24 + (this.Margins == null ? 0 : 9) + 11 * this.Borders.Count; } }
        internal IEnumerable<DxfObject> Resources
        {
            get
            {
                if (this.TextStyle != null) yield return this.TextStyle;
                foreach (var border in this.Borders) if (border.Linetype != null) yield return border.Linetype;
            }
        }

        /// <summary>Exports a complete qualified snapshot without silently discarding wrong-typed handle targets.</summary>
        public static DxfCellStyleFormatDefinition FromFormat(DxfCellStyleFormat format)
        {
            if (format == null) throw new ArgumentNullException(nameof(format));
            if (format.Content != null && format.Content.TextStyle == null && Convert.ToUInt64(format.Content.StoredTextStyleHandle, 16) != 0)
                throw new NotSupportedException("The content handle does not identify a typed STYLE.");
            var grids = new List<DxfCellGridFormatDefinition>();
            foreach (var border in format.Borders)
            {
                if (border.Linetype == null && Convert.ToUInt64(border.StoredLinetypeHandle, 16) != 0)
                    throw new NotSupportedException("A grid handle does not identify a typed LTYPE.");
                grids.Add(new DxfCellGridFormatDefinition(border.StoredIndexMask, border.Values, border.Linetype));
            }
            return new DxfCellStyleFormatDefinition(format.StoredType, format.StoredDataFlags, format.Values,
                format.Content?.Values, format.Content?.TextStyle, format.StoredMarginFlags ?? 0, format.Margins, grids);
        }

        /// <summary>Returns a definition using explicitly mapped destination STYLE/LTYPE identities.</summary>
        /// <remarks>The callback runs once per distinct non-null source identity. Null or wrong-typed results reject. No document or resource is mutated or imported.</remarks>
        public DxfCellStyleFormatDefinition RemapResources(Func<DxfObject, DxfObject> resolve)
        {
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));
            var mapped = new List<Tuple<DxfObject, DxfObject>>();
            DxfObject Map(DxfObject source)
            {
                if (source == null) return null;
                var prior = mapped.FirstOrDefault(pair => ReferenceEquals(pair.Item1, source));
                if (prior != null) return prior.Item2;
                var target = resolve(source);
                if (!(source is TextStyle && target is TextStyle) && !(source is Linetype && target is Linetype))
                    throw new ArgumentException("Every source resource requires an explicit destination of the same kind.", nameof(resolve));
                mapped.Add(Tuple.Create(source, target));
                return target;
            }
            return new DxfCellStyleFormatDefinition(this.StoredType, this.StoredDataFlags, this.Values, this.Content,
                (TextStyle)Map(this.TextStyle), this.StoredMarginFlags, this.Margins,
                this.Borders.Select(b => new DxfCellGridFormatDefinition(b.StoredIndexMask, b.Values, (Linetype)Map(b.Linetype))));
        }
    }

    /// <summary>An immutable authored map entry; duplicate identifiers and names are not assigned implicit roles.</summary>
    public sealed class DxfCellStyleMapEntryDefinition
    {
        /// <summary>Creates an entry with decoded text and a complete explicit format definition.</summary>
        public DxfCellStyleMapEntryDefinition(int id, int storedType, string name, DxfCellStyleFormatDefinition format)
        {
            DxfStoredTableContent.CheckEditableText(name, nameof(name));
            this.Id = id; this.StoredType = storedType; this.Name = name;
            this.Format = format ?? throw new ArgumentNullException(nameof(format));
        }
        /// <summary>Gets the stored identifier.</summary>
        public int Id { get; }
        /// <summary>Gets the stored entry type.</summary>
        public int StoredType { get; }
        /// <summary>Gets the decoded name.</summary>
        public string Name { get; }
        /// <summary>Gets the complete format definition.</summary>
        public DxfCellStyleFormatDefinition Format { get; }
        /// <summary>Exports an entry only when its complete public formatting grammar is qualified.</summary>
        /// <remarks>Common metadata and owner graphs belong to the map, not this definition.</remarks>
        public static DxfCellStyleMapEntryDefinition FromEntry(DxfStoredCellStyleMapEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entry.Format == null) throw new NotSupportedException("Unsupported format packets cannot be exported as complete definitions.");
            return new DxfCellStyleMapEntryDefinition(entry.Id, entry.StoredType, entry.Name, DxfCellStyleFormatDefinition.FromFormat(entry.Format));
        }
    }
}
