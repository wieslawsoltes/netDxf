/// Point display mode (`$PDMODE`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum PointShape {
    #[default]
    Dot = 0,
    Empty = 1,
    Plus = 2,
    Cross = 3,
    Line = 4,
    CircleDot = 32,
    CircleEmpty = 33,
    CirclePlus = 34,
    CircleCross = 35,
    CircleLine = 36,
    SquareDot = 64,
    SquareEmpty = 65,
    SquarePlus = 66,
    SquareCross = 67,
    SquareLine = 68,
    CircleSquareDot = 96,
    CircleSquareEmpty = 97,
    CircleSquarePlus = 98,
    CircleSquareCross = 99,
    CircleSquareLine = 100,
}

impl PointShape {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::Dot,
            1 => Self::Empty,
            2 => Self::Plus,
            3 => Self::Cross,
            4 => Self::Line,
            32 => Self::CircleDot,
            33 => Self::CircleEmpty,
            34 => Self::CirclePlus,
            35 => Self::CircleCross,
            36 => Self::CircleLine,
            64 => Self::SquareDot,
            65 => Self::SquareEmpty,
            66 => Self::SquarePlus,
            67 => Self::SquareCross,
            68 => Self::SquareLine,
            96 => Self::CircleSquareDot,
            97 => Self::CircleSquareEmpty,
            98 => Self::CircleSquarePlus,
            99 => Self::CircleSquareCross,
            100 => Self::CircleSquareLine,
            _ => return None,
        })
    }
}
