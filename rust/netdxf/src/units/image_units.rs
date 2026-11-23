/// Raster image units.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum ImageUnits {
    #[default]
    Unitless = 0,
    Millimeters = 1,
    Centimeters = 2,
    Meters = 3,
    Kilometers = 4,
    Inches = 5,
    Feet = 6,
    Yards = 7,
    Miles = 8,
}

impl ImageUnits {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::Unitless,
            1 => Self::Millimeters,
            2 => Self::Centimeters,
            3 => Self::Meters,
            4 => Self::Kilometers,
            5 => Self::Inches,
            6 => Self::Feet,
            7 => Self::Yards,
            8 => Self::Miles,
            _ => return None,
        })
    }
}
