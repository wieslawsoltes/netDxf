// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>The storage relationship recorded in an OLE2FRAME.</summary>
    public enum OleObjectType : short
    {
        /// <summary>A linked object. The link is never followed by netDxf.</summary>
        Link = 1,
        /// <summary>An embedded object. The payload is never activated by netDxf.</summary>
        Embedded = 2,
        /// <summary>A static representation.</summary>
        Static = 3
    }

    /// <summary>Presence of optional informational fields in an OLE2FRAME packet.</summary>
    [Flags]
    public enum Ole2FrameMetadataFields
    {
        /// <summary>No optional informational fields are emitted.</summary>
        None = 0,
        /// <summary>Group 70: stored OLE version.</summary>
        OleVersion = 1,
        /// <summary>Group 3: user-type description, including an explicitly empty string.</summary>
        Description = 2,
        /// <summary>Groups 10/20/30: complete upper-left WCS point.</summary>
        UpperLeftCorner = 4,
        /// <summary>Groups 11/21/31: complete lower-right WCS point.</summary>
        LowerRightCorner = 8,
        /// <summary>Group 71: link, embedded or static relationship.</summary>
        ObjectType = 16,
        /// <summary>Group 72: stored model/paper descriptor.</summary>
        TileMode = 32,
        /// <summary>All supported optional fields; the existing public constructor defaults to this.</summary>
        All = OleVersion | Description | UpperLeftCorner | LowerRightCorner | ObjectType | TileMode
    }

    /// <summary>An inert OLE2FRAME containing its published metadata and uninterpreted binary payload.</summary>
    /// <remarks>
    /// These metadata fields are redundant with private OLE data. They are deliberately immutable:
    /// editing a corner alone cannot relocate the embedded object. No COM activation, link resolution,
    /// rendering, private payload editing or native OLE validation is performed. Use the raw pipeline
    /// for unsupported private extensions. Typed persistence is supported in the 2000+ profiles.
    /// </remarks>
    public sealed class Ole2Frame : EntityObject
    {
        private readonly byte[] binaryData;

        /// <summary>Constructs an inert frame, defensively copying its payload.</summary>
        /// <param name="binaryData">Uninterpreted binary object data; an empty array is permitted.</param>
        /// <param name="upperLeftCorner">Finite stored upper-left WCS corner, not recomputed from the payload.</param>
        /// <param name="lowerRightCorner">Finite stored lower-right WCS corner, not recomputed from the payload.</param>
        /// <param name="description">Stored group-3 user-type description, with no CR, LF or NUL delimiters.</param>
        /// <param name="oleVersion">Nonnegative stored OLE version number.</param>
        /// <param name="objectType">Link, embedded or static object relationship.</param>
        /// <param name="tileMode">Stored group 72: zero for model space, one for paper space.</param>
        public Ole2Frame(byte[] binaryData, Vector3 upperLeftCorner, Vector3 lowerRightCorner,
            string description = "", short oleVersion = 2, OleObjectType objectType = OleObjectType.Embedded,
            short tileMode = 0)
            : this(binaryData, upperLeftCorner, lowerRightCorner, description, oleVersion, objectType, tileMode, true)
        {
        }

        internal Ole2Frame(byte[] binaryData, Vector3 upperLeftCorner, Vector3 lowerRightCorner,
            string description, short oleVersion, OleObjectType objectType, short tileMode, bool copyData,
            Ole2FrameMetadataFields metadataFields = Ole2FrameMetadataFields.All)
            : base(EntityType.Ole2Frame, DxfObjectCode.Ole2Frame)
        {
            if (binaryData == null) throw new ArgumentNullException(nameof(binaryData));
            if (description == null) throw new ArgumentNullException(nameof(description));
            if (description.IndexOf('\r') >= 0 || description.IndexOf('\n') >= 0 || description.IndexOf('\0') >= 0)
                throw new ArgumentException("OLE description cannot contain transport delimiters.", nameof(description));
            ValidatePoint(upperLeftCorner, nameof(upperLeftCorner));
            ValidatePoint(lowerRightCorner, nameof(lowerRightCorner));
            if (oleVersion < 0) throw new ArgumentOutOfRangeException(nameof(oleVersion));
            if (objectType < OleObjectType.Link || objectType > OleObjectType.Static)
                throw new ArgumentOutOfRangeException(nameof(objectType));
            if (tileMode != 0 && tileMode != 1) throw new ArgumentOutOfRangeException(nameof(tileMode));
            if ((metadataFields & ~Ole2FrameMetadataFields.All) != 0)
                throw new ArgumentOutOfRangeException(nameof(metadataFields));
            this.MetadataFields = metadataFields;
            this.binaryData = copyData ? (byte[])binaryData.Clone() : binaryData;
            this.UpperLeftCorner = upperLeftCorner;
            this.LowerRightCorner = lowerRightCorner;
            this.Description = description;
            this.OleVersion = oleVersion;
            this.ObjectType = objectType;
            this.TileMode = tileMode;
        }

        /// <summary>Gets which optional fields were supplied or explicitly selected for output.</summary>
        /// <remarks>Default-valued getters are not evidence that a field was present in the input.</remarks>
        public Ole2FrameMetadataFields MetadataFields { get; }

        /// <summary>Returns an independent snapshot with an explicit optional-field output selection.</summary>
        /// <remarks>
        /// Values and binary data are not changed. Omitted values are deliberately not serialized;
        /// a later reload exposes the existing default-valued getters for absent fields. This operation
        /// does not rewrite private OLE data. Use Clone to preserve the exact current field selection.
        /// </remarks>
        /// <param name="metadataFields">Supported field flags; unknown bits are rejected.</param>
        /// <returns>An independent snapshot without database identity.</returns>
        public Ole2Frame WithMetadataFields(Ole2FrameMetadataFields metadataFields)
        {
            return this.Copy(metadataFields);
        }

        /// <summary>Gets the stored finite upper-left WCS corner.</summary>
        public Vector3 UpperLeftCorner { get; }
        /// <summary>Gets the stored finite lower-right WCS corner.</summary>
        public Vector3 LowerRightCorner { get; }
        /// <summary>Gets the stored group-3 user-type description.</summary>
        public string Description { get; }
        /// <summary>Gets the stored OLE version number.</summary>
        public short OleVersion { get; }
        /// <summary>Gets the stored object relationship; links are not resolved.</summary>
        public OleObjectType ObjectType { get; }
        /// <summary>Gets the stored model/paper descriptor independently of the current owner.</summary>
        public short TileMode { get; }
        /// <summary>Gets the number of uninterpreted binary bytes.</summary>
        public int BinaryDataLength { get { return this.binaryData.Length; } }
        /// <summary>Returns an independent copy of the binary payload.</summary>
        public byte[] GetBinaryData() { return (byte[])this.binaryData.Clone(); }
        internal byte[] BinaryData { get { return this.binaryData; } }

        /// <summary>Accepts only exact identity; every other transform requires private OLE rewriting.</summary>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            if (transformation.M11 == 1 && transformation.M22 == 1 && transformation.M33 == 1 &&
                transformation.M12 == 0 && transformation.M13 == 0 && transformation.M21 == 0 &&
                transformation.M23 == 0 && transformation.M31 == 0 && transformation.M32 == 0 &&
                translation.X == 0 && translation.Y == 0 && translation.Z == 0) return;
            throw new NotSupportedException("OLE2FRAME cannot be transformed without rewriting its private binary payload.");
        }

        /// <summary>Creates an independent inert frame, without handle, owner or reactor identity.</summary>
        public override object Clone()
        {
            return this.Copy(this.MetadataFields);
        }

        private Ole2Frame Copy(Ole2FrameMetadataFields metadataFields)
        {
            var copy = new Ole2Frame(this.binaryData, this.UpperLeftCorner, this.LowerRightCorner,
                this.Description, this.OleVersion, this.ObjectType, this.TileMode, true, metadataFields)
            {
                Layer = (Layer)this.Layer.Clone(), Linetype = (Linetype)this.Linetype.Clone(),
                Color = (AciColor)this.Color.Clone(), Lineweight = this.Lineweight,
                Transparency = (Transparency)this.Transparency.Clone(), LinetypeScale = this.LinetypeScale,
                IsVisible = this.IsVisible, Normal = this.Normal
            };
            foreach (XData data in this.XData.Values) copy.XData.Add((XData)data.Clone());
            return copy;
        }

        private static void ValidatePoint(Vector3 point, string parameter)
        {
            if (double.IsNaN(point.X) || double.IsInfinity(point.X) ||
                double.IsNaN(point.Y) || double.IsInfinity(point.Y) ||
                double.IsNaN(point.Z) || double.IsInfinity(point.Z))
                throw new ArgumentOutOfRangeException(parameter, "OLE corner must be finite.");
        }
    }
}
