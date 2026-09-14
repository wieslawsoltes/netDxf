using System;
using System.Collections.Generic;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        /// <summary>Copies a registered object's complete ownership subtree into a destination dictionary.</summary>
        /// <remarks>The owner reference maps to the destination automatically. Other external references require explicit mappings across documents. Existing names are never replaced.</remarks>
        public DxfDatabaseObject CloneObject(DxfDatabaseObject source, DxfDictionary destination, string name,
            IReadOnlyDictionary<DxfObject, DxfObject> externalReferences = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (source.Database == null) throw new ArgumentException("The source must be registered.", nameof(source));
            if (destination.Database != this) throw new ArgumentException("The destination belongs to another database.", nameof(destination));
            DxfDictionary.ValidateName(name);
            if (destination.Contains(name) || destination == this.Root && IsReservedName(name)) throw new ArgumentException("The destination name exists or is reserved.", nameof(name));
            var mappings = new Dictionary<DxfObject, DxfObject>(ObjectIdentity);
            if (externalReferences != null) foreach (KeyValuePair<DxfObject, DxfObject> pair in externalReferences) mappings.Add(pair.Key, pair.Value);
            if (source.Owner != null)
            {
                if (mappings.TryGetValue(source.Owner, out DxfObject supplied) && supplied != destination) throw new ArgumentException("The source owner mapping conflicts with the destination dictionary.", nameof(externalReferences));
                mappings[source.Owner] = destination;
            }
            return this.CloneOwnershipGraph(source, destination, name, false, mappings);
        }
        /// <summary>Adds an independent named page setup under the root ACAD_PLOTSETTINGS dictionary.</summary>
        /// <remarks>The supplied payload is copied. Existing names are not replaced.</remarks>
        public DxfPlotSettingsObject AddPlotSettings(string name, PlotSettings settings)
        {
            DxfDictionary.ValidateName(name);
            var errors = new List<string>(); PlotSettings.ValidateValues(settings, errors);
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors), nameof(settings));
            if (settings.ShadePlotObject != null) this.CheckRegistered(settings.ShadePlotObject);
            DxfDictionary dictionary = this.Root.Contains("ACAD_PLOTSETTINGS") ? this.Root["ACAD_PLOTSETTINGS"] as DxfDictionary : null;
            if (dictionary == null && this.Root.Contains("ACAD_PLOTSETTINGS")) throw new InvalidOperationException("ACAD_PLOTSETTINGS is not a dictionary.");
            var result = new DxfPlotSettingsObject(settings); result.Settings.PageSetupName = name;
            errors.Clear(); PlotSettings.ValidateValues(result.Settings, errors);
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors), nameof(name));
            if (dictionary == null)
            {
                dictionary = new DxfDictionary(); result.PersistentReactors.Add(dictionary); dictionary.Add(name, result); this.Root.Add("ACAD_PLOTSETTINGS", dictionary);
            }
            else { result.PersistentReactors.Add(dictionary); dictionary.Add(name, result); }
            return result;
        }
        /// <summary>Gets the typed root wipeout variables, or null when absent or opaque.</summary>
        public DxfWipeoutVariables GetWipeoutVariables()
        { return this.Root.Contains("ACAD_WIPEOUT_VARS") ? this.Root["ACAD_WIPEOUT_VARS"] as DxfWipeoutVariables : null; }
        /// <summary>Creates or edits the canonical root WIPEOUTVARIABLES frame flag.</summary>
        public DxfWipeoutVariables SetWipeoutVariables(bool displayFrame)
        {
            DxfWipeoutVariables result = this.GetWipeoutVariables();
            if (result == null)
            {
                if (this.Root.Contains("ACAD_WIPEOUT_VARS")) throw new InvalidOperationException("ACAD_WIPEOUT_VARS has an incompatible object.");
                result = new DxfWipeoutVariables { DisplayFrame = displayFrame }; result.PersistentReactors.Add(this.Root); this.Root.Add("ACAD_WIPEOUT_VARS", result);
            }
            else result.DisplayFrame = displayFrame;
            return result;
        }
    }
}
