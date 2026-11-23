use bitflags::bitflags;

bitflags! {
    /// Plot layout flags (DXF group 70 in `AcDbPlotSettings`).
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct PlotFlags: i16 {
        const PLOT_VIEWPORT_BORDERS = 1;
        const SHOW_PLOT_STYLES = 2;
        const PLOT_CENTERED = 4;
        const PLOT_HIDDEN = 8;
        const USE_STANDARD_SCALE = 16;
        const PLOT_PLOT_STYLES = 32;
        const SCALE_LINEWEIGHTS = 64;
        const PRINT_LINEWEIGHTS = 128;
        const DRAW_VIEWPORTS_FIRST = 512;
        const MODEL_TYPE = 1024;
        const UPDATE_PAPER = 2048;
        const ZOOM_TO_PAPER_ON_UPDATE = 4096;
        const INITIALIZING = 8192;
        const PREV_PLOT_INIT = 16384;
    }
}
