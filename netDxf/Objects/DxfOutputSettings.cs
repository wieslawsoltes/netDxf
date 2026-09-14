using System;
using System.Collections.Generic;

namespace netDxf.Objects
{
    /// <summary>A standalone PLOTSETTINGS object using the same editable payload as a layout.</summary>
    public sealed class DxfPlotSettingsObject : DxfDatabaseObject
    {
        private readonly PlotSettings settings;
        /// <summary>Creates detached default page-setup settings.</summary>
        public DxfPlotSettingsObject() : this(new PlotSettings()) { }
        /// <summary>Creates detached settings from an independent copy of the supplied payload.</summary>
        public DxfPlotSettingsObject(PlotSettings settings) : base("PLOTSETTINGS")
        { this.settings = (PlotSettings)(settings ?? throw new ArgumentNullException(nameof(settings))).Clone(); }
        /// <summary>Gets this object's independently editable settings.</summary>
        public PlotSettings Settings { get { return this.settings; } }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { if (this.settings.ShadePlotObject != null) yield return this.settings.ShadePlotObject; } }
        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject clone, Func<DxfObject, DxfObject> resolve)
        { ((DxfPlotSettingsObject)clone).Settings.ShadePlotObject = resolve(this.settings.ShadePlotObject); }
        internal override DxfDatabaseObject CloneShell() { return new DxfPlotSettingsObject(this.settings); }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        { PlotSettings.ValidateValues(this.settings, errors); if (!(this.Owner is DxfDictionary)) errors.Add("PLOTSETTINGS requires a dictionary owner."); }
    }
    /// <summary>The WIPEOUTVARIABLES frame-display flag, distinct from newer HEADER frame controls.</summary>
    public sealed class DxfWipeoutVariables : DxfDatabaseObject
    {
        /// <summary>Creates detached variables with frames hidden.</summary>
        public DxfWipeoutVariables() : base("WIPEOUTVARIABLES") { }
        /// <summary>Gets or sets the group-70 display-frame flag.</summary>
        public bool DisplayFrame { get; set; }
        internal override DxfDatabaseObject CloneShell() { return new DxfWipeoutVariables { DisplayFrame = this.DisplayFrame }; }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        { if (!(this.Owner is DxfDictionary)) errors.Add("WIPEOUTVARIABLES requires a dictionary owner."); }
    }
}
