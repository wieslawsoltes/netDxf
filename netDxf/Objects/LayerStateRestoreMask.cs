// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Objects
{
    /// <summary>Persisted LAS/DXF group 91 layer-state mask. These values differ from LayerPropertiesRestoreFlags.</summary>
    /// <remarks>The viewport and plot-style bits are retained; this library does not apply viewport overrides or plot styles.</remarks>
    [Flags]
    public enum LayerStateRestoreMask
    {
        None = 0,
        On = 1,
        Frozen = 2,
        Locked = 4,
        Plot = 8,
        NewViewport = 16,
        Color = 32,
        Linetype = 64,
        Lineweight = 128,
        PlotStyle = 256,
        CurrentViewport = 512,
        Transparency = 1024,
        All = 2047,
        HiddenState = 32768,
        LastRestored = 65536
    }
}
