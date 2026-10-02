// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Threading;
using netDxf.Objects;

namespace netDxf.Collections
{
    public partial class LayerStateManager
    {
        /// <summary>Exports a saved layer state without truncating a destination on serialization failure.</summary>
        /// <param name="file">LAS destination in an existing directory.</param>
        /// <param name="layerStateName">Case-insensitive name of an existing saved state.</param>
        /// <param name="cancellationToken">Cancellation before staging and commit.</param>
        /// <remarks>
        /// Uses LayerState.SaveAtomic and its filesystem, cancellation and text-framing contract.
        /// Does not capture current layer settings, restore the state or allocate drawing handles.
        /// The existing Export method retains its non-atomic write behavior.
        /// </remarks>
        public void ExportAtomic(string file, string layerStateName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (layerStateName == null) throw new ArgumentNullException(nameof(layerStateName));
            if (!this.List.TryGetValue(layerStateName, out LayerState state))
                throw new ArgumentException("Invalid layer state name.", nameof(layerStateName));
            state.SaveAtomic(file, cancellationToken);
        }
    }
}
