// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Collections
{
    public partial class LayerStateManager
    {
        // One scan of current registered snapshots after all Layer.NameChanged observers.
        // Prepare the complete set before publishing any layer or saved-state key.
        internal Action PrepareLayerRename(Layer target, string newName)
        {
            if (!ReferenceEquals(target.Owner, this.Owner.Layers))
                throw new InvalidOperationException("Layer rename must use its current owning document.");
            var keys = new List<Action>();
            var currents = new List<LayerState>();
            string oldName = target.Name;
            foreach (LayerState state in this.Items)
            {
                string oldKey = null;
                LayerStateProperties selected = null;
                bool collision = false;
                foreach (var entry in state.Properties)
                {
                    bool keyMatches = string.Equals(entry.Key, oldName, StringComparison.OrdinalIgnoreCase);
                    bool valueMatches = entry.Value != null && string.Equals(entry.Value.Name, oldName, StringComparison.OrdinalIgnoreCase);
                    if (keyMatches || valueMatches)
                    {
                        if (!keyMatches || !valueMatches || oldKey != null)
                            throw new InvalidOperationException("A saved layer state has ambiguous or inconsistent layer keys.");
                        oldKey = entry.Key;
                        selected = entry.Value;
                    }
                    if (string.Equals(entry.Key, newName, StringComparison.OrdinalIgnoreCase)) collision = true;
                }
                if (oldKey != null)
                {
                    if (collision) throw new ArgumentException("A saved layer state already contains the new layer key.", nameof(newName));
                    string key = oldKey;
                    LayerStateProperties property = selected;
                    keys.Add(() =>
                    {
                        state.Properties.RekeyWithoutNotifications(key, newName);
                        property.FollowLayerRename(newName);
                    });
                }
                if (string.Equals(state.CurrentLayer, oldName, StringComparison.OrdinalIgnoreCase)) currents.Add(state);
            }
            bool header = string.Equals(this.Owner.DrawingVariables.CLayer, oldName, StringComparison.OrdinalIgnoreCase);
            return () =>
            {
                foreach (Action update in keys) update();
                foreach (LayerState state in currents) state.CurrentLayer = newName;
                if (header) this.Owner.DrawingVariables.CLayer = newName;
            };
        }
    }
}
