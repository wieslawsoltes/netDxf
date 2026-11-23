/// Named-view rendering modes stored in DXF group 281.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum ViewRenderMode {
    #[default]
    TwoDimensionalOptimized = 0,
    Wireframe = 1,
    HiddenLine = 2,
    FlatShaded = 3,
    GouraudShaded = 4,
    FlatShadedWithWireframe = 5,
    GouraudShadedWithWireframe = 6,
}

impl ViewRenderMode {
    pub fn from_i16(v: i16) -> Option<Self> {
        match v {
            0 => Some(Self::TwoDimensionalOptimized),
            1 => Some(Self::Wireframe),
            2 => Some(Self::HiddenLine),
            3 => Some(Self::FlatShaded),
            4 => Some(Self::GouraudShaded),
            5 => Some(Self::FlatShadedWithWireframe),
            6 => Some(Self::GouraudShadedWithWireframe),
            _ => None,
        }
    }
}
