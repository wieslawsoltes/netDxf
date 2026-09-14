// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using netDxf.Header;

namespace netDxf.Entities
{
    public sealed partial class MLeaderContext
    {
        private MLeaderMTextContent mtext;
        private MLeaderBlockContent block;
        private readonly MLeaderChildCollection<MLeaderNode> leaders;
        /// <summary>Creates an empty context with the published coordinate defaults.</summary>
        public MLeaderContext() { this.leaders = new MLeaderChildCollection<MLeaderNode>(this); }
        /// <summary>Gets the ordered leader branches. Their indices are stored values, not array offsets.</summary>
        public Collection<MLeaderNode> Leaders { get { return this.leaders; } }
        /// <summary>Gets or sets embedded text content. Remove block content before assigning text.</summary>
        public MLeaderMTextContent MText
        {
            get { return this.mtext; }
            set
            {
                if (ReferenceEquals(value,this.mtext)) return;
                if (value != null && (value.Parent != null || this.block != null)) throw new ArgumentException("Content must be unowned and mutually exclusive.",nameof(value));
                value?.CheckDocument(this.Document);
                if (this.mtext != null) this.mtext.Parent=null;
                this.mtext=value; if(value!=null)value.Parent=this;
            }
        }
        /// <summary>Gets or sets embedded block content. Remove text content before assigning a block.</summary>
        public MLeaderBlockContent Block
        {
            get { return this.block; }
            set
            {
                if (ReferenceEquals(value,this.block)) return;
                if (value != null && (value.Parent != null || this.mtext != null)) throw new ArgumentException("Content must be unowned and mutually exclusive.",nameof(value));
                value?.CheckDocument(this.Document);
                if (this.block != null) this.block.Parent=null;
                this.block=value; if(value!=null)value.Parent=this;
            }
        }
        internal override IEnumerable<MLeaderData> Children
        { get { if(this.mtext!=null)yield return this.mtext; if(this.block!=null)yield return this.block; foreach(var node in this.leaders)yield return node; } }
        internal override void CopyChildrenTo(MLeaderData target)
        {
            var copy=(MLeaderContext)target;
            if(this.mtext!=null)copy.MText=(MLeaderMTextContent)this.mtext.Clone();
            if(this.block!=null)copy.Block=(MLeaderBlockContent)this.block.Clone();
            foreach(var node in this.leaders)copy.Leaders.Add((MLeaderNode)node.Clone());
        }
        internal override void ValidateValues(DxfVersion version)
        {
            base.ValidateValues(version);
            ValidateDirection(this.PlaneXAxis); ValidateDirection(this.PlaneYAxis); ValidateDirection(Vector3.CrossProduct(this.PlaneXAxis,this.PlaneYAxis));
            this.mtext?.ValidateValues(version); this.block?.ValidateValues(version);
            foreach(var node in this.leaders)node.ValidateValues(version);
        }
    }
    public sealed partial class MLeaderMTextContent
    {
        private readonly Collection<double> columnHeights=new Collection<double>();
        /// <summary>Gets the ordered group-144 column heights, independent of the stored width and height fields.</summary>
        public Collection<double> ColumnHeights { get { return this.columnHeights; } }
        internal override void CopyChildrenTo(MLeaderData target) { foreach(double value in this.columnHeights)((MLeaderMTextContent)target).ColumnHeights.Add(value); }
        internal override void ValidateValues(DxfVersion version)
        {
            base.ValidateValues(version); ValidateDirection(this.Normal); ValidateDirection(this.Direction);
            if(this.Style==null)throw new InvalidOperationException("Embedded MULTILEADER text requires a registered STYLE reference.");
            if(this.ColumnType<0 || this.ColumnType>2)throw new NotSupportedException("Unsupported embedded MULTILEADER text column type.");
            foreach(double value in this.columnHeights) { Finite(value); if(value<0)throw new InvalidOperationException("Column heights cannot be negative."); }
        }
    }
    public sealed partial class MLeaderBlockContent
    {
        private readonly Collection<double> matrix=new Collection<double>();
        /// <summary>Gets the sixteen group-47 values in their original DXF order. Empty means no stored matrix.</summary>
        public Collection<double> TransformationMatrix { get { return this.matrix; } }
        internal override void CopyChildrenTo(MLeaderData target) { foreach(double value in this.matrix)((MLeaderBlockContent)target).TransformationMatrix.Add(value); }
        internal override void ValidateValues(DxfVersion version)
        {
            base.ValidateValues(version); ValidateDirection(this.Normal);
            if(this.Block==null)throw new InvalidOperationException("Embedded MULTILEADER block content requires a registered BLOCK_RECORD reference.");
            if(this.matrix.Count!=0 && this.matrix.Count!=16)throw new InvalidOperationException("A stored MULTILEADER block matrix must contain exactly sixteen values.");
            foreach(double value in this.matrix)Finite(value);
        }
    }
    /// <summary>A pair of stored WCS break endpoints.</summary>
    public sealed class MLeaderBreak
    {
        /// <summary>Creates a finite break pair without changing endpoint order.</summary>
        public MLeaderBreak(Vector3 start,Vector3 end) { MLeaderData.Finite(start);MLeaderData.Finite(end);this.Start=start;this.End=end; }
        /// <summary>Gets the start endpoint.</summary>
        public Vector3 Start {get;}
        /// <summary>Gets the end endpoint.</summary>
        public Vector3 End {get;}
    }
    /// <summary>An ordered group of line breaks associated with one stored segment index.</summary>
    public sealed class MLeaderLineBreaks
    {
        private readonly Collection<MLeaderBreak> breaks=new Collection<MLeaderBreak>();
        /// <summary>Gets or sets the stored group-90 index.</summary>
        public int Index {get;set;}
        /// <summary>Gets the ordered break pairs. Multiple pairs for the same index remain together.</summary>
        public Collection<MLeaderBreak> Breaks {get{return this.breaks;}}
        internal MLeaderLineBreaks Copy() {var copy=new MLeaderLineBreaks{Index=this.Index};foreach(var pair in this.breaks)copy.Breaks.Add(pair);return copy;}
    }
    public sealed partial class MLeaderNode
    {
        private readonly Collection<MLeaderBreak> breaks=new Collection<MLeaderBreak>();
        private readonly MLeaderChildCollection<MLeaderLine> lines;
        /// <summary>Creates a leader branch.</summary>
        public MLeaderNode() {this.lines=new MLeaderChildCollection<MLeaderLine>(this);}
        /// <summary>Gets ordered landing break pairs.</summary>
        public Collection<MLeaderBreak> Breaks {get{return this.breaks;}}
        /// <summary>Gets the ordered leader lines.</summary>
        public Collection<MLeaderLine> Lines {get{return this.lines;}}
        internal override IEnumerable<MLeaderData> Children {get{return this.lines;}}
        internal override void CopyChildrenTo(MLeaderData target) {var copy=(MLeaderNode)target;foreach(var pair in this.breaks)copy.Breaks.Add(pair);foreach(var line in this.lines)copy.Lines.Add((MLeaderLine)line.Clone());}
        internal override void ValidateValues(DxfVersion version) {base.ValidateValues(version);if(this.breaks.Any(b=>b==null))throw new InvalidOperationException("Break pairs cannot be null.");foreach(var line in this.lines)line.ValidateValues(version);}
    }
    public sealed partial class MLeaderLine
    {
        private readonly Collection<Vector3> vertices=new Collection<Vector3>();
        private readonly Collection<MLeaderLineBreaks> breaks=new Collection<MLeaderLineBreaks>();
        /// <summary>Gets stored WCS line vertices or spline fit points.</summary>
        public Collection<Vector3> Vertices {get{return this.vertices;}}
        /// <summary>Gets ordered indexed break groups.</summary>
        public Collection<MLeaderLineBreaks> Breaks {get{return this.breaks;}}
        internal override void CopyChildrenTo(MLeaderData target) {var copy=(MLeaderLine)target;foreach(var point in this.vertices)copy.Vertices.Add(point);foreach(var group in this.breaks)copy.Breaks.Add(group.Copy());}
        internal override void ValidateValues(DxfVersion version)
        {base.ValidateValues(version);foreach(var point in this.vertices)Finite(point);foreach(var group in this.breaks)if(group==null||group.Breaks.Count==0||group.Breaks.Any(b=>b==null))throw new InvalidOperationException("Line break groups require complete endpoint pairs.");}
    }
}
