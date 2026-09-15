// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.Objects
{
    /// <summary>A named MLEADERSTYLE object with the published stored parameter set.</summary>
    /// <remarks>The dictionary key supplies its name; group 3 is an independent description. Style changes do not rewrite existing MULTILEADER values.</remarks>
    public sealed class DxfMLeaderStyle : DxfDatabaseObject
    {
        private readonly MLeaderStyleProperties properties;
        private short? storedEnvelopeValue = 2;
        /// <summary>Creates a detached style. Register referenced tables and blocks before adopting this object.</summary>
        public DxfMLeaderStyle() : base("MLEADERSTYLE") {this.properties=new MLeaderStyleProperties{Parent=this};}
        /// <summary>Gets editable style values and typed document-object references.</summary>
        public MLeaderStyleProperties Properties {get{return this.properties;}}
        /// <summary>Gets or sets the optional group-179 envelope value. Null retains physical absence; the only qualified explicit value is 2.</summary>
        /// <remarks>This retains the stored envelope marker without assigning undocumented semantics to it.</remarks>
        public short? StoredEnvelopeValue
        {
            get { return this.storedEnvelopeValue; }
            set { if (value.HasValue && value.Value != 2) throw new ArgumentOutOfRangeException(nameof(value)); this.storedEnvelopeValue = value; }
        }
        internal override IEnumerable<DxfObject> DatabaseReferences {get{return this.properties.References;}}
        internal override DxfDatabaseObject CloneShell() {var copy=new DxfMLeaderStyle { StoredEnvelopeValue=this.StoredEnvelopeValue };this.properties.CopyValuesTo(copy.properties);return copy;}
        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject target,Func<DxfObject,DxfObject> resolve)
        {((DxfMLeaderStyle)target).properties.MapReferences(resolve);}
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database,List<string> errors)
        {
            try {this.ValidateValues(database.Document.DrawingVariables.AcadVer);}
            catch(Exception error) when(error is ArgumentException||error is InvalidOperationException||error is NotSupportedException){errors.Add(error.Message);}
        }
        internal void ValidateValues(DxfVersion version)
        {
            this.properties.ValidateValues(version);
            if(this.properties.ContentType<0||this.properties.ContentType>2)throw new NotSupportedException("MLEADERSTYLE tolerance content is not qualified.");
            if(this.properties.TextStyle==null)throw new InvalidOperationException("MLEADERSTYLE requires a text-style reference.");
        }
    }
    public sealed partial class DxfObjectDatabase
    {
        /// <summary>Adds a detached style under ACAD_MLEADERSTYLE. Existing names are never overwritten.</summary>
        public void AddMLeaderStyle(string name,DxfMLeaderStyle style)
        {
            if(style==null)throw new ArgumentNullException(nameof(style));DxfDictionary.ValidateName(name);
            if(style.Owner!=null||style.Database!=null)throw new ArgumentException("The MLEADERSTYLE must be detached and unowned.",nameof(style));
            style.ValidateValues(this.Document.DrawingVariables.AcadVer);style.Properties.CheckDocument(this.Document);
            DxfDictionary dictionary;
            if(this.Root.Contains("ACAD_MLEADERSTYLE"))
            {dictionary=this.Root["ACAD_MLEADERSTYLE"] as DxfDictionary??throw new InvalidOperationException("ACAD_MLEADERSTYLE is not a dictionary.");dictionary.Add(name,style);}
            else {dictionary=new DxfDictionary();dictionary.Add(name,style);try{this.Root.Add("ACAD_MLEADERSTYLE",dictionary);}catch{dictionary.Remove(name);style.Owner=null;throw;}}
        }
    }
}
