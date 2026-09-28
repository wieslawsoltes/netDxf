// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Objects
{
    public partial class LayerState
    {
        private bool renaming;

        /// <summary>Publishes the collection name only after all rename observers accept it.</summary>
        /// <remarks>
        /// Revalidates against the current owner after callbacks, preserving the state handle,
        /// snapshots and reference bucket. Recursive renaming of this same object is rejected.
        /// Detachment or adoption performed by a callback is respected; caller side effects,
        /// derived overrides, concurrent mutation and allocation failures are not rolled back.
        /// </remarks>
        protected override void OnNameChangedEvent(string oldName, string newName)
        {
            if (this.renaming)
                throw new InvalidOperationException("Recursive layer-state renaming is not supported.");
            this.renaming = true;
            try
            {
                if (this.Owner != null) this.Owner.ValidateStateRename(this, newName);
                base.OnNameChangedEvent(oldName, newName);
                if (this.Owner != null)
                {
                    this.Owner.ValidateStateRename(this, newName);
                    this.Owner.CommitStateRename(this, newName);
                }
            }
            finally { this.renaming = false; }
        }
    }
}
