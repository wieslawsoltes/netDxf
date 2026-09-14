// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Attribute
    {
        internal CommonEntityData CommonData { get; } = new CommonEntityData();

        /// <summary>Maximum accepted opaque proxy graphics payload, in bytes (16 MiB).</summary>
        public const int MaximumProxyGraphicsBytes = EntityObject.MaximumProxyGraphicsBytes;

        /// <summary>Gets or sets the optional stored color name (group 430, DXF 2004 and later).</summary>
        /// <remarks>Null omits the field; an empty string is explicitly present. This does not resolve a color book or change Color.</remarks>
        public string ColorName { get { return this.CommonData.ColorName; } set { this.CommonData.ColorName = value; } }

        /// <summary>Gets or sets the optional stored shadow mode (group 284, DXF 2007 and later).</summary>
        /// <remarks>Null omits the field; an explicit zero is retained.</remarks>
        public EntityShadowMode? ShadowMode
        {
            get { return this.CommonData.ShadowMode; }
            set { this.CommonData.SetShadowMode(value); }
        }

        /// <summary>Gets or sets an independent copy of the optional opaque proxy graphics cache.</summary>
        /// <remarks>
        /// Null omits the packet; an empty array retains an explicit zero byte count.
        /// The payload is bounded by MaximumProxyGraphicsBytes and is never decoded or executed.
        /// Cloning and geometry operations preserve these stored bytes. Geometry edits do not
        /// regenerate this cache: callers must clear it or supply regenerated data when appropriate.
        /// </remarks>
        public byte[] ProxyGraphics
        {
            get { return this.CommonData.GetProxyGraphics(); }
            set { this.CommonData.SetProxyGraphics(value); }
        }

        /// <summary>Removes the optional proxy graphics cache and its byte-count field.</summary>
        public void ClearProxyGraphics() { this.CommonData.ProxyGraphics = null; }

        /// <summary>Copies optional common entity metadata, including independent proxy bytes, to a clone.</summary>
        /// <param name="copy">The detached entity receiving the common metadata.</param>
        protected void CopyCommonDataTo(Attribute copy)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            this.CommonData.CopyTo(copy.CommonData);
        }
    }
}
