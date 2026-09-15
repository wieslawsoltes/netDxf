// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using netDxf.Header;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>A MULTILEADER entity with one stored context and typed text or block content.</summary>
    /// <remarks>The qualified output profiles are R2007 and later. Stored geometry and override values are independent; this API does not evaluate styles, annotation scales or rendering.</remarks>
    public sealed class MultiLeader : EntityObject
    {
        private readonly MLeaderProperties properties;
        private readonly MLeaderContext context;
        internal bool PendingInputReferences;
        private short? storedVersion = 2;
        /// <summary>Creates a leader with an empty context. Assign registered style, linetype and text-style references before adding it to a document.</summary>
        public MultiLeader() : base(EntityType.MultiLeader,"MULTILEADER")
        { this.properties=new MLeaderProperties{Parent=this};this.context=new MLeaderContext{Parent=this}; }
        /// <summary>Gets the entity-level stored properties and repeated arrow/attribute data.</summary>
        public MLeaderProperties Properties {get{return this.properties;}}
        /// <summary>Gets the independently stored coordinate, content and leader context.</summary>
        public MLeaderContext Context {get{return this.context;}}
        /// <summary>Gets or sets the optional group-270 envelope value. Null retains physical absence; the only qualified explicit value is 2.</summary>
        public short? StoredVersion
        {
            get { return this.storedVersion; }
            set { if (value.HasValue && value.Value != 2) throw new ArgumentOutOfRangeException(nameof(value)); this.storedVersion = value; }
        }
        /// <summary>Gets the qualified effective AcDbMLeader grammar version, always 2, including when group 270 is absent.</summary>
        public short Version {get{return 2;}}
        internal IEnumerable<MLeaderData> Data {get{yield return this.properties;yield return this.context;}}
        /// <summary>Validates the complete value grammar and, when registered, every object reference.</summary>
        public void Validate() {var document=MLeaderData.RegisteredDocument(this);this.Validate(document,document?.DrawingVariables.AcadVer??DxfVersion.AutoCad2018);}
        internal void Validate(DxfDocument document,DxfVersion version)
        {
            if(version<DxfVersion.AutoCad2007)throw new NotSupportedException("MULTILEADER requires the qualified R2007 or later writer profile.");
            this.properties.ValidateValues(version);this.context.ValidateValues(version);
            if(this.properties.Style==null||this.properties.LeaderLinetype==null||this.properties.TextStyle==null)
                throw new InvalidOperationException("MULTILEADER requires style, leader linetype and text-style references.");
            short type=this.properties.ContentType;
            if(type<0||type>2)throw new NotSupportedException("Only no-content, block and MTEXT MULTILEADER content types are qualified.");
            if((type==1)!=(this.context.Block!=null)||(type==2)!=(this.context.MText!=null))throw new InvalidOperationException("MULTILEADER content type and stored context disagree.");
            foreach(var arrow in this.properties.ArrowHeads)if(arrow.Block==null)throw new InvalidOperationException("Repeated MULTILEADER arrow data requires a block record.");
            foreach(var attribute in this.properties.BlockAttributes)
            {
                if(attribute.Definition==null)throw new InvalidOperationException("MULTILEADER attribute data requires an ATTDEF reference.");
                if(this.context.Block==null||attribute.Definition.Owner?.Record!=this.context.Block.Block)
                    throw new InvalidOperationException("MULTILEADER attributes must reference definitions in the embedded content block.");
            }
            this.properties.CheckDocument(document);this.context.CheckDocument(document);
        }
        internal void ValidateIncoming(DxfDocument document)
        {
            if(this.PendingInputReferences)return;
            this.Validate(document,document.DrawingVariables.AcadVer);
            foreach(XData data in this.XData.Values)
                if(data.ApplicationRegistry.Owner!=null && data.ApplicationRegistry.Owner!=document.ApplicationRegistries)
                    throw new ArgumentException("Clone foreign-owned XData registries before adding a MULTILEADER to this document.");
        }
        /// <summary>Rejects geometric transforms whose consistency across redundant context fields is not qualified.</summary>
        /// <remarks>The identity operation is accepted. Edit explicit stored coordinates when intentional independent values are required.</remarks>
        public override void TransformBy(Matrix3 transformation,Vector3 translation)
        {
            if(transformation.M11!=1||transformation.M22!=1||transformation.M33!=1||
                transformation.M12!=0||transformation.M13!=0||transformation.M21!=0||
                transformation.M23!=0||transformation.M31!=0||transformation.M32!=0||
                translation.X!=0||translation.Y!=0||translation.Z!=0)
                throw new NotSupportedException("MULTILEADER geometric transforms are not qualified; no stored value was changed.");
        }
        /// <summary>Accepts only the exact four-by-four identity matrix, including its fourth row.</summary>
        public override void TransformBy(Matrix4 transformation)
        {
            for(int row=0;row<4;row++)
                for(int column=0;column<4;column++)
                    if(transformation[row,column]!=(row==column?1.0:0.0))
                        throw new NotSupportedException("MULTILEADER geometric transforms are not qualified; no stored value was changed.");
        }
        /// <summary>Creates independent value components while retaining registered object references. Map those references explicitly when copying across documents.</summary>
        public override object Clone()
        {
            var copy=new MultiLeader {Layer=(Layer)this.Layer.Clone(),Linetype=(Linetype)this.Linetype.Clone(),Color=(AciColor)this.Color.Clone(),Lineweight=this.Lineweight,
                Transparency=(Transparency)this.Transparency.Clone(),LinetypeScale=this.LinetypeScale,IsVisible=this.IsVisible,Normal=this.Normal};
            copy.StoredVersion=this.StoredVersion;
            this.properties.CopyValuesTo(copy.properties);this.properties.CopyChildrenTo(copy.properties);
            this.context.CopyValuesTo(copy.context);this.context.CopyChildrenTo(copy.context);
            foreach(XData data in this.XData.Values)copy.XData.Add((XData)data.Clone());
            this.CopyCommonDataTo(copy);return copy;
        }
    }
    public sealed partial class MLeaderProperties
    {
        private readonly MLeaderChildCollection<MLeaderArrowHead> arrows;
        private readonly MLeaderChildCollection<MLeaderBlockAttribute> attributes;
        /// <summary>Creates the stored entity-level properties.</summary>
        public MLeaderProperties(){this.arrows=new MLeaderChildCollection<MLeaderArrowHead>(this);this.attributes=new MLeaderChildCollection<MLeaderBlockAttribute>(this);}
        /// <summary>Gets ordered repeated arrow data. Indices are retained independently of line indices.</summary>
        public Collection<MLeaderArrowHead> ArrowHeads {get{return this.arrows;}}
        /// <summary>Gets ordered ATTDEF associations and their stored text values.</summary>
        public Collection<MLeaderBlockAttribute> BlockAttributes {get{return this.attributes;}}
        internal override IEnumerable<MLeaderData> Children {get{foreach(var arrow in this.arrows)yield return arrow;foreach(var attribute in this.attributes)yield return attribute;}}
        internal override void CopyChildrenTo(MLeaderData target)
        {var copy=(MLeaderProperties)target;foreach(var arrow in this.arrows)copy.ArrowHeads.Add((MLeaderArrowHead)arrow.Clone());foreach(var attribute in this.attributes)copy.BlockAttributes.Add((MLeaderBlockAttribute)attribute.Clone());}
    }
}
