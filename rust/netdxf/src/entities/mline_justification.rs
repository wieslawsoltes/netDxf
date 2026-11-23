/// Multi-line justification (`MLineJustification`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum MLineJustification {
    #[default]
    Top = 0,
    Zero = 1,
    Bottom = 2,
}

impl MLineJustification {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::Top,
            1 => Self::Zero,
            2 => Self::Bottom,
            _ => return None,
        })
    }
}
