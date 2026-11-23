/// Attribute visibility mode (`$ATTMODE`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum AttMode {
    None = 0,
    #[default]
    Normal = 1,
    All = 2,
}

impl AttMode {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::None,
            1 => Self::Normal,
            2 => Self::All,
            _ => return None,
        })
    }
}
