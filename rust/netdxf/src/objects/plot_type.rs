/// Plot area type (DXF group 74 in `AcDbPlotSettings`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum PlotType {
    LastScreenDisplay = 0,
    DrawingExtents = 1,
    DrawingLimits = 2,
    View = 3,
    Window = 4,
    #[default]
    LayoutInformation = 5,
}
