/// Fraction format type for architectural and fractional dimensions.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum FractionFormatType {
    #[default]
    Horizontal = 0,
    Diagonal = 1,
    NotStacked = 2,
}

impl FractionFormatType {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::Horizontal,
            1 => Self::Diagonal,
            2 => Self::NotStacked,
            _ => return None,
        })
    }
}
