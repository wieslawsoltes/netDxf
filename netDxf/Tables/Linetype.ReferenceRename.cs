// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Tables
{
    public partial class Linetype
    {
        /// <summary>Commits table indexes only after observers accept the name, using the current owner after callbacks.</summary>
        protected override void OnNameChangedEvent(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("A Linetype name cannot be blank.", nameof(newName));
            if (this.Owner != null) this.Owner.ValidateMLeaderResourceRename(this, newName);
            base.OnNameChangedEvent(oldName, newName);
            if (this.Owner != null)
            {
                this.Owner.ValidateMLeaderResourceRename(this, newName);
                this.Owner.CommitMLeaderResourceRename(this, newName);
            }
        }
    }
}
