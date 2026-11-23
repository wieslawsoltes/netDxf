/// Shade plot resolution level (DXF group 77 in `AcDbPlotSettings`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum ShadePlotResolutionMode {
    Draft = 0,
    Preview = 1,
    #[default]
    Normal = 2,
    Presentation = 3,
    Maximum = 4,
    Custom = 5,
}
