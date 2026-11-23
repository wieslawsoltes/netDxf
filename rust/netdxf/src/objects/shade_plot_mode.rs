/// Shade plot mode (DXF group 76 in `AcDbPlotSettings`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum ShadePlotMode {
    #[default]
    AsDisplayed = 0,
    Wireframe = 1,
    Hidden = 2,
    Rendered = 3,
}
