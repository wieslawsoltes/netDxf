// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        // Layer-state projections are registered legacy objects, not Objects.Items.
        // Include their newly retained text in the existing pre-output framing gate.
        private void ValidateLayerStateText()
        {
            var manager = this.doc.Layers.StateManager;
            this.ValidateLayerStateXData(manager);
            if (manager.StoredStatesDictionary != null) this.ValidateLayerStateXData(manager.StoredStatesDictionary);
            foreach (LayerState state in manager.Items)
            {
                this.ValidateEntityTextString(state.Name, "Layer-state name");
                this.ValidateEntityTextString(state.Description, "Layer-state description");
                this.ValidateEntityTextString(state.CurrentLayer, "Layer-state current layer");
                this.ValidateLayerStateXData(state);
            }
        }

        private void ValidateLayerStateXData(DxfObject item)
        {
            foreach (XData data in item.XData.Values)
            {
                this.ValidateEntityTextString(data.ApplicationRegistry.Name, "Layer-state XData application");
                foreach (XDataRecord record in data.XDataRecord)
                    if (record.Value is string text) this.ValidateEntityTextString(text, "Layer-state XData");
            }
        }
    }
}
