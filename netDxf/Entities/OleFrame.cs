// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Inert legacy OLEFRAME data. This is distinct from OLE2FRAME.</summary>
    /// <remarks>
    /// The binary payload is never activated, interpreted or linked to an external application.
    /// No geometry is synthesized from the private data. Non-identity transforms are unsupported.
    /// </remarks>
    public sealed class OleFrame : EntityObject
    {
        private readonly byte[] binaryData;

        /// <summary>Constructs a frame with a defensive copy of the binary payload.</summary>
        /// <param name="binaryData">Uninterpreted bytes; an empty payload is permitted.</param>
        /// <param name="oleVersion">Nonnegative OLE version number; default is 1.</param>
        public OleFrame(byte[] binaryData, short oleVersion = 1)
            : this(binaryData, oleVersion, true)
        {
        }

        internal OleFrame(byte[] binaryData, short oleVersion, bool copyData, bool hasOleVersion = true)
            : base(EntityType.OleFrame, DxfObjectCode.OleFrame)
        {
            if (binaryData == null) throw new ArgumentNullException(nameof(binaryData));
            if (oleVersion < 0) throw new ArgumentOutOfRangeException(nameof(oleVersion));
            this.binaryData = copyData ? (byte[])binaryData.Clone() : binaryData;
            this.OleVersion = oleVersion;
            this.HasOleVersion = hasOleVersion;
        }

        /// <summary>Gets the stored OLE version, independent of the uninterpreted payload.</summary>
        public short OleVersion { get; }
        /// <summary>Gets whether the optional stored version tag is present.</summary>
        /// <remarks>An absent tag leaves the existing OleVersion getter at its default of 1 after loading.</remarks>
        public bool HasOleVersion { get; }

        /// <summary>Returns an independent frame selecting whether its version tag is written.</summary>
        /// <remarks>
        /// This selects metadata presence only; it does not rewrite native OLE data. Omitted values
        /// remain dormant in this instance but are not serialized. Reloading an omitted tag uses
        /// the existing default version of 1. The returned frame has no handle, owner or reactors.
        /// </remarks>
        public OleFrame WithOleVersionPresence(bool present) { return this.Copy(present); }

        /// <summary>Gets the number of stored binary bytes.</summary>
        public int BinaryDataLength { get { return this.binaryData.Length; } }
        /// <summary>Returns an independent copy of the stored bytes.</summary>
        public byte[] GetBinaryData() { return (byte[])this.binaryData.Clone(); }
        internal byte[] BinaryData { get { return this.binaryData; } }

        /// <summary>Accepts only exact identity, because other transforms require private payload rewriting.</summary>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            if (transformation.M11 == 1 && transformation.M22 == 1 && transformation.M33 == 1 &&
                transformation.M12 == 0 && transformation.M13 == 0 && transformation.M21 == 0 &&
                transformation.M23 == 0 && transformation.M31 == 0 && transformation.M32 == 0 &&
                translation.X == 0 && translation.Y == 0 && translation.Z == 0) return;
            throw new NotSupportedException("OLEFRAME cannot be transformed without rewriting its private binary payload.");
        }

        /// <summary>Creates an independent frame without retaining handle, owner or reactor identity.</summary>
        public override object Clone() { return this.Copy(this.HasOleVersion); }

        private OleFrame Copy(bool present)
        {
            var copy = new OleFrame(this.binaryData, this.OleVersion, true, present)
            {
                Layer = (Layer)this.Layer.Clone(), Linetype = (Linetype)this.Linetype.Clone(),
                Color = (AciColor)this.Color.Clone(), Lineweight = this.Lineweight,
                Transparency = (Transparency)this.Transparency.Clone(), LinetypeScale = this.LinetypeScale,
                IsVisible = this.IsVisible, Normal = this.Normal
            };
            foreach (XData data in this.XData.Values) copy.XData.Add((XData)data.Clone());
            return copy;
        }
    }
}
