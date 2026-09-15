using System;
using System.Collections.Generic;
using netDxf.Header;
using netDxf.Tables;
using netDxf.Entities;

namespace netDxf.Objects
{
    internal static class SunReferences
    {
        internal static bool IsHost(DxfObject host) { return host is VPort || host is View || host is Viewport; }
        internal static DxfDatabaseObject Get(DxfObject host)
        { return host is VPort vport ? vport.Sun : host is View view ? view.Sun : (host as Viewport)?.Sun; }
        internal static bool IsPresent(DxfObject host)
        { return host is VPort vport ? vport.SunHandlePresent : host is View view ? view.SunHandlePresent : host is Viewport viewport && viewport.SunHandlePresent; }
        internal static void Set(DxfObject host, DxfDatabaseObject sun, bool present = true)
        {
            if (host is VPort vport) { vport.Sun = sun; vport.SunHandlePresent = present; }
            else if (host is View view) { view.Sun = sun; view.SunHandlePresent = present; }
            else if (host is Viewport viewport) { viewport.Sun = sun; viewport.SunHandlePresent = present; }
            else throw new ArgumentException("SUN requires a VIEW, VPORT or VIEWPORT owner.", nameof(host));
        }
        internal static void CheckProfile(DxfObject host, DxfVersion version)
        {
            if (!IsHost(host)) throw new ArgumentException("SUN requires a VIEW, VPORT or VIEWPORT owner.", nameof(host));
            if (version < (host is View ? DxfVersion.AutoCad2010 : DxfVersion.AutoCad2007))
                throw new NotSupportedException("SUN requires R2007 or later; named VIEW ownership requires R2010 or later.");
        }
        internal static void CheckClone(DxfObject host)
        { if (Get(host) != null) throw new NotSupportedException("Clone the SUN ownership subtree explicitly with DxfObjectDatabase.CloneSun into a registered destination host."); }
        internal static void Validate(DxfObject host, DxfObjectDatabase database, List<string> errors)
        {
            if (!IsHost(host) || !IsPresent(host)) return;
            try { CheckProfile(host, database.Document.DrawingVariables.AcadVer); }
            catch (NotSupportedException error) { errors.Add(error.Message); }
            DxfDatabaseObject sun = Get(host);
            if (sun != null && (!(sun is DxfSun || sun is DxfOpaqueObject && sun.CodeName == "SUN") || !database.IsRegistered(sun) || !ReferenceEquals(sun.Owner, host)))
                errors.Add("Invalid reciprocal SUN ownership: " + host.Handle);
        }
    }
}
namespace netDxf.Tables
{
    public partial class VPort
    {
        /// <summary>Gets the owned SUN object, or null. Use Objects.SetSun, CloneSun and EraseOwnedTree to change the attachment.</summary>
        public netDxf.Objects.DxfDatabaseObject Sun { get; internal set; }
        internal bool SunHandlePresent { get; set; }
    }
    public partial class View
    {
        /// <summary>Gets the owned SUN object, or null. Use Objects.SetSun, CloneSun and EraseOwnedTree to change the attachment.</summary>
        public netDxf.Objects.DxfDatabaseObject Sun { get; internal set; }
        internal bool SunHandlePresent { get; set; }
    }
}
namespace netDxf.Entities
{
    public partial class Viewport
    {
        /// <summary>Gets the owned SUN object, or null. Use Objects.SetSun, CloneSun and EraseOwnedTree to change the attachment.</summary>
        public netDxf.Objects.DxfDatabaseObject Sun { get; internal set; }
        internal bool SunHandlePresent { get; set; }
    }
}
