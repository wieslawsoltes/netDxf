// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Blocks
{
    public partial class Block
    {
        /// <summary>Commits table indexes only after observers accept the name, using the current owner after callbacks.</summary>
        protected override void OnNameChangedEvent(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("A Block name cannot be blank.", nameof(newName));
            if (this.Record.Owner != null) this.Record.Owner.ValidateMLeaderResourceRename(this, newName);
            base.OnNameChangedEvent(oldName, newName);
            if (this.Record.Owner != null)
            {
                this.Record.Owner.ValidateMLeaderResourceRename(this, newName);
                this.Record.Owner.CommitMLeaderResourceRename(this, newName);
            }
            this.Record.Name = newName;
        }
    }
}
