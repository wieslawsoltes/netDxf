// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    /// <summary>Controls casting and receiving shadows (DXF 2007 and later).</summary>
    /// <remarks>Retained for compatibility with products predating AutoCAD 2016.</remarks>
    public enum EntityShadowMode : short
    {
        /// <summary>Casts and receives shadows.</summary>
        CastAndReceive = 0,
        /// <summary>Casts shadows.</summary>
        Cast = 1,
        /// <summary>Receives shadows.</summary>
        Receive = 2,
        /// <summary>Ignores shadows.</summary>
        Ignore = 3
    }

    internal sealed class CommonEntityData
    {
        internal string ColorName;
        internal EntityShadowMode? ShadowMode;
        internal byte[] ProxyGraphics;
        internal void SetShadowMode(EntityShadowMode? value)
        {
            if (value.HasValue && (value.Value < EntityShadowMode.CastAndReceive || value.Value > EntityShadowMode.Ignore))
                throw new ArgumentOutOfRangeException(nameof(value));
            this.ShadowMode = value;
        }
        internal void SetProxyGraphics(byte[] value)
        {
            if (value != null && value.Length > EntityObject.MaximumProxyGraphicsBytes)
                throw new ArgumentOutOfRangeException(nameof(value), "Proxy graphics exceed the 16 MiB limit.");
            this.ProxyGraphics = value == null ? null : (byte[])value.Clone();
        }
        internal byte[] GetProxyGraphics() { return this.ProxyGraphics == null ? null : (byte[])this.ProxyGraphics.Clone(); }
        internal void CopyTo(CommonEntityData target)
        {
            target.ColorName = this.ColorName;
            target.ShadowMode = this.ShadowMode;
            target.SetProxyGraphics(this.ProxyGraphics);
        }
    }

    public abstract partial class EntityObject
    {
        internal CommonEntityData CommonData { get; } = new CommonEntityData();

        /// <summary>Maximum accepted opaque proxy graphics payload, in bytes (16 MiB).</summary>
        public const int MaximumProxyGraphicsBytes = 16 * 1024 * 1024;

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
        protected void CopyCommonDataTo(EntityObject copy)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            this.CommonData.CopyTo(copy.CommonData);
        }
    }
}
