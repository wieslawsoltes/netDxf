/// Raster image resolution units.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum ImageResolutionUnits {
    #[default]
    NoUnits = 0,
    Centimeters = 2,
    Inches = 5,
}

impl ImageResolutionUnits {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::NoUnits,
            2 => Self::Centimeters,
            5 => Self::Inches,
            _ => return None,
        })
    }
}
