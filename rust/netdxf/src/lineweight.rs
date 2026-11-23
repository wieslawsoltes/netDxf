/// Represents the line weight of a layer or an entity in 1/100 mm.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum Lineweight {
    Default = -3,
    ByBlock = -2,
    #[default]
    ByLayer = -1,
    W0 = 0,
    W5 = 5,
    W9 = 9,
    W13 = 13,
    W15 = 15,
    W18 = 18,
    W20 = 20,
    W25 = 25,
    W30 = 30,
    W35 = 35,
    W40 = 40,
    W50 = 50,
    W53 = 53,
    W60 = 60,
    W70 = 70,
    W80 = 80,
    W90 = 90,
    W100 = 100,
    W106 = 106,
    W120 = 120,
    W140 = 140,
    W158 = 158,
    W200 = 200,
    W211 = 211,
}

impl Lineweight {
    pub fn from_i16(value: i16) -> Option<Self> {
        Some(match value {
            -3 => Self::Default,
            -2 => Self::ByBlock,
            -1 => Self::ByLayer,
            0 => Self::W0,
            5 => Self::W5,
            9 => Self::W9,
            13 => Self::W13,
            15 => Self::W15,
            18 => Self::W18,
            20 => Self::W20,
            25 => Self::W25,
            30 => Self::W30,
            35 => Self::W35,
            40 => Self::W40,
            50 => Self::W50,
            53 => Self::W53,
            60 => Self::W60,
            70 => Self::W70,
            80 => Self::W80,
            90 => Self::W90,
            100 => Self::W100,
            106 => Self::W106,
            120 => Self::W120,
            140 => Self::W140,
            158 => Self::W158,
            200 => Self::W200,
            211 => Self::W211,
            _ => return None,
        })
    }

    #[inline]
    pub const fn as_i16(self) -> i16 {
        self as i16
    }
}
