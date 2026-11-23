/// Orthographic type identifiers used by UCS origin pairs (DXF group 71).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum UcsOrthographicType {
    Top = 1,
    Bottom = 2,
    Front = 3,
    Back = 4,
    Left = 5,
    Right = 6,
}

impl UcsOrthographicType {
    pub fn from_i16(v: i16) -> Option<Self> {
        match v {
            1 => Some(Self::Top),
            2 => Some(Self::Bottom),
            3 => Some(Self::Front),
            4 => Some(Self::Back),
            5 => Some(Self::Left),
            6 => Some(Self::Right),
            _ => None,
        }
    }
}
