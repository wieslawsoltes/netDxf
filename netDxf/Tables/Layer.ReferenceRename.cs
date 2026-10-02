// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Tables
{
    public partial class Layer
    {
        private bool renamingLayer;

        /// <summary>Commits layer indexes and typed saved-state names after rename observers accept.</summary>
        /// <remarks>
        /// Follows matching CLAYER and saved CurrentLayer values, rekeys matching saved properties
        /// without replacing them, and preserves handles, reference buckets and captured settings.
        /// Snapshot membership is unchanged, so this internal rekey does not emit dictionary add/remove
        /// events. Active property-key enumerators may be invalidated. Ambiguous/mismatched affected
        /// keys reject before publication. Callback side effects, derived overrides, concurrency,
        /// allocation failure, detached snapshots and cross-document property aliases are not rolled back.
        /// </remarks>
        protected override void OnNameChangedEvent(string oldName, string newName)
        {
            if (this.renamingLayer) throw new InvalidOperationException("Recursive layer renaming is not supported.");
            this.renamingLayer = true;
            try
            {
                if (this.Owner != null) this.Owner.ValidateLayerRename(this, newName);
                base.OnNameChangedEvent(oldName, newName);
                if (this.Owner != null)
                {
                    this.Owner.ValidateLayerRename(this, newName);
                    Action updateStates = this.Owner.StateManager.PrepareLayerRename(this, newName);
                    this.Owner.CommitLayerRename(this, newName);
                    updateStates();
                }
            }
            finally { this.renamingLayer = false; }
        }
    }
}
