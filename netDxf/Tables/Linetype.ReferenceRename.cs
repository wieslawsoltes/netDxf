// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Tables
{
    public partial class Linetype
    {
        /// <summary>Commits table indexes and typed saved-layer-state linetype names after observers accept the name.</summary>
        /// <remarks>Uses the current owner and snapshot values after callbacks; also follows a matching CELTYPE.
        /// Snapshot properties, aliases, other settings and identifiers are preserved. Caller callback side effects,
        /// concurrent mutation, detached snapshots, cross-document aliases and private name payloads are not transactional.</remarks>
        protected override void OnNameChangedEvent(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("A Linetype name cannot be blank.", nameof(newName));
            if (this.Owner != null) this.Owner.ValidateMLeaderResourceRename(this, newName);
            base.OnNameChangedEvent(oldName, newName);
            if (this.Owner != null)
            {
                this.Owner.ValidateMLeaderResourceRename(this, newName);
                Action updateStates = this.Owner.Owner.Layers.StateManager.PrepareLinetypeRename(this, newName);
                this.Owner.CommitMLeaderResourceRename(this, newName);
                updateStates();
            }
        }
    }
}
