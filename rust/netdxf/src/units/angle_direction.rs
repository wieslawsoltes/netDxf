/// Direction for angle measurement (`$ANGDIR`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum AngleDirection {
    #[default]
    CCW = 0,
    CW = 1,
}

impl AngleDirection {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::CCW,
            1 => Self::CW,
            _ => return None,
        })
    }
}
