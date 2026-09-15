using System.Collections.Generic;
using System.Linq;
using netDxf.Collections;

namespace netDxf.Tables
{
    public partial class ApplicationRegistry
    {
        private readonly HashSet<XDataDictionary> xdataBindings = new HashSet<XDataDictionary>();
        internal void AttachXData(XDataDictionary dictionary) { this.xdataBindings.Add(dictionary); }
        internal void DetachXData(XDataDictionary dictionary) { this.xdataBindings.Remove(dictionary); }

        /// <summary>Updates internal indexes only after public name observers accept the change.</summary>
        protected override void OnNameChangedEvent(string oldName, string newName)
        {
            this.ValidateBindingRename(newName);
            base.OnNameChangedEvent(oldName, newName);
            // Observers may add/remove owners or XData; validate the current bindings again.
            this.ValidateBindingRename(newName);
            XDataDictionary[] bindings = this.xdataBindings.ToArray();
            if (this.Owner != null) this.Owner.CommitRecordRename(this, newName);
            foreach (XDataDictionary dictionary in bindings) dictionary.CommitApplicationRegistryRename(this, newName);
        }
        private void ValidateBindingRename(string newName)
        {
            if (this.Owner != null) this.Owner.ValidateRecordRename(this, newName);
            foreach (XDataDictionary dictionary in this.xdataBindings)
                dictionary.ValidateApplicationRegistryRename(this, newName);
        }
    }
}
