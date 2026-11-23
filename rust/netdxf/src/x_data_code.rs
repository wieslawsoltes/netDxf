/// Defines the extended data (XData) group codes (1000..=1071).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum XDataCode {
    String = 1000,
    AppReg = 1001,
    ControlString = 1002,
    LayerName = 1003,
    BinaryData = 1004,
    DatabaseHandle = 1005,
    RealX = 1010,
    RealY = 1020,
    RealZ = 1030,
    WorldSpacePositionX = 1011,
    WorldSpacePositionY = 1021,
    WorldSpacePositionZ = 1031,
    WorldSpaceDisplacementX = 1012,
    WorldSpaceDisplacementY = 1022,
    WorldSpaceDisplacementZ = 1032,
    WorldDirectionX = 1013,
    WorldDirectionY = 1023,
    WorldDirectionZ = 1033,
    Real = 1040,
    Distance = 1041,
    ScaleFactor = 1042,
    Int16 = 1070,
    Int32 = 1071,
}

impl XDataCode {
    pub fn from_i16(code: i16) -> Option<Self> {
        Some(match code {
            1000 => Self::String,
            1001 => Self::AppReg,
            1002 => Self::ControlString,
            1003 => Self::LayerName,
            1004 => Self::BinaryData,
            1005 => Self::DatabaseHandle,
            1010 => Self::RealX,
            1020 => Self::RealY,
            1030 => Self::RealZ,
            1011 => Self::WorldSpacePositionX,
            1021 => Self::WorldSpacePositionY,
            1031 => Self::WorldSpacePositionZ,
            1012 => Self::WorldSpaceDisplacementX,
            1022 => Self::WorldSpaceDisplacementY,
            1032 => Self::WorldSpaceDisplacementZ,
            1013 => Self::WorldDirectionX,
            1023 => Self::WorldDirectionY,
            1033 => Self::WorldDirectionZ,
            1040 => Self::Real,
            1041 => Self::Distance,
            1042 => Self::ScaleFactor,
            1070 => Self::Int16,
            1071 => Self::Int32,
            _ => return None,
        })
    }

    #[inline]
    pub const fn as_i16(self) -> i16 {
        self as i16
    }
}
