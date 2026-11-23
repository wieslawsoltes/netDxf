/// Linear unit format (`$LUNITS` / `DIMLUNIT`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum LinearUnitType {
    Scientific = 1,
    #[default]
    Decimal = 2,
    Engineering = 3,
    Architectural = 4,
    Fractional = 5,
    WindowsDesktop = 6,
}

impl LinearUnitType {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            1 => Self::Scientific,
            2 => Self::Decimal,
            3 => Self::Engineering,
            4 => Self::Architectural,
            5 => Self::Fractional,
            6 => Self::WindowsDesktop,
            _ => return None,
        })
    }
}
